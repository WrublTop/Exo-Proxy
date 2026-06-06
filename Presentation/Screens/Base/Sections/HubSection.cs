using ExoProxy.Core;
using ExoProxy.Data;

namespace ExoProxy.Presentation.Screens.Base.Sections;

public sealed class HubSection : IBaseSection
{
    public string SectionId => "hub";
    public BaseSectionResponse Response { get; private set; } = new(BaseSectionRequest.Stay, null);

    private readonly OperatorAccount _account;
    private readonly GameSettings    _settings;
    private string _input = "";
    private string _message = "";
    private bool _messageIsError;
    private bool _helpVisible;
    private bool _blinkVisible = true;
    private DateTimeOffset _blinkTimer;
    private const int BlinkMs = 500;

    private const int BoxWidth   = 64;
    private const int BoxInner   = BoxWidth - 2;

    private static readonly (string Cmd, string Desc, bool IsSystem)[] _commands =
    [
        ("MISSION",    "Begin new mission",                  false),
        ("MEMORY",     "Memory management & save data",      false),
        ("COMMS",      "Correspondence & messages",           false),
        ("SYNC",       "Sync data / transmit to SUIRDC",     false),
        ("DIAG",       "Rover diagnostics",                  false),
        ("/HELP",      "Show help panel",                    true),
        ("/SETTINGS",  "System settings",                    true),
    ];

    public HubSection(OperatorAccount account, GameSettings settings)
    {
        _account    = account;
        _settings   = settings;
        _blinkTimer = DateTimeOffset.UtcNow;
    }

    public void Update(DateTimeOffset now, InputEvent? input)
    {
        Response = new(BaseSectionRequest.Stay, null);

        if (now - _blinkTimer >= TimeSpan.FromMilliseconds(BlinkMs))
        {
            _blinkVisible = !_blinkVisible;
            _blinkTimer   = now;
        }

        if (input is null) return;

        var key = input.Value.Key;

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

        if (key.KeyChar != '\0' && _input.Length < 16)
        {
            _input   += char.ToUpper(key.KeyChar);
            _message  = "";
        }
    }

    private void HandleCommand()
    {
        var cmd = _input.Trim();
        _input = "";

        if (cmd == "/HELP")
        {
            _helpVisible = !_helpVisible;
            _message     = "";
            return;
        }

        if (cmd == "/SETTINGS")
        {
            Response = new BaseSectionResponse(BaseSectionRequest.GoToSection, "settings");
            return;
        }

        var gameCommands = new[] { "MISSION", "MEMORY", "COMMS", "SYNC", "DIAG" };
        if (Array.IndexOf(gameCommands, cmd) >= 0)
        {
            Response = new BaseSectionResponse(BaseSectionRequest.GoToSection, cmd.ToLower());
            return;
        }

        _message        = $"Unknown command: {cmd}";
        _messageIsError = true;
        _helpVisible    = false;
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
        int left    = (buffer.Width - BoxWidth) / 2;
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
        string paddedInput  = inputDisplay.PadRight(BoxInner);
        buffer.WriteAt(left, centerY + 1, "│", ExoColors.ProksBorder);
        buffer.WriteAt(left + 1, centerY + 1, paddedInput, ExoColors.PhosphorText);
        if (_blinkVisible)
            buffer.WriteAt(left + 1 + inputDisplay.Length, centerY + 1, "_", ExoColors.PhosphorDim);
        buffer.WriteAt(left + BoxWidth - 1, centerY + 1, "│", ExoColors.ProksBorder);

        buffer.WriteAt(left, centerY + 2, "└" + new string('─', BoxInner) + "┘", ExoColors.ProksBorder);

        // ── /help hint or error ───────────────────────────────────────────────
        if (!string.IsNullOrEmpty(_message))
        {
            string color = _messageIsError ? ExoColors.FaultText : ExoColors.ProksPale;
            int msgX = (buffer.Width - _message.Length) / 2;
            buffer.WriteAt(msgX, centerY + 4, _message, color);
        }
        else if (!_helpVisible)
        {
            const string hint = "/help";
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
                string cmdColor  = isSystem ? ExoColors.ProksPale : ExoColors.PhosphorText;
                string line      = $"  {cmd.PadRight(12)}{desc}";
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
        string sol = _settings.SolDisplay;
        buffer.WriteAt(left + BoxWidth - sol.Length, statusY, sol, ExoColors.SignalText);
    }
}
