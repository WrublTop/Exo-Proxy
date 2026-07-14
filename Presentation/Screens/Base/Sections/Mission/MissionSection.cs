using ExoProxy.Core;
using ExoProxy.Data;
using ExoProxy.Data.Mission;

namespace ExoProxy.Presentation.Screens.Base.Sections.Mission;

// MISSION — the remote field link to rover SR-74. Walking skeleton scope:
// terrain map with grid and rover, SR MOVE commands routed through a
// signal-delay uplink, arrow keys for single steps. Sensors, battery,
// collection and hazards come later.
//
// Layout (120×30): left panel ~70% = header + map, right panel = SR command
// terminal, bottom row = environment telemetry.
public sealed class MissionSection : IBaseSection
{
    public string SectionId => SectionIds.Mission;
    public BaseSectionResponse Response { get; private set; } = new(BaseSectionRequest.Stay, null);

    private readonly OperatorProgress _progress;
    private readonly MissionWorld _world;
    private readonly bool _isDev;     // dev mode unlocks the whole-map overview zoom
    private readonly IAudioService _audio;

    private string _input = "";
    private BlinkState _blink;

    // Set when the rover rolls onto the base tile after an excursion: the
    // terminal blocks normal input and waits for the DOCK? Y/N answer.
    private bool _awaitingDock;

    // Rover incapacitated in the field — destroyed or stranded out of power.
    // _disabled latches (it can't drive again), so dismissing the prompt only
    // lets the operator look around or LOGOUT; any drive attempt re-raises it.
    private bool _disabled;
    private bool _awaitingTerminate;

    // MASS sensor online → the map tints by elevation. (Battery drain and the
    // other sensors come with the collection pass.)
    private bool _massActive;

    // Map zoom: 1:1 is navigation scale; smaller cells fit more terrain on
    // screen for surveying. Index into _zoomLevels.
    private int _zoom;
    private static readonly (int W, int H, string Label)[] _zoomLevels =
    [
        (10, 5, "ZOOM 1:1"),
        (4,  2, "ZOOM 1:3"),
        (2,  1, "ZOOM 1:5"),    // densest square cell — terminal chars are ~2:1, so W=2H
    ];

    // Dev gets one step past the normal zooms: the whole-map overview
    // (index == _zoomLevels.Length), for judging the terrain at a glance.
    private int MaxZoomIndex => _isDev ? _zoomLevels.Length : _zoomLevels.Length - 1;
    private bool IsOverview  => _zoom >= _zoomLevels.Length;
    private string ZoomLabel => IsOverview ? "ZOOM MAP" : _zoomLevels[_zoom].Label;

    // ── uplink / drive state ──────────────────────────────────────────────────
    // A command is not executed when typed — first it is transmitted (fixed
    // 3 s uplink), and only then does the rover physically drive, one cell
    // per second. The player watches both phases happen.
    private enum LinkState { Idle, Transmitting, Moving }

    private LinkState _state = LinkState.Idle;
    private int _phaseElapsedMs;
    private Direction _pendingDir;
    private int _stepsRemaining;
    private int _stepsMoved;
    private string? _pendingMark;     // non-null → the uplink carries a MARK, not a MOVE

    private const int TxMs      = 3000;   // data uplink — always the same
    private const int StepMs    = 1000;   // physical drive — per cell
    private const int TxFrames  = 8;
    private const int DotStepMs = 400;

    private const int MaxInput  = 28;
    private const int MaxSteps  = 99;
    private const int LogLimit  = 200;

    private readonly List<(string Text, string Color)> _log = [];

    public MissionSection(OperatorProgress progress, MissionWorld world, bool devMode, IAudioService audio)
    {
        _progress = progress;
        _world    = world;
        _isDev    = devMode;
        _audio    = audio;
        Log("SR-74 FIELD LINK ESTABLISHED", ExoColors.SignalDim);
        Log("SR MOVE N|S|E|W [1-99]", ExoColors.ProksText);
        Log("SR MARK <NAME> | SR MARKS", ExoColors.ProksText);
        Log("SR SENSOR MASS|OFF", ExoColors.ProksText);
        Log(_isDev ? "SR ZOOM 1-4 (4=MAP) | PGUP/PGDN" : "SR ZOOM 1-3 | PGUP/PGDN", ExoColors.ProksText);
        Log("ARROW KEYS — SINGLE STEP", ExoColors.ProksText);
        Log("LOGOUT | EXIT", ExoColors.ProksText);
    }

