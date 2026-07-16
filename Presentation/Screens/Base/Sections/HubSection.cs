using ExoProxy.Core;
using ExoProxy.Data;
using ExoProxy.Data.Mission;

namespace ExoProxy.Presentation.Screens.Base.Sections;

public sealed class HubSection : IBaseSection
{
    public string SectionId => SectionIds.Hub;
    public BaseSectionResponse Response { get; private set; } = new(BaseSectionRequest.Stay, null);

    private readonly OperatorAccount _account;
    private readonly OperatorProgress _progress;
    private readonly IAudioService _audio;
    private readonly bool _devMode;
    private readonly CommsRepository? _commsRepo;
    private readonly MemoryRepository? _memRepo;
    private readonly RoverStats? _roverStats;
    private readonly MissionWorld? _world;
    private string _input = "";
    private string _message = "";
    private bool _messageIsError;
    private bool _helpVisible;
    private BlinkState _blink;

    // Set when the operator asks to deploy below the safe charge: they must
    // confirm leaving for the field with a low battery.
    private bool _awaitingFieldDeploy;
    private const int LowChargeThreshold = 30;

    private const int BoxWidth = 64;
    private const int BoxInner = BoxWidth - 2;

    private static readonly (string Cmd, string Desc, bool IsSystem)[] _baseCommands =
    [
        ("MISSION",    "Begin new mission",                  false),
        ("MEMORY",     "Memory management & save data",      false),
        ("COMMS",      "Correspondence & messages",           false),
        ("UPGRADE",    "Upgrade rover systems",          false),
        ("DIAG",       "Rover diagnostics",                  false),
        ("SHOWHP",     "Show rover current health",          false),
        ("LOGOUT",     "End session — return to login",      true),
        ("EXIT",       "Power down terminal",                true),
        ("/HELP",      "Show help panel",                    true),
        ("/SETTINGS",  "System settings",                    true),
    ];

    // Visible in /help and autocomplete only when logged in as DEV.
    private static readonly (string Cmd, string Desc, bool IsSystem)[] _devCommands =
    [
        ("SOL",        "DEV: SOL 5 / SOL +1 / SOL -1",       true),
        ("GOTO",       "DEV: jump to section, GOTO COMMS",   true),
        ("RELOAD",     "DEV: re-read content YAML + sound bank", true),
        ("SFX",        "DEV: audition a sound, SFX rover.dock", true),
        ("RESET",      "DEV: wipe DEV saves, back to SOL 1", true),
        ("FUNDS",      "DEV: add funds to account", true),
        ("DMG",        "DEV: damage rover, DMG / DMG 25",    true),
        ("RESETHP",    "DEV: restore rover HP to maximum",   true),
        ("RESETUPGRADE","DEV: reset rover upgrades",         true),
    ];

    private readonly (string Cmd, string Desc, bool IsSystem)[] _commands;

    public HubSection(OperatorAccount account, OperatorProgress progress, IAudioService audio,
                      string? loadWarning = null, bool devMode = false, CommsRepository? commsRepo = null,
                      MemoryRepository? memRepo = null, RoverStats? roverStats = null, MissionWorld? world = null)
    {
        _account   = account;
        _progress  = progress;
        _audio     = audio;
        _devMode   = devMode;
        _commsRepo = commsRepo;
        _memRepo   = memRepo;
        _roverStats = roverStats;
        _world      = world;
        _commands  = devMode ? [.. _baseCommands, .. _devCommands] : _baseCommands;

        // Save-integrity warnings surface here, in the system's own voice.
        if (!string.IsNullOrEmpty(loadWarning))
        {
            _message        = loadWarning;
            _messageIsError = true;
        }
    }

    public void Update(GameTime time, InputEvent? input)
    {
        var now = time.Total;
        _blink.Update(now);

        Response = new(BaseSectionRequest.Stay, null);

        if (input is null) return;

        var key = input.Value.Key;

        // Low-charge deployment confirmation takes over input until answered.
        if (_awaitingFieldDeploy)
        {
            if (key.Key == ConsoleKey.Y)
            {
                _awaitingFieldDeploy = false;
                Response = new BaseSectionResponse(BaseSectionRequest.GoToSection, SectionIds.Mission);
            }
            else if (key.Key is ConsoleKey.N or ConsoleKey.Escape)
            {
                _awaitingFieldDeploy = false;
                _message        = "DEPLOYMENT HELD — CHARGING";
                _messageIsError = false;
            }
            return;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (_input.Length > 0) _input = _input[..^1];
            _message = "";
            return;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            _input       = "";
            _message     = "";
            _helpVisible = false;
            return;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            HandleCommand();
            return;
        }

        if (key.KeyChar != '\0' && _input.Length < 48)
        {
            _input   += char.ToUpper(key.KeyChar);
            _message  = "";
            // Keypress click is fired globally by the game loop, not here.
        }
    }

