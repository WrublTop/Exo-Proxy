using ExoProxy.Core;
using ExoProxy.Data;
using ExoProxy.Data.Mission;

namespace ExoProxy.Presentation.Screens.Base.Sections.Mission;

// MISSION — remote field link to rover SR-74. Left panel = terrain map,
// right panel = SR command terminal, bottom row = telemetry.
public sealed class MissionSection : IBaseSection
{
    public string SectionId => SectionIds.Mission;
    public BaseSectionResponse Response { get; private set; } = new(BaseSectionRequest.Stay, null);

    private readonly OperatorProgress _progress;
    private readonly OperatorAccount _account;
    private readonly MissionWorld _world;
    private readonly bool _isDev;
    private readonly IAudioService _audio;
    private readonly MemoryRepository _memory;
    private readonly Random _rng = new();

    private string _input = "";
    private BlinkState _blink;
    private TimeSpan _now;   // stashed each Update for Render

    private bool _wasDocked;           // dock latch — flush rover tape once per undock
    private bool _awaitingDock;        // DOCK? Y/N prompt up
    private bool _disabled;            // wrecked/stranded — latched, can't drive
    private bool _awaitingTerminate;

    // Independent sensor toggles; each stacks battery drain per step.
    private bool _massActive;
    private bool _thermalActive;
    private bool _emActive;
    private int ActiveSensorCount => (_massActive ? 1 : 0) + (_thermalActive ? 1 : 0) + (_emActive ? 1 : 0);

    private int _zoom;   // index into _zoomLevels
    private static readonly (int W, int H, string Label)[] _zoomLevels =
    [
        (10, 5, "ZOOM 1:1"),
        (4,  2, "ZOOM 1:3"),
        (2,  1, "ZOOM 1:5"),    // densest square cell — chars are ~2:1, so W=2H
    ];

    // Dev gets one extra zoom step: the whole-map overview.
    private int MaxZoomIndex => _isDev ? _zoomLevels.Length : _zoomLevels.Length - 1;
    private bool IsOverview  => _zoom >= _zoomLevels.Length;
    private string ZoomLabel => IsOverview ? "ZOOM MAP" : _zoomLevels[_zoom].Label;

    // ── uplink / drive state ──────────────────────────────────────────────────
    // Command transmits (3s uplink) before the rover drives one cell per second.
    private enum LinkState { Idle, Transmitting, Moving }

    private LinkState _state = LinkState.Idle;
    private int _phaseElapsedMs;
    private Direction _pendingDir;
    private int _stepsRemaining;
    private int _stepsMoved;
    private string? _pendingMark;     // non-null → the uplink carries a MARK, not a MOVE

    // ── extraction minigames ──────────────────────────────────────────────────
    // SR EXTRACT freezes driving and runs a minigame. Starting one commits the
    // deposit — any non-success outcome loses it for the run.
    private enum FieldMode { Driving, ExtractThermal, ExtractEm, ConvoyChoice, ConvoySync }
    private FieldMode _mode = FieldMode.Driving;

    // ── convoy sync interaction ────────────────────────────────────────────────
    private bool _awaitingConvoy;   // "SYNC? Y/N" prompt is up
    private bool _convoyDeclined;   // declined this approach; cleared on leaving range
    private enum ConvoyOutcome { File, Ride }
    private ConvoyOutcome _convoyOutcome;
    private (int DX, int DY) _rideHeading;   // convoy heading captured at sync start

    // Sync minigame — time SPACE to the marker crossing a drifting window.
    private double _syncPos;         // sweeping marker 0-100
    private double _syncPosPrev;     // position as last shown — what a press is judged against
    private double _syncDir = 1;
    private readonly double[] _winC = new double[SyncWindows];   // window centres
    private readonly double[] _winW = new double[SyncWindows];   // window widths
    private readonly double[] _winV = new double[SyncWindows];   // window drift velocities
    private int    _syncBarCells = 44;   // bar resolution = panel width, set each render
    private double _sync;            // sync meter 0-100
    private double _syncTimeLeft;
    private double _syncCooldown;
    private int    _syncHits, _syncMiss;
    private int    _syncStreak;      // drives the point multiplier

    private const int    SyncWindows      = 3;
    private const double SyncWidthMin     = 2.0, SyncWidthMax = 6.0;  // narrow = brighter + worth more
    private const double SyncMinGap       = 15.0;
    private const int    SyncPtsNarrow    = 7, SyncPtsWide = 3;
    private const double SyncMissPenalty  = 15.0;                     // also resets the streak
    private const double SyncBounceChance = 0.33;
    private const double SyncSweepSpeed   = 46.0;
    private const double SyncCooldownSec  = 0.30;
    private const double SyncDurationMin  = 60.0, SyncDurationMax = 60.0;
    private const double SyncDriftSpeed   = 13.0;
    private const double SyncDriftMin     = 4.0, SyncDriftMax = 96.0;
    private const int    SyncStreakStep   = 3, SyncMultMax = 2;
    private MissionWorld.Deposit _activeDeposit;

    // THERMAL — ←/→ track the drifting vein, SPACE strikes. Passive tracking is
    // capped; only a good strike clears the pass floor.
    private double _thermalTarget, _thermalVelocity, _thermalCursor, _thermalLock;
    private double _thermalTimeLeft, _thermalStrikeCooldown, _thermalSpeedPhaseTimer;
    private int    _thermalStrikeCount, _thermalStrikeHits;   // for the assay log

    private const double ThermalDuration     = 30.0;
    private const double ThermalWindow       = 5.0;
    private const double ThermalCursorStep   = 3.0;
    private const double ThermalPassiveGain  = 1.8;   // gain > decay: tracking leans slightly for you
    private const double ThermalPassiveDecay = 1.5;
    private const double ThermalStrikeGood   = 10.0;  // SPACE while aligned
    private const double ThermalStrikeBad    = -6.0;  // SPACE while not
    private const double ThermalStrikeCooldownSec = 2.5;
    private const int    ThermalMinLock      = 25;    // final LOCK below this = botched core