    // ── update ────────────────────────────────────────────────────────────────

    public void Update(GameTime time, InputEvent? input)
    {
        _blink.Update(time.Total);
        Response = new(BaseSectionRequest.Stay, null);

        if (_state == LinkState.Transmitting)
        {
            _phaseElapsedMs += (int)time.Delta.TotalMilliseconds;
            if (_phaseElapsedMs >= TxMs)
            {
                _phaseElapsedMs = 0;
                if (_pendingMark is not null)
                {
                    // A MARK has no drive phase — the rover just confirms.
                    _world.AddMark(_pendingMark);
                    Log($"SR: MARKED {_pendingMark} = {PosDisplay()}", ExoColors.ProksText);
                    _pendingMark = null;
                    _state       = LinkState.Idle;
                    _world.Persist?.Invoke();
                }
                else
                {
                    _state = LinkState.Moving;
                }
            }
        }
        else if (_state == LinkState.Moving)
        {
            _phaseElapsedMs += (int)time.Delta.TotalMilliseconds;
            while (_phaseElapsedMs >= StepMs && _state == LinkState.Moving)
            {
                _phaseElapsedMs -= StepMs;
                StepRover();
            }
            // A finished drive (arrival, edge, depletion or completion) is a save
            // checkpoint; mid-drive frames stay Moving and don't touch the disk.
            if (_state == LinkState.Idle) _world.Persist?.Invoke();
        }

        if (input is null) return;
        var key = input.Value.Key;

        // While the dock prompt is up nothing else responds — the operator
        // must answer Y/N (Escape reads as N).
        if (_awaitingDock)
        {
            if (key.Key == ConsoleKey.Y) ConfirmDock();
            else if (key.Key is ConsoleKey.N or ConsoleKey.Escape) CancelDock();
            return;
        }

        // Same for the terminate prompt after a wreck / dead battery. Y ends the
        // run (permadeath); N/Escape just dismisses it — the rover stays disabled.
        if (_awaitingTerminate)
        {
            if (key.Key == ConsoleKey.Y) ConfirmTerminate();
            else if (key.Key is ConsoleKey.N or ConsoleKey.Escape) _awaitingTerminate = false;
            return;
        }

        // Arrow keys steer the rover directly — one unit per press.
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:    TryBeginMove(Direction.North, 1); return;
            case ConsoleKey.DownArrow:  TryBeginMove(Direction.South, 1); return;
            case ConsoleKey.RightArrow: TryBeginMove(Direction.East,  1); return;
            case ConsoleKey.LeftArrow:  TryBeginMove(Direction.West,  1); return;
            case ConsoleKey.PageUp:     _zoom = Math.Max(0, _zoom - 1); return;
            case ConsoleKey.PageDown:   _zoom = Math.Min(MaxZoomIndex, _zoom + 1); return;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (_input.Length > 0) _input = _input[..^1];
            return;
        }

        // Escape only clears a half-typed command — it never leaves the field.
        // The one way back to the hub is to physically drive the rover home and
        // dock; that commitment is what gives the battery and permadeath weight.
        if (key.Key == ConsoleKey.Escape)
        {
            _input = "";
            return;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            HandleCommand();
            return;
        }