    private void HandleCommand()
    {
        var cmd = _input.Trim();
        _input = "";

        if (_devMode && HandleDevCommand(cmd))
            return;

        if (cmd == "/HELP")
        {
            _helpVisible = !_helpVisible;
            _message     = "";
            return;
        }

        if (cmd == "/SETTINGS")
        {
            Response = new BaseSectionResponse(BaseSectionRequest.GoToSection, SectionIds.Settings);
            return;
        }

        if (cmd == "LOGOUT")
        {
            Response = new BaseSectionResponse(BaseSectionRequest.Logout, null);
            return;
        }

        if (cmd == "EXIT")
        {
            Response = new BaseSectionResponse(BaseSectionRequest.ExitGame, null);
            return;
        }

        if (cmd == "MEMORY")
        {
            Response = new BaseSectionResponse(BaseSectionRequest.GoToSection, SectionIds.Memory);
            return;
        }

        if (cmd == "COMMS")
        {
            Response = new BaseSectionResponse(BaseSectionRequest.GoToSection, SectionIds.Comms);
            return;
        }
        if (cmd == "UPGRADE")
        {
            Response = new BaseSectionResponse(BaseSectionRequest.GoToSection, SectionIds.Upgrade);
            return;
        }
        
        if (cmd == "DIAG")
        {
            Response = new BaseSectionResponse(BaseSectionRequest.GoToSection, SectionIds.Diag);
            return;
        }

        if (cmd == "SHOWHP")
        {
            if (_roverStats is null)
            {
                _message        = "ROVER HEALTH DATA UNAVAILABLE";
                _messageIsError = true;
                return;
            }

            _audio.Play("command.accept");
            _message        = $"ROVER HP: {_roverStats.CurrentHealth}/{_roverStats.MaxHealth}";
            _messageIsError = false;
            return;
        }

        if (cmd == "MISSION")
        {
            // Deploying below the safe charge needs a confirmation — once in
            // the field the rover can't come back to top up.
            if (_world is not null && !_world.IsFull && _world.PercentDisplay < LowChargeThreshold)
            {
                _awaitingFieldDeploy = true;
                return;
            }
            Response = new BaseSectionResponse(BaseSectionRequest.GoToSection, SectionIds.Mission);
            return;
        }

        _message        = $"Unknown command: {cmd}";
        _messageIsError = true;
        _helpVisible    = false;
        _audio.Play("command.error");
    }