    // Drift speed rides a few-second phase (slow/baseline/fast) rather than a
    // fresh roll each frame; a separate low-frequency roll flips direction too.
    private const double ThermalPhaseMinSec    = 2.5, ThermalPhaseMaxSec = 5.0;
    private const double ThermalSpeedBase      = 20.0, ThermalSpeedBaseJitter = 2.0;  // → 18-22
    private const double ThermalSpeedSlowMin   = 16.0, ThermalSpeedSlowMax   = 18.0;
    private const double ThermalSpeedFastMin   = 27.0, ThermalSpeedFastMax   = 29.0;
    private const double ThermalDirFlipChance  = 0.12;  // per second

    // EM — frequency capture. Same two-layer shape as THERMAL, but the carrier
    // drifts continuously and periodically hops far. Feedback is low-resolution
    // (NO/WEAK/STRONG/LOCKED bands, no number or direction) so finding it takes
    // a real search, not one glance at a meter.
    private double _emTarget, _emCapture, _emTimeLeft;
    private double _emPulseCooldown, _emHopTimer;
    private double _emDriftVelocity;
    private int    _emCursor;
    private int    _emPulseCount, _emPulseHits;
    private double _emMoveCooldown;   // throttles ←/→ so scanning costs real time

    private const double EmDurationMin     = 32.0, EmDurationMax = 36.0;   // per-attempt, for run-to-run swing
    private const double EmWindow          = 4.0;    // LOCKED band
    private const double EmStrongRadius    = 7.0;    // STRONG band out to here
    private const double EmWeakRadius      = 12.0;   // WEAK band out to here; beyond it, nothing
    private const double EmPassiveGain     = 2.7;
    private const double EmPassiveDecay    = 0.7;    // NO SIGNAL
    private const double EmPassiveWeakDecay = 0.3;   // WEAK; STRONG is neutral (see TickEm)

    // Pulse reward graduated by band: LOCKED full, STRONG a quarter, WEAK free,
    // NO SIGNAL costs.
    private const double EmPulseGoodLocked = 18.0;
    private const double EmPulseGoodStrong = EmPulseGoodLocked / 4.0;
    private const double EmPulseGoodWeak   = 0.0;
    private const double EmPulseBad        = -6.0;
    private const double EmPulseCooldownSec = 2.5;

    private const double EmDriftSpeed      = 2.8;    // calm — THERMAL owns "fast and erratic"
    private const double EmDriftFlipChance = 0.15;   // per second

    // Frequency hop: a hard jump ≥24 units away every few seconds, on top of drift.
    private const double EmHopMinSec       = 4.2, EmHopMaxSec = 5.7;
    private const double EmHopMinDistance  = 24.0;

    private const double EmMoveCooldownSec = 0.03;

    private const int TxMs      = 3000;   // data uplink — always the same
    private const int StepMs    = 1000;   // physical drive — per cell
    private const int TxFrames  = 8;
    private const int DotStepMs = 400;

    private const int MaxInput  = 28;
    private const int MaxSteps  = 99;
    private const int LogLimit  = 200;

    private readonly List<(string Text, string Color)> _log = [];

    public MissionSection(OperatorProgress progress, OperatorAccount account, MissionWorld world, bool devMode,
        IAudioService audio, MemoryRepository memory)
    {
        _progress = progress;
        _account  = account;
        _world    = world;
        _isDev    = devMode;
        _audio    = audio;
        _memory   = memory;

        if (_isDev) _massActive = _thermalActive = _emActive = true;   // DEV: sensors on from the start

        Log("SR-74 FIELD LINK ESTABLISHED", ExoColors.SignalDim);
        Log("SR MOVE N|S|E|W [1-99]", ExoColors.ProksText);
        Log("SR MARK <NAME> | SR MARKS", ExoColors.ProksText);
        Log("SR MASS|THERMAL|EM ON|OFF", ExoColors.ProksText);
        Log("SR COLLECT", ExoColors.ProksText);
        Log(_isDev ? "SR ZOOM 1-4 (4=MAP) | PGUP/PGDN" : "SR ZOOM 1-3 | PGUP/PGDN", ExoColors.ProksText);
        Log("ARROW KEYS — SINGLE STEP", ExoColors.ProksText);
        Log("LOGOUT | EXIT", ExoColors.ProksText);
    }

    // ── update ────────────────────────────────────────────────────────────────

    public void Update(GameTime time, InputEvent? input)
    {
        _blink.Update(time.Total);
        _now = time.Total;
        Response = new(BaseSectionRequest.Stay, null);

        // Leaving the dock flushes the rover tape — checked here so every undock
        // path (drive or dev step) is caught.
        if (_wasDocked && !_world.IsDocked && _memory.WipeRover())
            Log("ROVER TAPE FLUSHED — UNSYNCED DATA LOST", ExoColors.FaultText);
        _wasDocked = _world.IsDocked;

        // Runner laps its loop while out in the field, but holds still mid-prompt/sync.
        if (!_awaitingConvoy && _mode is not (FieldMode.ConvoyChoice or FieldMode.ConvoySync))
            _world.UpdateConvoy(time.Delta.TotalSeconds);

        if (_mode == FieldMode.ExtractThermal) TickThermal(time.Delta.TotalSeconds);
        else if (_mode == FieldMode.ExtractEm) TickEm(time.Delta.TotalSeconds);
        else if (_mode == FieldMode.ConvoySync) TickSync(time.Delta.TotalSeconds);

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
            if (_state == LinkState.Idle) _world.Persist?.Invoke();   // save on drive end only
        }

        // Runner in range → offer the sync prompt. Once per approach, once per SOL.
        if (!_world.IsNearConvoy()) _convoyDeclined = false;
        if (_mode == FieldMode.Driving && _state == LinkState.Idle
            && !_awaitingDock && !_awaitingTerminate && !_awaitingConvoy && !_convoyDeclined
            && !_world.ConvoyAttemptedThisSol && _world.IsNearConvoy())
        {
            _awaitingConvoy = true;
            _audio.Play("rover.uplink");
            Log("SUPPLY RUNNER IN RANGE", ExoColors.SignalDim);
        }

        if (input is null) return;
        var key = input.Value.Key;