        if (key.KeyChar != '\0' && _input.Length < MaxInput)
            _input += char.ToUpper(key.KeyChar);
    }

    // ── commands ──────────────────────────────────────────────────────────────

    private void HandleCommand()
    {
        string cmd = _input.Trim();
        _input = "";
        if (cmd.Length == 0) return;

        Log("> " + cmd, ExoColors.PhosphorDim);

        // Terminal-level commands — these power down / end the session, they do
        // NOT move the rover out of the field. With persistence the rover stays
        // exactly where it is; quitting the terminal is always allowed.
        if (cmd == "LOGOUT")
        {
            Response = new(BaseSectionRequest.Logout, null);
            return;
        }
        if (cmd == "EXIT")
        {
            Response = new(BaseSectionRequest.ExitGame, null);
            return;
        }

        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2 && parts[0] == "SR" && parts[1] == "MOVE")
        {
            if (parts.Length is 3 or 4 && TryParseDirection(parts[2], out var dir))
            {
                int steps = 1;
                if (parts.Length == 3 ||
                    (int.TryParse(parts[3], out steps) && steps >= 1 && steps <= MaxSteps))
                {
                    TryBeginMove(dir, steps);
                    return;
                }
            }
            Log($"SYNTAX: SR MOVE N|S|E|W [1-{MaxSteps}]", ExoColors.FaultText);
            return;
        }

        if (parts.Length >= 2 && parts[0] == "SR" && parts[1] == "MARK")
        {
            if (parts.Length == 3) TryBeginMark(parts[2]);
            else Log("SYNTAX: SR MARK <NAME>", ExoColors.FaultText);
            return;
        }

        if (parts.Length == 2 && parts[0] == "SR" && parts[1] == "MARKS")
        {
            ListMarks();
            return;
        }

        if (parts.Length >= 2 && parts[0] == "SR" && parts[1] == "SENSOR")
        {
            if (parts.Length == 3) HandleSensor(parts[2]);
            else Log("SYNTAX: SR SENSOR MASS|OFF", ExoColors.FaultText);
            return;
        }

        if (parts.Length >= 2 && parts[0] == "SR" && parts[1] == "ZOOM")
        {
            if (parts.Length == 3 && int.TryParse(parts[2], out int z) && z >= 1 && z <= MaxZoomIndex + 1)
            {
                _zoom = z - 1;
                Log($"ZOOM SET — {ZoomLabel}", ExoColors.ProksText);
            }
            else Log($"SYNTAX: SR ZOOM 1-{MaxZoomIndex + 1}", ExoColors.FaultText);
            return;
        }

        Log("UNKNOWN COMMAND: " + cmd, ExoColors.FaultText);
    }

    private void HandleSensor(string name)
    {
        switch (name)
        {
            case "MASS":
                _massActive = true;
                _audio.Play("rover.sensor_on");
                Log("SENSOR: MASS ONLINE — TOPOGRAPHY", ExoColors.SignalDim);
                break;
            case "OFF":
                _massActive = false;
                _audio.Play("rover.sensor_off");
                Log("SENSOR OFFLINE", ExoColors.ProksText);
                break;
            case "THERMAL" or "EM":
                Log($"SENSOR {name} NOT INSTALLED", ExoColors.FaultText);
                break;
            default:
                Log("SYNTAX: SR SENSOR MASS|OFF", ExoColors.FaultText);
                break;
        }
    }

    private void TryBeginMark(string name)
    {
        if (_disabled) { _awaitingTerminate = true; return; }
        if (_state != LinkState.Idle) return;   // same silent drop as moves

        if (name.Length is < 1 or > 4 || !name.All(char.IsLetterOrDigit))
        {
            Log("SYNTAX: SR MARK <1-4 LETTERS/DIGITS>", ExoColors.FaultText);
            return;
        }
        if (name is "SR" or "BS" or "BASE")
        {
            Log($"RESERVED NAME: {name}", ExoColors.FaultText);
            return;
        }
        if (_world.HasMark(name))
        {
            Log($"MARKER NAME IN USE: {name}", ExoColors.FaultText);
            return;
        }
        if (_world.MarksFull)
        {
            Log($"MARKER LIMIT REACHED — {MissionWorld.MaxMarks} MAX", ExoColors.FaultText);
            return;
        }

        _pendingMark    = name;
        _phaseElapsedMs = 0;
        _state          = LinkState.Transmitting;
        _audio.Play("rover.uplink");
    }

    // Reading your own annotations is local — no uplink round-trip.
    private void ListMarks()
    {
        if (_world.Marks.Count == 0)
        {
            Log("NO MARKERS SET", ExoColors.ProksText);
            return;
        }

        foreach (var m in _world.Marks)
            Log($"{m.Name,-4} = [{Coord(m.X)};{Coord(m.Y)}]", ExoColors.PhosphorText);
        Log($"MARKERS {_world.Marks.Count}/{MissionWorld.MaxMarks}", ExoColors.ProksText);
    }

    private static bool TryParseDirection(string token, out Direction dir)
    {
        dir = token switch
        {
            "N" => Direction.North,
            "S" => Direction.South,
            "E" => Direction.East,
            "W" => Direction.West,
            _   => (Direction)(-1),
        };
        return (int)dir >= 0;
    }

    private void TryBeginMove(Direction dir, int steps)
    {
        // A wrecked or stranded rover can't drive — re-raise the terminate prompt
        // instead of silently eating the command.
        if (_disabled) { _awaitingTerminate = true; return; }

        // Uplink carries one command at a time — extra input is dropped
        // silently; an error line for every keypress got annoying fast.
        if (_state != LinkState.Idle) return;

        _pendingDir     = dir;
        _stepsRemaining = steps;
        _stepsMoved     = 0;
        _phaseElapsedMs = 0;
        _state          = LinkState.Transmitting;
        _audio.Play("rover.uplink");
    }

    // One physical cell of travel. The map and POS header update live —
    // the player watches the rover crawl across the grid, step by step.
    private void StepRover()
    {
        // Was the rover on lethal low ground before this step? Used below to fire
        // hull damage only on the *fall* (solid ground → band 0), not for driving
        // along the bottom or climbing back out.
        bool wasOnHazard = _world.IsOnHazard;

        // Each cell draws on the battery before the wheels turn — the slope onto
        // that cell and an active sensor stack onto the base cost. If the rover
        // can't cover it: either it's truly out of range (can't afford even the
        // cheapest possible step) and is stranded for good, or this one grade is
        // just too steep for the charge in hand — hold and let the operator pick a
        // gentler line.
        if (!_world.TrySpendForStep(_pendingDir, _massActive))
        {
            _state = LinkState.Idle;
            if (_world.Charge < MissionWorld.BaseMoveCost)
            {
                _audio.Play("rover.low_power");
                Disable("POWER DEPLETED — SR-74 STRANDED IN FIELD");
            }
            else
            {
                Log("INSUFFICIENT POWER FOR GRADE — SR-74 HOLDS", ExoColors.FaultText);
                Log("POS = " + PosDisplay(), ExoColors.FaultText);
            }
            return;
        }

        // Band 0 is passable — the rover always reaches the cell it was sent to.
        // Move returns 0 only at the survey rectangle: the playable boundary, past
        // which lies the deep ocean / off-map void with nothing to find.
        if (_world.Move(_pendingDir, 1) == 0)
        {
            _state = LinkState.Idle;
            _audio.Play("rover.boundary");
            Log(_stepsMoved == 0
                    ? "SURVEY BOUNDARY — SR-74 HOLDS"
                    : $"SURVEY BOUNDARY — MOVED {_stepsMoved}U {_pendingDir.Letter()}",
                ExoColors.FaultText);
            Log("POS = " + PosDisplay(), ExoColors.FaultText);
            return;
        }

        _stepsMoved++;
        _stepsRemaining--;

        // Fell off solid ground onto lethal low ground — sea, void or a canyon
        // floor. The impact takes a fixed bite out of the hull and the drive stops
        // dead on the cell it dropped onto. Driving on along the bottom, or
        // climbing back out, does no further hull damage — only the fall does.
        if (_world.IsOnHazard && !wasOnHazard)
        {
            _world.Damage(MissionWorld.ImpactDamage);
            _audio.Play("rover.hull_impact");
            _state = LinkState.Idle;
            if (_world.IsDestroyed)
                Disable("HULL FAILURE — SR-74 DESTROYED");
            else
            {
                Log($"SR-74 HULL IMPACT — INTEGRITY {_world.IntegrityDisplay}%", ExoColors.FaultText);
                Log("POS = " + PosDisplay(), ExoColors.FaultText);
            }
            return;
        }

        // Rolling onto the base after an excursion halts the drive and offers
        // the dock — even mid-way through a longer MOVE command.
        if (_world.IsAtBase && !_world.IsDocked)
        {
            _state = LinkState.Idle;
            Log($"SR: ARRIVED AT BASE STATION — {_stepsMoved}U {_pendingDir.Letter()}", ExoColors.ProksText);
            Log("POS = " + PosDisplay(), ExoColors.ProksText);
            _awaitingDock = true;
            return;
        }

        if (_stepsRemaining > 0) return;

        _state = LinkState.Idle;
        Log($"SR: MOVED {_stepsMoved}U {_pendingDir.Letter()}", ExoColors.ProksText);
        Log("POS = " + PosDisplay(), ExoColors.ProksText);
    }

    private void ConfirmDock()
    {
        _awaitingDock = false;
        _world.Dock();
        Log("DOCKING CONFIRMED — UPLINK TO SUIRDC RESTORED", ExoColors.SignalText);
        _world.Persist?.Invoke();
        Response = new(BaseSectionRequest.Dock, null);
    }

    private void CancelDock()
    {
        _awaitingDock = false;
        Log("DOCKING ABORTED — SR-74 HOLDING AT BASE", ExoColors.ProksText);
    }

    // Field incapacitation — destroyed or stranded. Latches the rover offline and
    // raises the terminate prompt with the cause.
    private void Disable(string reason)
    {
        _disabled          = true;
        _awaitingTerminate = true;
        Log(reason, ExoColors.FaultText);
        Log("POS = " + PosDisplay(), ExoColors.FaultText);
    }

    // Operator confirms the end of the run — Program.cs handles the permadeath:
    // the account is terminated and its saves wiped before returning to login.
    private void ConfirmTerminate()
    {
        _awaitingTerminate = false;
        Response = new(BaseSectionRequest.Perish, null);
    }

    private void Log(string text, string color)
    {
        _log.Add((text, color));
        if (_log.Count > LogLimit) _log.RemoveAt(0);
    }

    // ── render ────────────────────────────────────────────────────────────────

    public void Render(IRenderBuffer buffer)
    {
        int w = buffer.Width, h = buffer.Height;
        int divX      = w * 7 / 10;       // vertical divider — left panel ≈ 70%
        int leftInner = divX - 1;         // columns 1..divX-1
        int rightX    = divX + 1;         // first column of the right panel
        int rightW    = w - divX - 2;
        int sepY      = h - 3;            // telemetry / input separator row

        // ── frame ─────────────────────────────────────────────────────────────
        Ui.WriteDualTitleBorder(buffer, 0, 0, w,
            " MISSION ", " " + _progress.SolDisplay + " ",
            ExoColors.PhosphorBright, ExoColors.SignalText, ExoColors.ProksBorder);
        buffer.WriteAt(divX, 0, '┬', ExoColors.ProksBorder);

        for (int y = 1; y < h - 1; y++)
        {
            buffer.WriteAt(0,     y, '│', ExoColors.ProksBorder);
            buffer.WriteAt(divX,  y, '│', ExoColors.ProksBorder);
            buffer.WriteAt(w - 1, y, '│', ExoColors.ProksBorder);
        }

        string leftSep  = PadSeparator("─ TELEMETRY ", leftInner);
        string rightSep = PadSeparator("─ SR UPLINK ", rightW);
        buffer.WriteAt(0, sepY, "├" + leftSep + "┼" + rightSep + "┤", ExoColors.ProksBorder);

        buffer.WriteAt(0, h - 1,
            "└" + new string('─', leftInner) + "┴" + new string('─', rightW) + "┘",
            ExoColors.ProksBorder);

        // ── left panel: header (single status line) ───────────────────────────
        string pos = "POS = " + PosDisplay();
        buffer.WriteAt(2, 1, pos, ExoColors.PhosphorText);
        int hx = 2 + pos.Length + 4;

        string sensor = _massActive ? "SENSOR: MASS" : "SENSOR: NONE";
        buffer.WriteAt(hx, 1, sensor, _massActive ? ExoColors.SignalDim : ExoColors.ProksPale);
        hx += sensor.Length + 4;

        var sector = _world.SectorAt(_world.RoverX, _world.RoverY);
        string sectorTxt = sector is null ? "SECTOR --" : "SECTOR " + sector.Label;
        string sectorCol = sector is null
            ? ExoColors.ProksPale
            : ExoCodes.Fg(sector.Rgb.R, sector.Rgb.G, sector.Rgb.B);
        buffer.WriteAt(hx, 1, sectorTxt, sectorCol);

        string power = $"POWER: {_world.PercentDisplay}%";
        string zoom = ZoomLabel;
        buffer.WriteAt(divX - power.Length - 1, 1, power, PowerColor());
        buffer.WriteAt(divX - power.Length - 1 - 4 - zoom.Length, 1, zoom, ExoColors.ProksPale);

        Ui.WriteTransmitBorder(buffer, 0, 2, leftInner, "─ TERRAIN ");

        // ── left panel: map ───────────────────────────────────────────────────
        var map = new Region(buffer, 1, 3, leftInner, sepY - 3);
        if (IsOverview) MapRenderer.RenderOverview(map, _world);
        else MapRenderer.Render(map, _world, _massActive, _zoomLevels[_zoom].W, _zoomLevels[_zoom].H);

        // ── left panel: telemetry (placeholders until the economy exists) ─────
        string alt = _world.Terrain is { } terr
            ? $"ALT {terr.DisplayAltitude(_world.RoverX, _world.RoverY)}M"
            : "ALT ----";
        buffer.WriteAt(2, h - 2,
            $"{alt}    HULL {_world.IntegrityDisplay}%    TEMP -61C    ES 0.3    WIND 12 M/S    SIG ON",
            ExoColors.ProksText);

        // ── right panel: log + uplink animation ──────────────────────────────
        int logTop = 1, logBottom = sepY - 1;
        if (_state != LinkState.Idle)
        {
            RenderLinkActivity(buffer, rightX, rightW, logBottom);
            logBottom -= 3;       // activity block + one blank spacer row
        }

        int rows    = logBottom - logTop + 1;
        int visible = Math.Min(rows, _log.Count);
        int yStart  = logBottom - visible + 1;
        for (int i = 0; i < visible; i++)
        {
            var (text, color) = _log[_log.Count - visible + i];
            buffer.WriteAt(rightX + 1, yStart + i, Ui.Truncate(text, rightW - 2), color);
        }

        // ── right panel: command input ────────────────────────────────────────
        if (_awaitingDock)
        {
            buffer.WriteAt(rightX + 1, h - 2,
                Ui.Truncate("DOCK WITH BASE STATION? Y/N", rightW - 2), ExoColors.SignalBright);
        }
        else if (_awaitingTerminate)
        {
            buffer.WriteAt(rightX + 1, h - 2,
                Ui.Truncate("TERMINATE SESSION? Y/N", rightW - 2), ExoColors.FaultText);
        }
        else
        {
            string prompt = "> " + _input;
            buffer.WriteAt(rightX + 1, h - 2, prompt, ExoColors.PhosphorText);
            if (_blink.Visible)
                buffer.WriteAt(rightX + 1 + prompt.Length, h - 2, '_', ExoColors.PhosphorDim);
        }
    }

    // Phase feedback above the prompt. Transmitting: growing TX arrow, same
    // frame feel as the COMMS transmit animation. Moving: typing-style dots —
    // the drive takes one second per cell, so long hauls visibly take long.
    private void RenderLinkActivity(IRenderBuffer buffer, int x, int w, int y)
    {
        if (_state == LinkState.Transmitting)
        {
            int frame   = Math.Min(TxFrames - 1, _phaseElapsedMs * TxFrames / TxMs);
            int dashMax = Math.Max(0, w - 9);              // room for "► SR-74"
            int dashes  = dashMax * frame / (TxFrames - 1);

            buffer.WriteAt(x + 1, y - 1, "UPLINK ▸ TRANSMITTING", ExoColors.ProksPale);
            buffer.WriteAt(x + 1, y, new string('─', dashes) + "► SR-74", ExoColors.SignalDim);
        }
        else
        {
            int total   = _stepsMoved + _stepsRemaining;
            int dotStep = _phaseElapsedMs / DotStepMs % 3;
            string dots = dotStep == 0 ? ".  " : dotStep == 1 ? ".. " : "...";

            buffer.WriteAt(x + 1, y - 1, $"SR-74 ▸ MOVING {_stepsMoved + 1:D2}/{total:D2}", ExoColors.ProksPale);
            buffer.WriteAt(x + 1, y, dots, ExoColors.SignalDim);
        }
    }

    // Signed, fixed-width coordinate: +0012 / -0150. Keeps columns aligned
    // and makes the negative half of the world unambiguous.
    private static string Coord(int v) => (v < 0 ? "-" : "+") + Math.Abs(v).ToString("D4");

    // The one true position format — header and log messages both use it.
    private string PosDisplay() => $"[{Coord(_world.RoverX)};{Coord(_world.RoverY)}]";

    // POWER readout dims as the charge drains; below a quarter it goes fault-red.
    private string PowerColor() => _world.PercentDisplay switch
    {
        > 75 => ExoColors.SignalBright,
        > 50 => ExoColors.SignalText,
        > 25 => ExoColors.SignalDim,
        _    => ExoColors.FaultText,
    };

    private static string PadSeparator(string label, int width) =>
        label.Length >= width
            ? label[..width]
            : label + new string('─', width - label.Length);
}