    // Testing commands available only when logged in as DEV. Returns true
    // when the command was recognized and handled.
    private bool HandleDevCommand(string cmd)
    {
        if (cmd == "SOL" || cmd.StartsWith("SOL "))
        {
            string arg = cmd.Length > 3 ? cmd[4..].Trim() : "";
            int newSol;

            if (arg.StartsWith('+') && int.TryParse(arg[1..], out int inc))
                newSol = _progress.Sol + inc;
            else if (arg.StartsWith('-') && int.TryParse(arg[1..], out int dec))
                newSol = _progress.Sol - dec;
            else if (int.TryParse(arg, out int abs))
                newSol = abs;
            else
            {
                _message        = "Usage: SOL 5  |  SOL +1  |  SOL -1";
                _messageIsError = true;
                return true;
            }

            _progress.Sol = Math.Max(1, newSol);
            _progress.Save();
            _message        = $"[DEV] Sol counter set — {_progress.SolDisplay}";
            _messageIsError = false;
            return true;
        }

        if (cmd == "GOTO" || cmd.StartsWith("GOTO "))
        {
            string target = cmd.Length > 4 ? cmd[5..].Trim().ToLowerInvariant() : "";

            if (target is SectionIds.Comms or SectionIds.Memory or SectionIds.Settings
                or SectionIds.Upgrade or SectionIds.Diag or SectionIds.Mission)
            {
                Response = new BaseSectionResponse(BaseSectionRequest.GoToSection, target);
                return true;
            }

            _message        = "Usage: GOTO COMMS | MEMORY | SETTINGS | UPGRADE | DIAG | MISSION";
            _messageIsError = true;
            return true;
        }

        if (cmd == "RELOAD")
        {
            // Re-reads shipped content (messages.yaml, memory_files.yaml) so
            // writers can iterate without restarting the game. Per-operator
            // state files are re-read too, but those rarely change on disk.
            _commsRepo?.Load(_account.Login);
            _memRepo?.Load(_account.Login);
            _audio.Reload();
            _message        = "[DEV] Content + sound bank reloaded from disk";
            _messageIsError = false;
            return true;
        }

        if (cmd == "SFX" || cmd.StartsWith("SFX "))
        {
            string arg = cmd.Length > 3 ? cmd[4..].Trim() : "";
            if (arg.Length == 0)
            {
                _message        = "Usage: SFX rover.dock";
                _messageIsError = true;
                return true;
            }
            _audio.Play(arg);   // soundbank lookup is case-insensitive
            _message        = $"[DEV] SFX -> {arg}";
            _messageIsError = false;
            return true;
        }

        if (cmd == "RESET")
        {
            string saveDir = Path.Combine(AppContext.BaseDirectory, "SaveData");
            if (Directory.Exists(saveDir))
                foreach (var file in Directory.GetFiles(saveDir, "*_DEV.yaml"))
                    File.Delete(file);

            _progress.Sol = 1;
            _commsRepo?.Load(_account.Login);
            _memRepo?.Load(_account.Login);
            _world?.Reset();
            _message        = "[DEV] DEV save data wiped — back to SOL 001";
            _messageIsError = false;
            return true;
        }

        if (cmd == "FUNDS" || cmd.StartsWith("FUNDS "))
        {
            string arg = cmd.Length > 5 ? cmd[6..].Trim() : "";
            if (int.TryParse(arg, out int amount))
            {
                _account.Funds += amount;
                _message        = $"[DEV] Added {amount} funds to account";
                _messageIsError = false;
            }
            else
            {
                _message        = "Usage: Funds 1000";
                _messageIsError = true;
            }

            return true;
        }

        if (cmd == "DMG" || cmd.StartsWith("DMG "))
        {
            if (_roverStats is null)
            {
                _message        = "[DEV] Rover stats unavailable";
                _messageIsError = true;
                return true;
            }

            string arg = cmd.Length > 3 ? cmd[4..].Trim() : "";
            int amount = 10;

            if (arg.Length > 0 && !int.TryParse(arg, out amount))
            {
                _message        = "Usage: DMG  |  DMG 25";
                _messageIsError = true;
                return true;
            }

            if (amount < 0)
            {
                _message        = "Damage must be zero or greater";
                _messageIsError = true;
                return true;
            }

            _roverStats.CurrentHealth = Math.Max(0, _roverStats.CurrentHealth - amount);
            _roverStats.Save();
            _message        = $"[DEV] Rover damaged by {amount} — HP {_roverStats.CurrentHealth}/{_roverStats.MaxHealth}";
            _messageIsError = false;
            return true;
        }

        if (cmd == "RESETHP")
        {
            if (_roverStats is null)
            {
                _message        = "[DEV] Rover stats unavailable";
                _messageIsError = true;
                return true;
            }

            _roverStats.CurrentHealth = _roverStats.MaxHealth;
            _roverStats.ProcessedDamageThresholds = 0;
            _roverStats.Save();
            _message        = $"[DEV] Rover HP restored — HP {_roverStats.CurrentHealth}/{_roverStats.MaxHealth}";
            _messageIsError = false;
            return true;
        }

        if (cmd == "RESETUPGRADE")
        {
            if (_roverStats is null)
            {
                _message        = "[DEV] Rover stats unavailable";
                _messageIsError = true;
                return true;
            }

            _roverStats.ResetUpgradesToDefaults();
            _roverStats.ProcessedDamageThresholds = RoverElectronicsFailureChooser.GetDamageThresholdCount(_roverStats);
            _roverStats.Save();
            _message        = "[DEV] Rover upgrades reset to defaults";
            _messageIsError = false;
            return true;
        }

        return false;
    }