        // Dock prompt: nothing else responds until Y/N (Escape = N).
        if (_awaitingDock)
        {
            if (key.Key == ConsoleKey.Y) ConfirmDock();
            else if (key.Key is ConsoleKey.N or ConsoleKey.Escape) CancelDock();
            return;
        }

        // Terminate prompt (wreck / dead battery): Y = permadeath, N/Escape dismisses.
        if (_awaitingTerminate)
        {
            if (key.Key == ConsoleKey.Y) ConfirmTerminate();
            else if (key.Key is ConsoleKey.N or ConsoleKey.Escape) _awaitingTerminate = false;
            return;
        }

        // Convoy Y/N → choice → sync. Only resolving the sync burns the SOL attempt.
        if (_awaitingConvoy)
        {
            if (key.Key == ConsoleKey.Y) BeginConvoyChoice();
            else if (key.Key is ConsoleKey.N or ConsoleKey.Escape) { _awaitingConvoy = false; _convoyDeclined = true; }
            return;
        }
        if (_mode == FieldMode.ConvoyChoice)
        {
            if (key.KeyChar == '1') { _convoyOutcome = ConvoyOutcome.File; BeginSync(); }
            else if (key.KeyChar == '2') { _convoyOutcome = ConvoyOutcome.Ride; BeginSync(); }
            else if (key.Key == ConsoleKey.Escape) { _mode = FieldMode.Driving; _convoyDeclined = true; }
            return;
        }
        if (_mode == FieldMode.ConvoySync)
        {
            if (key.Key == ConsoleKey.Escape)   { FailSync("SYNC ABORTED — RUNNER GONE"); return; }
            if (key.Key == ConsoleKey.Spacebar) { PulseSync(); return; }
            return;
        }

        // Extraction minigames swallow input until resolved; ESC forfeits the deposit.
        if (_mode == FieldMode.ExtractThermal)
        {
            if (key.Key == ConsoleKey.Escape)     { FailExtraction("EXTRACTION ABORTED — CORE LOST"); return; }
            if (key.Key == ConsoleKey.Spacebar)   { StrikeThermal(); return; }
            if (key.Key == ConsoleKey.LeftArrow)  { _thermalCursor = Math.Clamp(_thermalCursor - ThermalCursorStep, 0, 100); return; }
            if (key.Key == ConsoleKey.RightArrow) { _thermalCursor = Math.Clamp(_thermalCursor + ThermalCursorStep, 0, 100); return; }
            return;
        }
        if (_mode == FieldMode.ExtractEm)
        {
            if (key.Key == ConsoleKey.Escape)     { FailExtraction("EXTRACTION ABORTED — SIGNAL LOST"); return; }
            if (key.Key == ConsoleKey.Spacebar)   { PulseEm(); return; }
            if (key.Key == ConsoleKey.LeftArrow)  { TryMoveEm(-1); return; }
            if (key.Key == ConsoleKey.RightArrow) { TryMoveEm(1); return; }
            return;
        }

        // Arrow keys steer the rover — one unit per press.
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

        // Escape only clears a half-typed command — the only way out is to drive home and dock.
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

        // Terminal-level: end the session, but leave the rover where it is.
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

        // Sensors: short form "SR THERMAL ON" (long "SR SENSOR THERMAL ON" still works).
        if (parts.Length == 3 && parts[0] == "SR" && parts[1] is "MASS" or "THERMAL" or "EM")
        {
            HandleSensor(parts[1], parts[2]);
            return;
        }

        if (parts.Length >= 2 && parts[0] == "SR" && parts[1] == "SENSOR")
        {
            if (parts.Length == 4) HandleSensor(parts[2], parts[3]);
            else Log("SYNTAX: SR MASS|THERMAL|EM ON|OFF", ExoColors.FaultText);
            return;
        }