    private (string Cmd, string Desc, bool IsSystem)[] GetSuggestions()
    {
        if (_input.Length == 0) return [];
        return _commands
            .Where(c => c.Cmd.StartsWith(_input, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public void Render(IRenderBuffer buffer)
    {
        int left = (buffer.Width - BoxWidth) / 2;
        int centerY = buffer.Height / 2 - 1;

        // ── autocomplete suggestions (above box) ──────────────────────────────
        var suggestions = GetSuggestions();
        if (suggestions.Length > 0 && !_helpVisible)
        {
            int suggestTop = Math.Max(0, centerY - suggestions.Length - 1);
            for (int i = 0; i < suggestions.Length; i++)
            {
                var (cmd, desc, _) = suggestions[i];
                string line = $"  {cmd.PadRight(12)}{desc}";
                buffer.WriteAt(left, suggestTop + i, line, ExoColors.ProksText);
            }
            buffer.WriteAt(left, centerY - 1, new string('─', BoxWidth), ExoColors.ProksBorder);
        }

        // ── search box ────────────────────────────────────────────────────────
        buffer.WriteAt(left, centerY, "┌" + new string('─', BoxInner) + "┐", ExoColors.ProksBorder);

        string inputDisplay = "> " + _input;
        string paddedInput = inputDisplay.PadRight(BoxInner);
        buffer.WriteAt(left, centerY + 1, "│", ExoColors.ProksBorder);
        buffer.WriteAt(left + 1, centerY + 1, paddedInput, ExoColors.PhosphorText);
        if (_blink.Visible)
            buffer.WriteAt(left + 1 + inputDisplay.Length, centerY + 1, "_", ExoColors.PhosphorDim);
        buffer.WriteAt(left + BoxWidth - 1, centerY + 1, "│", ExoColors.ProksBorder);

        buffer.WriteAt(left, centerY + 2, "└" + new string('─', BoxInner) + "┘", ExoColors.ProksBorder);

        // ── /help hint or error ───────────────────────────────────────────────
        if (_awaitingFieldDeploy && _world is not null)
        {
            string prompt = $"LOW CHARGE ({_world.PercentDisplay}%) — PROCEED TO FIELD? Y/N";
            int promptX = (buffer.Width - prompt.Length) / 2;
            buffer.WriteAt(promptX, centerY + 4, prompt, ExoColors.SignalBright);
        }
        else if (!string.IsNullOrEmpty(_message))
        {
            string color = _messageIsError ? ExoColors.FaultText : ExoColors.ProksPale;
            int msgX = (buffer.Width - _message.Length) / 2;
            buffer.WriteAt(msgX, centerY + 4, _message, color);
        }
        else if (!_helpVisible)
        {
            const string hint = "/HELP";
            buffer.WriteAt(left + BoxWidth - hint.Length, centerY + 4, hint, ExoColors.ProksDark);
        }

        // ── help panel ────────────────────────────────────────────────────────
        if (_helpVisible)
        {
            int panelTop = centerY + 4;
            buffer.WriteAt(left, panelTop, "┌" + new string('─', BoxInner) + "┐", ExoColors.ProksBorder);

            int row = panelTop + 1;
            foreach (var (cmd, desc, isSystem) in _commands)
            {
                string cmdColor = isSystem ? ExoColors.ProksPale : ExoColors.PhosphorText;
                string line = $"  {cmd.PadRight(12)}{desc}";
                buffer.WriteAt(left, row, "│", ExoColors.ProksBorder);
                buffer.WriteAt(left + 1, row, line.PadRight(BoxInner), cmdColor);
                buffer.WriteAt(left + BoxWidth - 1, row, "│", ExoColors.ProksBorder);
                row++;
            }

            buffer.WriteAt(left, row, "└" + new string('─', BoxInner) + "┘", ExoColors.ProksBorder);
        }

        // ── status bar ────────────────────────────────────────────────────────
        int statusY = buffer.Height - 2;
        buffer.WriteAt(left, statusY, $"OPERATOR: {_account.Login}", ExoColors.ProksPale);
        string sol = _progress.SolDisplay;
        buffer.WriteAt(left + BoxWidth - sol.Length, statusY, sol, ExoColors.SignalText);

        // Rover charge ticks up here while docked — centred between operator and SOL.
        if (_world is not null)
        {
            string charge = _world.IsFull ? "POWER 100%" : $"CHARGING {_world.PercentDisplay}%";
            string color  = _world.IsFull ? ExoColors.SignalText : ExoColors.SignalBright;
            buffer.WriteAt(left + (BoxWidth - charge.Length) / 2, statusY, charge, color);
        }
    }
}