        if (parts.Length == 2 && parts[0] == "SR" && parts[1] is "COLLECT" or "EXTRACT")
        {
            TryBeginExtract();
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

    // Explicit ON/OFF, no toggle — the operator always knows the state.
    private void HandleSensor(string name, string state)
    {
        bool on;
        if (state == "ON") on = true;
        else if (state == "OFF") on = false;
        else { Log("SYNTAX: SR MASS|THERMAL|EM ON|OFF", ExoColors.FaultText); return; }

        switch (name)
        {
            case "MASS":    SetSensor(ref _massActive,    on, "MASS",    "TOPOGRAPHY");      break;
            case "THERMAL": SetSensor(ref _thermalActive, on, "THERMAL", "PROKS DEPOSITS");  break;
            case "EM":      SetSensor(ref _emActive,      on, "EM",      "SIGNAL CAPTURES"); break;
            default:        Log("SYNTAX: SR SENSOR MASS|THERMAL|EM ON|OFF", ExoColors.FaultText); break;
        }
    }

    private void SetSensor(ref bool flag, bool on, string label, string desc)
    {
        if (flag == on)
        {
            Log($"SENSOR: {label} ALREADY {(on ? "ONLINE" : "OFFLINE")}", ExoColors.ProksText);
            return;
        }
        flag = on;
        _audio.Play(on ? "rover.sensor_on" : "rover.sensor_off");
        Log(on ? $"SENSOR: {label} ONLINE — {desc}" : $"SENSOR: {label} OFFLINE", ExoColors.SignalDim);
    }

    // ── extraction ────────────────────────────────────────────────────────────

    private void TryBeginExtract()
    {
        if (_disabled) { _awaitingTerminate = true; return; }
        if (_mode != FieldMode.Driving || _state != LinkState.Idle) return;

        if (!_world.TryGetDepositAt(_world.RoverX, _world.RoverY, _progress.Sol, out var dep))
        {
            Log("NOTHING TO EXTRACT HERE", ExoColors.FaultText);
            return;
        }

        bool sensorMatches = dep.Kind == "THERMAL" ? _thermalActive : _emActive;
        if (!sensorMatches)
        {
            Log($"{dep.Kind} SENSOR REQUIRED — SR {dep.Kind} ON", ExoColors.FaultText);
            return;
        }

        _activeDeposit = dep;
        _audio.Play("rover.uplink");

        if (dep.Kind == "THERMAL")
        {
            _thermalCursor = 50; _thermalLock = 0; _thermalStrikeCooldown = 0;
            _thermalStrikeCount = 0; _thermalStrikeHits = 0;
            _thermalTarget = 15 + _rng.Next(71);
            _thermalVelocity = (_rng.Next(2) == 0 ? -1 : 1) * RollThermalSpeed();
            _thermalSpeedPhaseTimer = ThermalPhaseMinSec + _rng.NextDouble() * (ThermalPhaseMaxSec - ThermalPhaseMinSec);
            _thermalTimeLeft = ThermalDuration;
            _mode = FieldMode.ExtractThermal;
            Log("THERMAL EXTRACTION — TRACK & STRIKE", ExoColors.SignalDim);
            Log("←/→ TRACK   SPACE STRIKE   ESC ABORT", ExoColors.ProksText);
        }
        else
        {
            _emCursor = 50; _emCapture = 0;
            _emTimeLeft = EmDurationMin + _rng.NextDouble() * (EmDurationMax - EmDurationMin);
            _emPulseCooldown = 0; _emPulseCount = 0; _emPulseHits = 0; _emMoveCooldown = 0;
            _emTarget = 15 + _rng.Next(71);
            _emDriftVelocity = (_rng.Next(2) == 0 ? -1 : 1) * EmDriftSpeed;
            _emHopTimer = EmHopMinSec + _rng.NextDouble() * (EmHopMaxSec - EmHopMinSec);
            _mode = FieldMode.ExtractEm;
            Log("EM EXTRACTION — SIGNAL HOPS FREQUENCY", ExoColors.SignalDim);
            Log("←/→ SEARCH   SPACE PULSE   ESC ABORT", ExoColors.ProksText);
        }
    }

    // ── THERMAL minigame ──────────────────────────────────────────────────────
    // Drifts the vein and applies passive tracking each frame. The strike
    // cooldown ticks down regardless of alignment.
    private void TickThermal(double dt)
    {
        _thermalTimeLeft -= dt;
        if (_thermalStrikeCooldown > 0) _thermalStrikeCooldown = Math.Max(0, _thermalStrikeCooldown - dt);

        // Vein drift bounces at the edges; speed rides a multi-second phase.
        _thermalTarget += _thermalVelocity * dt;
        if (_thermalTarget <= 6 || _thermalTarget >= 94)
        {
            _thermalVelocity = -_thermalVelocity;
            _thermalTarget = Math.Clamp(_thermalTarget, 6, 94);
        }
        if ((_thermalSpeedPhaseTimer -= dt) <= 0)
        {
            _thermalSpeedPhaseTimer = ThermalPhaseMinSec + _rng.NextDouble() * (ThermalPhaseMaxSec - ThermalPhaseMinSec);
            _thermalVelocity = Math.Sign(_thermalVelocity) * RollThermalSpeed();
        }
        if (_rng.NextDouble() < dt * ThermalDirFlipChance) _thermalVelocity = -_thermalVelocity;   // occasional outright reversal

        bool onVein = Math.Abs(_thermalCursor - _thermalTarget) <= ThermalWindow;
        _thermalLock = Math.Clamp(
            _thermalLock + (onVein ? ThermalPassiveGain : -ThermalPassiveDecay) * dt, 0, 100);

        if (_thermalLock >= 100 || _thermalTimeLeft <= 0) ResolveThermal();
    }

    // SPACE fires once off cooldown; a hit clears the floor, a miss costs more than waiting.
    private void StrikeThermal()
    {
        if (_thermalStrikeCooldown > 0) return;
        _thermalStrikeCooldown = ThermalStrikeCooldownSec;
        _thermalStrikeCount++;

        bool onVein = Math.Abs(_thermalCursor - _thermalTarget) <= ThermalWindow;
        if (onVein) _thermalStrikeHits++;
        _thermalLock = Math.Clamp(_thermalLock + (onVein ? ThermalStrikeGood : ThermalStrikeBad), 0, 100);
        _audio.Play(onVein ? "rover.scan_confirmed" : "command.error");

        if (_thermalLock >= 100) ResolveThermal();
    }

    // Drift-speed magnitude for the current phase: mostly baseline, sometimes slow/fast.
    private double RollThermalSpeed()
    {
        double roll = _rng.NextDouble();
        if (roll < 0.25) return ThermalSpeedSlowMin + _rng.NextDouble() * (ThermalSpeedSlowMax - ThermalSpeedSlowMin);
        if (roll < 0.50) return ThermalSpeedFastMin + _rng.NextDouble() * (ThermalSpeedFastMax - ThermalSpeedFastMin);
        return ThermalSpeedBase - ThermalSpeedBaseJitter + _rng.NextDouble() * (2 * ThermalSpeedBaseJitter);
    }

    // Full lock or timeout: LOCK becomes the sample quality; below the floor the core is botched.
    private void ResolveThermal()
    {
        int quality = (int)Math.Round(_thermalLock);
        if (quality < ThermalMinLock)
        {
            FailExtraction("EXTRACTION FAILED — LOCK TOO WEAK");
            return;
        }
        // Same assay template for every core; special deposits append an authored note.
        string note = _world.DepositNote(_activeDeposit.FileId);
        _memory.RegisterDynamicFile(ProxAssayGenerator.Build(
            _activeDeposit.FileId, _activeDeposit.Tier, quality,
            _thermalStrikeCount, _thermalStrikeHits, note, _rng));
        StoreExtracted(_activeDeposit, $" (LOCK {quality}%)");
    }

    // ── EM minigame ───────────────────────────────────────────────────────────
    // Passive tuning plus a discrete PULSE; the carrier drifts continuously and
    // hops. Resolves like THERMAL: CAPTURE at time-up (or 100 early).
    private void TickEm(double dt)
    {
        _emTimeLeft -= dt;
        if (_emPulseCooldown > 0) _emPulseCooldown = Math.Max(0, _emPulseCooldown - dt);
        if (_emMoveCooldown > 0) _emMoveCooldown = Math.Max(0, _emMoveCooldown - dt);

        // Continuous drift keeps a found lock from being "solved and forgotten".
        _emTarget += _emDriftVelocity * dt;
        if (_emTarget <= 15 || _emTarget >= 85)
        {
            _emDriftVelocity = -_emDriftVelocity;
            _emTarget = Math.Clamp(_emTarget, 15, 85);
        }
        if (_rng.NextDouble() < dt * EmDriftFlipChance) _emDriftVelocity = -_emDriftVelocity;

        // Frequency hop: a hard jump guaranteed far from the current carrier.
        if ((_emHopTimer -= dt) <= 0)
        {
            _emHopTimer = EmHopMinSec + _rng.NextDouble() * (EmHopMaxSec - EmHopMinSec);
            double next;
            do { next = 15 + _rng.Next(71); } while (Math.Abs(next - _emTarget) < EmHopMinDistance);
            _emTarget = next;
        }

        // Passive rate graduated by band — only true NO SIGNAL pays the full cost.
        double distance = Math.Abs(_emCursor - _emTarget);
        double rate = distance <= EmWindow       ? EmPassiveGain
                    : distance <= EmStrongRadius ? 0.0
                    : distance <= EmWeakRadius    ? -EmPassiveWeakDecay
                                                  : -EmPassiveDecay;
        _emCapture = Math.Clamp(_emCapture + rate * dt, 0, 100);

        if (_emCapture >= 100 || _emTimeLeft <= 0) ResolveEm();
    }

    // Cooldown-gated so holding an arrow can't cross the range instantly.
    private void TryMoveEm(int delta)
    {
        if (_emMoveCooldown > 0) return;
        _emMoveCooldown = EmMoveCooldownSec;
        _emCursor = Math.Clamp(_emCursor + delta, 0, 100);
    }

    // SPACE fires once off cooldown: a CAPTURE surge if aligned, a cost if not.
    private void PulseEm()
    {
        if (_emPulseCooldown > 0) return;
        _emPulseCooldown = EmPulseCooldownSec;
        _emPulseCount++;

        double distance = Math.Abs(_emCursor - _emTarget);
        bool locked = distance <= EmWindow;
        bool strong = !locked && distance <= EmStrongRadius;
        bool weak   = !locked && !strong && distance <= EmWeakRadius;
        if (locked) _emPulseHits++;

        double delta = locked ? EmPulseGoodLocked
                      : strong ? EmPulseGoodStrong
                      : weak   ? EmPulseGoodWeak
                               : EmPulseBad;
        _emCapture = Math.Clamp(_emCapture + delta, 0, 100);
        _audio.Play(locked || strong ? "rover.scan_confirmed" : weak ? "section.enter" : "command.error");

        if (_emCapture >= 100) ResolveEm();
    }

    // Clean pass/fail — only reaching 100 counts; anything short is a loss.
    private void ResolveEm()
    {
        if (_emCapture >= 100) StoreExtracted(_activeDeposit, "");
        else FailExtraction("TRANSMISSION ENDED — CAPTURE INCOMPLETE");
    }

    // Success: mark the deposit spent and drop the file onto the rover tape.
    // `detail` is appended to the confirmation line (e.g. the achieved LOCK).
    private void StoreExtracted(MissionWorld.Deposit dep, string detail)
    {
        _world.MarkExtracted(dep.FileId);
        _mode = FieldMode.Driving;
        bool added = _memory.AddFileToRover(dep.FileId);
        _audio.Play("rover.scan_confirmed");
        Log(added
                ? $"EXTRACTED — {dep.FileId.ToUpperInvariant()}{detail}"
                : "ROVER STORAGE FULL — SAMPLE LOST",
            ExoColors.SignalText);
    }

    // Failure: the deposit was committed on SR EXTRACT, so it's consumed.
    private void FailExtraction(string reason)
    {
        _world.MarkExtracted(_activeDeposit.FileId);
        _mode = FieldMode.Driving;
        _audio.Play("command.error");
        Log(reason, ExoColors.FaultText);
    }

    // ── convoy sync ─────────────────────────────────────────────────────────────

    private void BeginConvoyChoice()
    {
        _awaitingConvoy = false;
        _mode = FieldMode.ConvoyChoice;
        _rideHeading = _world.ConvoyHeading();   // freeze which way it was going, for a ride
        _audio.Play("rover.uplink");
        Log("SUPPLY RUNNER: legacy unit, still hauling.", ExoColors.SignalDim);
        Log("[1] PULL FILE  [2] HITCH RIDE  ESC", ExoColors.ProksText);
    }

    private void BeginSync()
    {
        _mode = FieldMode.ConvoySync;
        _sync = 0; _syncPos = 0; _syncPosPrev = 0; _syncDir = 1;
        _syncCooldown = 0; _syncHits = 0; _syncMiss = 0; _syncStreak = 0;
        _syncTimeLeft = SyncDurationMin + _rng.NextDouble() * (SyncDurationMax - SyncDurationMin);
        for (int i = 0; i < SyncWindows; i++) _winC[i] = -100;   // sentinel so spacing ignores unspawned
        for (int i = 0; i < SyncWindows; i++) SpawnWindow(i);
        _audio.Play("rover.uplink");
        Log("SYNC — SPACE ON A WINDOW; SMALLER = MORE   ESC", ExoColors.ProksText);
    }

    // Marker sweeps the bar and bounces at the ends; the SPACE cooldown is what
    // makes it a timing test rather than a mash.
    private void TickSync(double dt)
    {
        _syncTimeLeft -= dt;
        if (_syncCooldown > 0) _syncCooldown = Math.Max(0, _syncCooldown - dt);

        _syncPosPrev = _syncPos;   // last-shown position — what a press is judged against

        _syncPos += _syncDir * SyncSweepSpeed * dt;
        if (_syncPos <= 0)   { _syncPos = 0;   _syncDir = 1; }
        if (_syncPos >= 100) { _syncPos = 100; _syncDir = -1; }

        // Windows drift and bounce off the ends; nothing stays campable.
        for (int i = 0; i < SyncWindows; i++)
        {
            _winC[i] += _winV[i] * dt;
            if (_winC[i] <= SyncDriftMin) { _winC[i] = SyncDriftMin; _winV[i] =  Math.Abs(_winV[i]); }
            if (_winC[i] >= SyncDriftMax) { _winC[i] = SyncDriftMax; _winV[i] = -Math.Abs(_winV[i]); }
        }

        if (_sync >= 100 || _syncTimeLeft <= 0) ResolveSync();
    }

    private void PulseSync()
    {
        if (_syncCooldown > 0) return;
        _syncCooldown = SyncCooldownSec;

        // Judge in the same cell resolution the operator sees — "#" on a "=" cell always scores.
        int m = SyncCell(_syncPosPrev);
        int hit = -1;
        for (int i = 0; i < SyncWindows; i++)
        {
            var (a, b) = WinCells(i);
            if (m >= a && m <= b) { hit = i; break; }
        }

        if (hit >= 0)
        {
            _syncStreak++;
            int mult = Math.Min(1 + _syncStreak / SyncStreakStep, SyncMultMax);   // #5 streak multiplier
            _syncHits++;
            _sync = Math.Clamp(_sync + WinReward(_winW[hit]).Pts * mult, 0, 100);
            _audio.Play("rover.scan_confirmed");
            SpawnWindow(hit);   // that window vanishes and reappears elsewhere, new size
            if (_rng.NextDouble() < SyncBounceChance) _syncDir = -_syncDir;
        }
        else
        {
            _syncStreak = 0;    // #5 a miss breaks the streak
            _syncMiss++;
            _sync = Math.Clamp(_sync - SyncMissPenalty, 0, 100);
            _audio.Play("command.error");
        }

        if (_sync >= 100) ResolveSync();
    }

    // Places window i at a fresh centre clear of the others, with a random width.
    private void SpawnWindow(int i)
    {
        _winW[i] = SyncWidthMin + _rng.NextDouble() * (SyncWidthMax - SyncWidthMin);
        _winV[i] = (_rng.Next(2) == 0 ? -1 : 1) * SyncDriftSpeed;   // #2 fresh drift direction
        for (int attempt = 0; attempt < 30; attempt++)
        {
            double c = 8 + _rng.Next(85);
            bool clear = true;
            for (int j = 0; j < SyncWindows; j++)
                if (j != i && Math.Abs(c - _winC[j]) < SyncMinGap) { clear = false; break; }
            if (clear) { _winC[i] = c; return; }
        }
        _winC[i] = 8 + _rng.Next(85);
    }

    // Reward + colour for a window: narrower = brighter and worth more.
    private static (int Pts, string Col) WinReward(double width)
    {
        double t = Math.Clamp((SyncWidthMax - width) / (SyncWidthMax - SyncWidthMin), 0, 1);  // 0 wide .. 1 narrow
        int pts = (int)Math.Round(SyncPtsWide + t * (SyncPtsNarrow - SyncPtsWide));
        string col = width <= 3.0 ? ExoColors.SignalBright : width <= 4.5 ? ExoColors.SignalText : ExoColors.SignalDim;
        return (pts, col);
    }

    // The one quantisation both the hit-check and the render go through.
    private int SyncCell(double pos) =>
        Math.Clamp((int)Math.Round(pos * (_syncBarCells - 1) / 100.0), 0, _syncBarCells - 1);

    private (int A, int B) WinCells(int i)
    {
        double scale = (_syncBarCells - 1) / 100.0;
        int a = Math.Clamp((int)Math.Round((_winC[i] - _winW[i] / 2) * scale), 0, _syncBarCells - 1);
        int b = Math.Clamp((int)Math.Round((_winC[i] + _winW[i] / 2) * scale), 0, _syncBarCells - 1);
        return (a, Math.Max(a, b));   // at least one cell wide
    }

    private void ResolveSync()
    {
        _world.MarkConvoyAttempted();
        _mode = FieldMode.Driving;

        if (_sync < 100) { FailSync("SYNC LOST — RUNNER PULLED AWAY"); return; }

        _audio.Play("rover.scan_confirmed");
        if (_convoyOutcome == ConvoyOutcome.File) GrantSupplyFile();
        else                                      GrantRide();
    }

    // Outcome [1]: reveals the hidden vein (shows under THERMAL) + a manifest naming its grid.
    private void GrantSupplyFile()
    {
        if (!_world.TryGetHiddenVein(out var vein))
        {
            Log("RUNNER CACHE EMPTY — NOTHING TO PULL", ExoColors.ProksText);
            return;
        }
        _world.RevealVein(vein.FileId);
        _memory.RegisterDynamicFile(BuildManifest(vein.X, vein.Y));
        bool added = _memory.AddFileToRover("supply_manifest");
        Log(added ? $"MANIFEST PULLED — VEIN AT {vein.X},{vein.Y}" : "MANIFEST PULLED — TAPE FULL, LOST",
            added ? ExoColors.SignalText : ExoColors.FaultText);
    }

    // Outcome [2]: hitch a ride the runner's way — free, no battery, stops at edge/pit.
    private void GrantRide()
    {
        int moved = _world.RideConvoy(_rideHeading.DX, _rideHeading.DY, ConvoyRideTiles);
        Log(moved > 0 ? $"HITCHED — RODE {moved}U, NO POWER SPENT" : "RIDE BLOCKED — NO CLEAR TRACK",
            moved > 0 ? ExoColors.SignalText : ExoColors.FaultText);
    }

    private const int ConvoyRideTiles = 20;

    private static MemoryFile BuildManifest(int x, int y) => new()
    {
        Id          = "supply_manifest",
        DisplayName = "SUPPLY_MANIFEST",
        Type        = "LOG",
        Blocks      = 2,
        Sol         = "SOL 001",
        Description = "SUPPLY RUNNER CACHE MANIFEST",
        SyncValue   = 25,
        Content =
            "SUPPLY RUNNER — CACHE MANIFEST (PARTIAL DUMP)\n" +
            "-----------------------------------------------------------------------\n" +
            "  ROUTE DB CROSS-REFERENCE: 1 OFF-GRID DEPOSIT\n" +
            $"  CLASS: ACTIVE PROX, HIGH PURITY   GRID: {x}, {y}\n" +
            "  SURVEY STATUS: UNREPORTED (not on the SUIRDC scan overlay)\n" +
            "-----------------------------------------------------------------------\n" +
            "NOTE: Deposit now flagged on your THERMAL sensor. Go dig it out before\n" +
            "the logistics net notices the manifest left with you.\n",
    };

    private void FailSync(string reason)
    {
        _world.MarkConvoyAttempted();
        _mode = FieldMode.Driving;
        _audio.Play("command.error");
        Log(reason, ExoColors.FaultText);
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
        if (_disabled) { _awaitingTerminate = true; return; }   // wrecked/stranded — re-raise the prompt
        if (_state != LinkState.Idle) return;                    // uplink carries one command at a time

        // DEV: instant, free movement — no uplink delay or battery cost.
        if (_isDev)
        {
            for (int i = 0; i < steps && !_awaitingDock; i++) DevInstantStep(dir);
            _world.Persist?.Invoke();
            return;
        }

        _pendingDir     = dir;
        _stepsRemaining = steps;
        _stepsMoved     = 0;
        _phaseElapsedMs = 0;
        _state          = LinkState.Transmitting;
        _audio.Play("rover.uplink");
    }

    // One free, instant cell of DEV movement — no battery cost, no hull risk.
    private void DevInstantStep(Direction dir)
    {
        if (_world.Move(dir, 1) == 0)
        {
            Log("SURVEY BOUNDARY — SR-74 HOLDS", ExoColors.FaultText);
            return;
        }
        if (_world.IsAtBase && !_world.IsDocked) _awaitingDock = true;
    }

    // One physical cell of travel; the map and POS header update live.
    private void StepRover()
    {
        // For fall damage below: only fire on the fall onto low ground, not driving along it.
        bool wasOnHazard = _world.IsOnHazard;

        // Battery pays for this cell (slope + sensors on base cost). Failing to
        // cover it either strands the rover for good or just holds for a gentler line.
        if (!_world.TrySpendForStep(_pendingDir, ActiveSensorCount))
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

        // Move returns 0 only at the survey boundary — everything else is passable.
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

        // Fell onto lethal low ground: fixed hull damage and the drive stops dead.
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

        // Rolling onto the base halts the drive and offers the dock, even mid-MOVE.
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

        string sensor = "SENSOR: " + SensorLabel();
        buffer.WriteAt(hx, 1, sensor, ActiveSensorCount > 0 ? ExoColors.SignalDim : ExoColors.ProksPale);
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
        else MapRenderer.Render(map, _world, _massActive, _thermalActive, _emActive,
                _zoomLevels[_zoom].W, _zoomLevels[_zoom].H, _progress.Sol, _now.TotalSeconds);

        // ── left panel: telemetry — readouts that actually inform a field decision
        int tx = 2, ty = h - 2;
        void Field(string s, string col) { buffer.WriteAt(tx, ty, s, col); tx += s.Length + 4; }

        string alt = _world.Terrain is { } terr
            ? $"ALT {terr.DisplayAltitude(_world.RoverX, _world.RoverY)}M"
            : "ALT ----";
        Field(alt, ExoColors.ProksText);

        int hull = _world.IntegrityDisplay;
        Field($"HULL {hull}%",
            hull > 50 ? ExoColors.ProksText : hull > 25 ? ExoColors.PhosphorText : ExoColors.FaultText);

        int dist = Math.Abs(_world.RoverX - _world.BaseX) + Math.Abs(_world.RoverY - _world.BaseY);
        Field($"TO BASE {dist}U", ExoColors.ProksText);

        int cargoUsed = _memory.GetUsed(StorageLocation.Rover);
        int cargoCap  = _memory.GetCapacity(StorageLocation.Rover);
        Field($"CARGO {cargoUsed}/{cargoCap}KB",
            cargoUsed >= cargoCap ? ExoColors.FaultText : ExoColors.ProksText);

        Field($"BAL ${_account.Funds}", ExoColors.ProksText);

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
        else if (_awaitingConvoy)
        {
            buffer.WriteAt(rightX + 1, h - 2,
                Ui.Truncate("SYNC WITH SUPPLY RUNNER? Y/N", rightW - 2), ExoColors.SignalBright);
        }
        else if (_mode == FieldMode.ConvoyChoice)
        {
            buffer.WriteAt(rightX + 1, h - 2,
                Ui.Truncate("[1] PULL FILE   [2] HITCH RIDE   ESC", rightW - 2), ExoColors.SignalText);
        }
        else if (_mode == FieldMode.ConvoySync)
        {
            RenderConvoySync(buffer, rightX, rightW, h - 3, h - 2);
        }
        else if (_mode == FieldMode.ExtractThermal)
        {
            RenderThermalExtraction(buffer, rightX, rightW, h - 3, h - 2);
        }
        else if (_mode == FieldMode.ExtractEm)
        {
            RenderEmExtraction(buffer, rightX, rightW, h - 3, h - 2);
        }
        else
        {
            string prompt = "> " + _input;
            buffer.WriteAt(rightX + 1, h - 2, prompt, ExoColors.PhosphorText);
            if (_blink.Visible)
                buffer.WriteAt(rightX + 1 + prompt.Length, h - 2, '_', ExoColors.PhosphorDim);
        }
    }

    // Phase feedback above the prompt: growing TX arrow while transmitting, dots while moving.
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

    // Bar of `width` cells: '#' where the target window currently sits, 'o' for
    // the player cursor (drawn last so it always shows even inside the window).
    private static string BuildBar(int width, double cursor, double target, double window)
    {
        var chars = new char[width];
        for (int i = 0; i < width; i++)
        {
            double v = i * 100.0 / (width - 1);
            chars[i] = Math.Abs(v - target) <= window ? '#' : '-';
        }
        int cursorIdx = Math.Clamp((int)Math.Round(cursor * (width - 1) / 100.0), 0, width - 1);
        chars[cursorIdx] = 'o';
        return new string(chars);
    }

    private void RenderConvoySync(IRenderBuffer buffer, int rightX, int rightW, int barY, int statY)
    {
        int innerW = rightW - 2;
        _syncBarCells = Math.Max(10, innerW - 2);   // stretch the bar across the whole panel
        int barW = _syncBarCells;                   // same resolution the hit-check uses
        int mk = SyncCell(_syncPos);

        // Cell spans from the shared WinCells, so what's drawn is what a press hits.
        var win = new (int A, int B, string Col)[SyncWindows];
        for (int w = 0; w < SyncWindows; w++)
        {
            var (a, b) = WinCells(w);
            win[w] = (a, b, WinReward(_winW[w]).Col);
        }

        bool onWindow = false;
        buffer.WriteAt(rightX + 1, barY, "[", ExoColors.ProksBorder);
        for (int i = 0; i < barW; i++)
        {
            int inWin = -1;
            for (int w = 0; w < SyncWindows; w++) if (i >= win[w].A && i <= win[w].B) { inWin = w; break; }
            char ch; string col;
            if (i == mk)      { ch = '#'; col = inWin >= 0 ? ExoColors.SignalBright : ExoColors.PhosphorText; if (inWin >= 0) onWindow = true; }
            else if (inWin >= 0) { ch = '='; col = win[inWin].Col; }
            else                 { ch = '.'; col = ExoColors.ProksBorder; }
            buffer.WriteAt(rightX + 2 + i, barY, ch.ToString(), col);
        }
        buffer.WriteAt(rightX + 2 + barW, barY, "]", ExoColors.ProksBorder);

        int mult = Math.Min(1 + _syncStreak / SyncStreakStep, SyncMultMax);
        string cd = _syncCooldown <= 0 ? "READY" : $"{_syncCooldown,3:0.0}s";
        string status = $"SYNC {(int)_sync,3}%  x{mult}  PULSE {cd}  T-{Math.Max(0, _syncTimeLeft),4:0.0}s  ESC";
        buffer.WriteAt(rightX + 1, statY, Ui.Truncate(status, innerW),
            onWindow ? ExoColors.SignalBright : _syncCooldown <= 0 ? ExoColors.SignalText : ExoColors.PhosphorText);
    }

    private void RenderThermalExtraction(IRenderBuffer buffer, int rightX, int rightW, int barY, int statY)
    {
        int innerW = rightW - 2;
        int barW   = Math.Min(innerW, 44);

        // Bar colour is the whole rule: bright on the vein, fault-red off it.
        bool onVein = Math.Abs(_thermalCursor - _thermalTarget) <= ThermalWindow;
        buffer.WriteAt(rightX + 1, barY,
            BuildBar(barW, _thermalCursor, _thermalTarget, ThermalWindow),
            onVein ? ExoColors.SignalBright : ExoColors.FaultText);

        string strike = _thermalStrikeCooldown <= 0 ? "STRIKE READY" : $"STRIKE {_thermalStrikeCooldown,3:0.0}s";
        string status = $"LOCK {(int)_thermalLock,3}%  {strike}  T-{Math.Max(0, _thermalTimeLeft),4:0.0}s  ←/→ SPACE ESC";
        buffer.WriteAt(rightX + 1, statY, Ui.Truncate(status, innerW),
            _thermalStrikeCooldown <= 0 ? ExoColors.SignalText : ExoColors.PhosphorText);
    }

    private void RenderEmExtraction(IRenderBuffer buffer, int rightX, int rightW, int barY, int statY)
    {
        int innerW = rightW - 2;
        double distance = Math.Abs(_emCursor - _emTarget);
        bool locked   = distance <= EmWindow;
        bool strong   = !locked && distance <= EmStrongRadius;
        bool weak     = !locked && !strong && distance <= EmWeakRadius;

        // Row 1: tuning ruler with the cursor — no target/distance/direction,
        // just your position and a 4-band proximity read.
        int posW = Math.Min(44, Math.Max(10, innerW - 16));
        int cursorIdx = Math.Clamp((int)Math.Round(_emCursor * (posW - 1) / 100.0), 0, posW - 1);

        string band = locked ? "LOCKED       "
                    : strong ? "STRONG SIGNAL"
                    : weak   ? "WEAK SIGNAL  "
                             : "NO SIGNAL    ";
        // Tier ladder, weakest to strongest — used for the label and the fade below.
        string[] tierColors = [ExoColors.ProksDark, ExoColors.ProksPale, ExoColors.SignalDim, ExoColors.SignalBright];
        int bandIdx = locked ? 3 : strong ? 2 : weak ? 1 : 0;
        string bandColor = tierColors[bandIdx];

        // Band colour glows around the cursor and fades one tier per ring outward,
        // symmetric so it never hints which way the carrier lies.
        for (int i = 0; i < posW; i++)
        {
            if (i == cursorIdx) continue;
            int d = Math.Abs(i - cursorIdx);
            string col = d <= 2 ? tierColors[bandIdx]
                       : d <= 4 ? tierColors[Math.Max(0, bandIdx - 1)]
                                : tierColors[0];
            buffer.WriteAt(rightX + 1 + i, barY, "-", col);
        }
        buffer.WriteAt(rightX + 1 + cursorIdx, barY, "o", ExoColors.PhosphorBright);
        buffer.WriteAt(rightX + 1 + posW + 2, barY, Ui.Truncate(band, Math.Max(0, innerW - posW - 2)), bandColor);

        // Row 2: capture % + pulse cooldown, same layout as THERMAL's LOCK/STRIKE line.
        string pulse  = _emPulseCooldown <= 0 ? "PULSE READY" : $"PULSE {_emPulseCooldown,3:0.0}s";
        string status = $"CAPTURE {(int)_emCapture,3}%  {pulse}  T-{Math.Max(0, _emTimeLeft),4:0.0}s  ←/→ SPACE ESC";
        buffer.WriteAt(rightX + 1, statY, Ui.Truncate(status, innerW),
            locked ? ExoColors.SignalText : ExoColors.PhosphorText);
    }

    private string SensorLabel()
    {
        var active = new List<string>(3);
        if (_massActive) active.Add("MASS");
        if (_thermalActive) active.Add("THERMAL");
        if (_emActive) active.Add("EM");
        return active.Count == 0 ? "NONE" : string.Join("+", active);
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
