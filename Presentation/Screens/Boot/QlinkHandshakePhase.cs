using ExoProxy.Core;

namespace ExoProxy.Presentation.Screens.Boot;

public sealed class QlinkHandshakePhase : IBootPhase
{
    private readonly LoginPhase _loginPhase;
    private readonly IAudioService _audio;
    private bool _carrierBeeped;

    private record struct StreamEvent(int DelayMs, string Text, string Color, bool UpdateLast = false);

    private List<StreamEvent> _events = [];
    private readonly List<(string Text, string Color)> _lines = new();
    private int _eventIndex;
    private TimeSpan _nextEventTime;
    private bool _eventsBuilt;
    private TimeSpan _phaseStartTime;
    private TimeSpan _lastNow;

    private bool _blinkVisible = true;
    private TimeSpan _blinkTimer;
    private const int BlinkMs = 500;

    private bool _windingDown;
    private TimeSpan _windDownStart;
    private double _totalDurationMs;

    // ── Frequency-band equalizer ───────────────────────────────────────────
    private const string EqMarker = "__EQ__";
    private const string EqAxisMarker = "__EQAXIS__";
    private const int EqRows = 8;
    private const int EqCols = 31;
    private const int EqCarrier = 12;
    private const int EqMaxBar = 7;

    private int _eqStartIndex = -1;
    private int _eqAxisIndex = -1;
    private readonly int[] _eqHeights = new int[EqCols];
    private readonly int[] _eqTargets = new int[EqCols];
    private readonly int[] _eqIdleBase = new int[EqCols];

    // 0=scan  1=flash-blank  2=flash-full  3=idle-pulse
    private int _eqPhase;
    private float _eqMarkerPos;
    private TimeSpan _eqPhaseTimer;
    private TimeSpan _eqStepTimer;
    private TimeSpan _eqTargetTimer;
    private TimeSpan _eqSpikeTimer;
    private TimeSpan _eqFlashTimer;
    private TimeSpan _eqIdleTargetTimer;
    private readonly Random _eqRng = new();

    private static readonly string _eqAxisString = BuildEqAxisString();

    public bool IsDone { get; private set; }

    public QlinkHandshakePhase(LoginPhase loginPhase, IAudioService audio)
    {
        _loginPhase = loginPhase;
        _audio      = audio;
        // Timers anchor on the first Update tick (see _eventsBuilt block).
    }

    private static string TxArrow(string prefix, int dashes, string suffix)
    {
        string arrow = new string('─', dashes) + "►";
        string pad = new string(' ', 11 - arrow.Length);
        return $"{prefix}{arrow}{pad}  {suffix}";
    }

    private static string RxArrow(int dashes, string timestamp)
    {
        string arrow = "◄" + new string('─', dashes);
        int spaces = 42 + (10 - dashes);
        return new string(' ', spaces) + arrow + "  " + timestamp;
    }

    private static string BuildEqAxisString()
    {
        char[] a = new char[62];
        for (int i = 0; i < 62; i++) a[i] = ' ';
        void Place(int col, string s)
        {
            int p = col * 2;
            for (int j = 0; j < s.Length && p + j < 62; j++) a[p + j] = s[j];
        }
        Place(0, "100");
        Place(5, "120");
        Place(10, "140");
        Place(15, "160");
        Place(20, "180");
        Place(25, "200");
        Place(29, "MHz");
        return new string(a);
    }

    private void InitEq(TimeSpan now)
    {
        for (int i = 0; i < EqCols; i++)
        {
            _eqHeights[i] = 1;
            _eqTargets[i] = _eqRng.Next(1, EqRows + 1);
        }
        _eqMarkerPos       = 0f;
        _eqPhase           = 0;
        _eqStepTimer       = now;
        _eqTargetTimer     = now;
        _eqSpikeTimer      = now;
        _eqPhaseTimer      = now;
        _eqFlashTimer      = now;
        _eqIdleTargetTimer = now;
    }

    private static int BellTarget(int dist) => dist switch
    {
        0 or 1 or 2 => 8,
        3 or 4 or 5 => 7,
        6 or 7 or 8 => 6,
        9 or 10 or 11 => 5,
        12 or 13 => 4,
        14 or 15 => 3,
        _ => 2,
    };

    private void UpdateEq(TimeSpan now)
    {
        if (_eqStartIndex < 0) return;

        const int stepMs = 40;
        const int targetMs = 200;
        const int totalMs = 5000;

        int elapsedMs = (int)(now - _eqPhaseTimer).TotalMilliseconds;
        float progress = Math.Clamp(elapsedMs / (float)totalMs, 0f, 1f);
        float noiseAmpF = 3f * (1f - progress);
        int noiseRange = Math.Min(EqRows - 1, (int)(noiseAmpF * 2f));

        // ── Scan marker: sweep → return → lock ───────────────────────
        if (progress < 0.55f)
            _eqMarkerPos = progress / 0.55f * (EqCols - 1);
        else if (progress < 0.70f)
            _eqMarkerPos = (EqCols - 1) - (progress - 0.55f) / 0.15f * ((EqCols - 1) - EqCarrier);
        else
            _eqMarkerPos = EqCarrier;

        // ── Phase transitions ─────────────────────────────────────────
        if (_eqPhase == 0 && progress >= 0.70f)
        {
            _eqPhase      = 1;
            _eqFlashTimer = now;
        }

        if (_eqPhase == 1 && (now - _eqFlashTimer).TotalMilliseconds >= 150)
        {
            _eqPhase      = 2;
            _eqFlashTimer = now;
            if (!_carrierBeeped) { _audio.Play("boot.carrier_lock"); _carrierBeeped = true; }  // carrier locked
            for (int i = 0; i < EqCols; i++)
            {
                _eqHeights[i]  = EqMaxBar;
                _eqTargets[i]  = BellTarget(Math.Abs(i - EqCarrier));
                _eqIdleBase[i] = _eqTargets[i];
            }
        }

        if (_eqPhase == 2 && (now - _eqFlashTimer).TotalMilliseconds >= 300)
        {
            _eqPhase           = 3;
            _eqIdleTargetTimer = now;
        }

        // ── Phase 0: occasional spike burst + regular noise refresh ───
        if (_eqPhase == 0)
        {
            if (now - _eqSpikeTimer >= TimeSpan.FromMilliseconds(700))
            {
                _eqSpikeTimer = now;
                if (_eqRng.NextDouble() < 0.65)
                    _eqTargets[_eqRng.Next(0, EqCols)] = EqRows;
            }

            if (now - _eqTargetTimer >= TimeSpan.FromMilliseconds(targetMs))
            {
                _eqTargetTimer = now;
                for (int i = 0; i < EqCols; i++)
                {
                    if (_eqRng.NextDouble() > noiseAmpF / 3.0) continue;
                    int dist = Math.Abs(i - EqCarrier);
                    int noise = noiseRange > 0 ? _eqRng.Next(-noiseRange, noiseRange + 1) : 0;
                    _eqTargets[i] = Math.Clamp(BellTarget(dist) + noise, 1, EqRows);
                }
            }
        }

        // ── Phase 3: idle pulse — frequent, visible breathing ─────────
        if (_eqPhase == 3 && (now - _eqIdleTargetTimer).TotalMilliseconds >= 200)
        {
            _eqIdleTargetTimer = now;
            for (int i = 0; i < EqCols; i++)
            {
                bool isCarrier = i == EqCarrier;
                if (!isCarrier && _eqRng.NextDouble() > 0.60) continue;
                int nudge = _eqRng.Next(-1, 2);
                _eqTargets[i] = Math.Clamp(_eqIdleBase[i] + nudge, 1, EqMaxBar);
            }
        }

        // ── Physics: skip during flash, run for scan and idle ─────────
        if (_eqPhase == 1 || _eqPhase == 2) return;
        if (now - _eqStepTimer < TimeSpan.FromMilliseconds(stepMs)) return;
        _eqStepTimer = now;

        int physStep = _eqPhase == 3 ? 1 : Math.Max(1, (int)Math.Ceiling(noiseAmpF));
        for (int i = 0; i < EqCols; i++)
        {
            if (_eqHeights[i] < _eqTargets[i])
                _eqHeights[i] = Math.Min(_eqHeights[i] + physStep, _eqTargets[i]);
            else if (_eqHeights[i] > _eqTargets[i])
                _eqHeights[i] = Math.Max(_eqHeights[i] - physStep, _eqTargets[i]);
        }
    }

    private List<StreamEvent> BuildEvents()
    {
        var proks    = ExoColors.ProksText;
        var proksP   = ExoColors.ProksPale;
        var proksBdr = ExoColors.ProksBorder;
        var proksDrk = ExoColors.ProksDark;
        var phos     = ExoColors.PhosphorText;
        var sig      = ExoColors.SignalText;
        var sigDm    = ExoColors.SignalDim;
        var fault    = ExoColors.FaultText;

        string rawOp = _loginPhase.LoggedInAccount?.Login ?? "UNKNOWN";
        string op = rawOp.Length > 12 ? rawOp[..12] : rawOp;
        string sessionId = $"0x{Random.Shared.Next():X8}";

        const string p1 = "TX  [QLINK v2.1]  [SEQ:0001]  [CRC:A3F2] ";
        const string p2 = "TX  [AUTH REQ]    [SEQ:0002]  [CRC:7B1E] ";
        const string p3 = "TX  [KEY EXCH]    [SEQ:0003]  [CRC:9F4A] ";
        const string p4 = "TX  [TELEM REQ]   [SEQ:0004]  [CRC:2D9A] ";

        const string timeout = "                                                       TIMEOUT";

        return
        [
            new(0,    "QLINK PROTOCOL v2.1 — UPLINK INITIALIZATION",                                    proks),
            new(100,  new string('═', 62),                                                               proksBdr),
            new(600,  "",                                                                                 proksBdr),

            // ── Frequency scanner ──────────────────────────────────────────
            new(200,  "SCANNING FREQUENCY BAND...",                                                      proksP),
            new(500,  EqMarker,                                                                          sigDm),
            new(0,    EqAxisMarker,                                                                      proksDrk),
            new(5000, "CARRIER DETECTED  148.842 MHz",                                                   sig),
            new(500,  "",                                                                                 proksBdr),

            // ── SNR acquisition ────────────────────────────────────────────
            new(200,  "SIGNAL ACQUISITION",                                                              proks),
            new(400,  "SNR  [░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░]   0 dB",                                  sigDm),
            new(500,  "SNR  [▒▒▒▒▒▒▒░░░░░░░░░░░░░░░░░░░░░░░]   8 dB",                                  sigDm, UpdateLast: true),
            new(500,  "SNR  [▒▒▒▒▒▒▒▒▒▒▒▒▒▒░░░░░░░░░░░░░░░░]  16 dB",                                  sigDm, UpdateLast: true),
            new(500,  "SNR  [▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒░░░░░░░░░]  24 dB",                                  sigDm, UpdateLast: true),
            new(500,  "SNR  [▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒░░]  31 dB",                                  sig,   UpdateLast: true),
            new(400,  "CARRIER LOCK ......................................... OK",                         phos),
            new(500,  "",                                                                                 proksBdr),

            // ── Doppler correction ─────────────────────────────────────────
            new(200,  "DOPPLER CORRECTION",                                                              proks),
            new(400,  "DRIFT  +3.41 kHz  CORRECTING...",                                                 proksP),
            new(600,  "DRIFT  +1.24 kHz  CORRECTING...",                                                 proksP, UpdateLast: true),
            new(600,  "DRIFT  +0.41 kHz  CORRECTING...",                                                 proksP, UpdateLast: true),
            new(600,  "DRIFT  +0.02 kHz  STABLE ............................ OK",                         phos,   UpdateLast: true),
            new(500,  "",                                                                                 proksBdr),

            // ── Channel diagnostics ────────────────────────────────────────
            new(200,  "CHANNEL DIAGNOSTICS",                                                             proks),
            new(250,  "  UPLINK RATE ................................... 847 QBps",                        proksP),
            new(200,  "  DOWNLINK RATE ................................. 631 QBps",                        proksP),
            new(200,  "  PACKET LOSS ..................................... 0.2%",                          proksP),
            new(200,  "  LATENCY ........................................ 2.3s",                           proksP),
            new(200,  "  QEC LAYER ...................................... ACTIVE",                          proks),
            new(500,  "",                                                                                 proksBdr),

            new(400,  "RELAY NODE  ARX-7 [ORBITAL]  SIGNAL INTEGRITY: 94% .... OK",                     phos),
            new(600,  "",                                                                                 proksBdr),
            new(0,    new string('─', 62),                                                               proksBdr),
            new(300,  "",                                                                                 proksBdr),

            // ── Packet 1: QLINK version ────────────────────────────────────
            new(300,  TxArrow(p1, 0,  "T+0.0s"),                                                        proksP),
            new(150,  TxArrow(p1, 5,  "T+0.0s"),                                                        proksP, UpdateLast: true),
            new(150,  TxArrow(p1, 10, "T+0.0s"),                                                        proksP, UpdateLast: true),
            new(2300, RxArrow(0,  "T+2.6s"),                                                             sigDm),
            new(150,  RxArrow(5,  "T+2.6s"),                                                             sigDm, UpdateLast: true),
            new(150,  RxArrow(10, "T+2.6s"),                                                             sigDm, UpdateLast: true),
            new(200,  "                                          [QLINK v2.1] COMPATIBLE",               proks),
            new(700,  "",                                                                                 proksBdr),

            // ── Packet 2: AUTH — TIMEOUT blinks twice, then retry ──────────
            new(300,  TxArrow(p2, 0,  "T+0.0s"),                                                        proksP),
            new(150,  TxArrow(p2, 5,  "T+0.0s"),                                                        proksP, UpdateLast: true),
            new(150,  TxArrow(p2, 10, "T+0.0s"),                                                        proksP, UpdateLast: true),
            new(3500, timeout,                                                                            fault),
            new(220,  "",                                                                                 fault, UpdateLast: true),
            new(220,  timeout,                                                                            fault, UpdateLast: true),
            new(220,  "",                                                                                 fault, UpdateLast: true),
            new(220,  timeout,                                                                            fault, UpdateLast: true),
            new(1000, TxArrow(p2, 0,  "RETRY 1/3"),                                                     proksP),
            new(150,  TxArrow(p2, 5,  "RETRY 1/3"),                                                     proksP, UpdateLast: true),
            new(150,  TxArrow(p2, 10, "RETRY 1/3"),                                                     proksP, UpdateLast: true),
            new(2400, RxArrow(0,  "T+2.7s"),                                                             sigDm),
            new(150,  RxArrow(5,  "T+2.7s"),                                                             sigDm, UpdateLast: true),
            new(150,  RxArrow(10, "T+2.7s"),                                                             sigDm, UpdateLast: true),
            new(200,  "                                          [AUTH GRANTED]",                         phos),
            new(700,  "",                                                                                 proksBdr),

            // ── Packet 3: KEY EXCHANGE — relay cache ───────────────────────
            new(300,  TxArrow(p3, 0,  "T+0.0s"),                                                        proksP),
            new(150,  TxArrow(p3, 5,  "T+0.0s"),                                                        proksP, UpdateLast: true),
            new(150,  TxArrow(p3, 10, "T+0.0s"),                                                        proksP, UpdateLast: true),
            new(1500, RxArrow(0,  "T+0.9s"),                                                             sigDm),
            new(150,  RxArrow(5,  "T+0.9s"),                                                             sigDm, UpdateLast: true),
            new(150,  RxArrow(10, "T+0.9s"),                                                             sigDm, UpdateLast: true),
            new(200,  "                                          [KEY CONFIRMED]",                        phos),
            new(200,  "SESSION KEY  [A3F2...8C1D] .......................... OK",                         phos),
            new(700,  "",                                                                                 proksBdr),

            // ── Packet 4: TELEMETRY — wolny, prawie timeout ────────────────
            new(300,  TxArrow(p4, 0,  "T+0.0s"),                                                        proksP),
            new(150,  TxArrow(p4, 5,  "T+0.0s"),                                                        proksP, UpdateLast: true),
            new(150,  TxArrow(p4, 10, "T+0.0s"),                                                        proksP, UpdateLast: true),
            new(3100, RxArrow(0,  "T+3.5s"),                                                             sigDm),
            new(150,  RxArrow(5,  "T+3.5s"),                                                             sigDm, UpdateLast: true),
            new(150,  RxArrow(10, "T+3.5s"),                                                             sigDm, UpdateLast: true),
            new(500,  "",                                                                                 proksBdr),

            // ── Telemetry ──────────────────────────────────────────────────
            new(300,  "RX  4E 52 3A 38 31 20 48 55 4C 3A 39 36 20 50 4F 53...",                         sigDm),
            new(2000, "PARSING TELEMETRY.",                                                              proks),
            new(300,  "PARSING TELEMETRY..",                                                             proks, UpdateLast: true),
            new(300,  "PARSING TELEMETRY...",                                                            proks, UpdateLast: true),
            new(600,  "",                                                                                 proksBdr),
            new(300,  "  UNIT DESIGNATION .................................... SR-74",                     sig),
            new(300,  "  POWER SYSTEMS ........................................ 81%",                      sig),
            new(300,  "  HULL INTEGRITY ....................................... 96%",                      sig),
            new(300,  "  POSITION .................. 34.2°N  117.8°E  ALT +1,247 m",                     sig),
            new(500,  "",                                                                                 sigDm),

            // ── Environment ────────────────────────────────────────────────
            new(300,  "ENVIRONMENT SNAPSHOT",                                                            sig),
            new(300,  "  SURFACE TEMP ..................................... 187 K",                        sig),
            new(300,  "  RADIATION INDEX ................................ 2.4 mSv",                       sig),
            new(300,  "  ATMOSPHERIC PRESSURE ......................... 0.03 kPa",                        sig),
            new(500,  "",                                                                                 sigDm),

            // ── Module diagnostics ─────────────────────────────────────────
            new(0,    new string('─', 62),                                                               proksBdr),
            new(300,  "RUNNING MODULE DIAGNOSTICS...",                                                   proks),
            new(300,  "  DRIVE SYSTEM ...................................... TESTING",                     proksP),
            new(2000, "  DRIVE SYSTEM ....................................... OK",                         phos,  UpdateLast: true),
            new(200,  "  POWER MANAGEMENT .................................. TESTING",                     proksP),
            new(1500, "  POWER MANAGEMENT ................................... OK",                         phos,  UpdateLast: true),
            new(200,  "  SENSOR ARRAY ...................................... TESTING",                     proksP),
            new(2200, "  SENSOR ARRAY ....................................... OK",                         phos,  UpdateLast: true),
            new(200,  "  SAMPLE COLLECTOR .................................. TESTING",                     proksP),
            new(1500, "  SAMPLE COLLECTOR ................................... OK",                         phos,  UpdateLast: true),
            new(200,  "  COMMS RELAY ....................................... TESTING",                     proksP),
            new(1800, "  COMMS RELAY ........................................ OK",                         phos,  UpdateLast: true),
            new(500,  "",                                                                                 proksBdr),

            // ── Clock sync ─────────────────────────────────────────────────
            new(300,  "MISSION CLOCK SYNC",                                                              proks),
            new(400,  "  STATION  SOL 001  08:14:33",                                                    proksP),
            new(2500, "  SR-74    SOL 001  08:14:35  DELTA +2s  CORRECTING...  OK",                     sig),
            new(500,  "",                                                                                 proksBdr),

            // ── Operator session ───────────────────────────────────────────
            new(0,    new string('─', 62),                                                               proksBdr),
            new(300,  "REGISTERING OPERATOR SESSION",                                                    proks),
            new(300,  $"  OPERATOR ..................................... {op}",                            proksP),
            new(200,  "  ACCESS TIER ................................... DELTA-2",                          proksP),
            new(200,  $"  SESSION ID ........................................ {sessionId}",                proksP),
            new(500,  "",                                                                                 proksBdr),

            new(600,  "DOWNLINK QUEUE  0 PACKETS — FIRST SESSION",                                      proksP),
            new(600,  "",                                                                                 proksBdr),
            new(300,  new string('═', 62),                                                               proksBdr),

            // ── Final line: flash-in for emphasis ─────────────────────────
            new(500,  "",                                                                                 phos),
            new(200,  "QLINK SESSION OPEN — SR-74 UPLINK READY",                                        phos,  UpdateLast: true),
            new(250,  "",                                                                                 phos,  UpdateLast: true),
            new(300,  "QLINK SESSION OPEN — SR-74 UPLINK READY",                                        phos,  UpdateLast: true),
        ];
    }

    public void Update(GameTime time, InputEvent? input)
    {
        var now = time.Total;
        if (IsDone) return;
        _lastNow = now;

        if (_windingDown)
        {
            if ((now - _windDownStart).TotalMilliseconds >= 300)
                IsDone = true;
            return;
        }

        if (input?.Key.Key == ConsoleKey.F4)
        {
            _audio.StopLoop("computer.thinking");
            IsDone = true;
            return;
        }

        if (now - _blinkTimer >= TimeSpan.FromMilliseconds(BlinkMs))
        {
            _blinkVisible = !_blinkVisible;
            _blinkTimer   = now;
        }

        if (!_eventsBuilt)
        {
            _events           = BuildEvents();
            _eventsBuilt      = true;
            _nextEventTime    = now;
            _phaseStartTime   = now;
            _blinkTimer       = now;
            _totalDurationMs  = _events.Sum(e => (double)e.DelayMs) + 1500.0;
            _audio.Play("boot.handshake");       // modem warble as the uplink starts negotiating
            _audio.Play("computer.thinking");    // heavy-task hum while the link negotiates
        }

        while (_eventIndex < _events.Count && now >= _nextEventTime)
        {
            var ev = _events[_eventIndex];

            if (ev.UpdateLast && _lines.Count > 0)
                _lines[^1] = (ev.Text, ev.Color);
            else
                _lines.Add((ev.Text, ev.Color));

            if (ev.Text == EqMarker && _eqStartIndex < 0)
            {
                _eqStartIndex = _lines.Count - 1;
                InitEq(now);
            }
            if (ev.Text == EqAxisMarker && _eqAxisIndex < 0)
                _eqAxisIndex = _lines.Count - 1;

            _eventIndex++;

            if (_eventIndex < _events.Count)
                _nextEventTime = now + TimeSpan.FromMilliseconds(_events[_eventIndex].DelayMs);
            else
                _nextEventTime = now + TimeSpan.FromMilliseconds(1500);
        }

        UpdateEq(now);

        if (_eventIndex >= _events.Count && now >= _nextEventTime)
        {
            _audio.StopLoop("computer.thinking");   // link established — machine settles
            _windingDown   = true;
            _windDownStart = now;
        }
    }

    public void Render(IRenderBuffer buffer)
    {
        if (!_eventsBuilt) return;
        if (_windingDown) return;

        const int marginTop = 1;
        const int marginBottom = 4;
        int available = buffer.Height - marginTop - marginBottom;
        int startX = (buffer.Width - 62) / 2;

        int startLine = Math.Max(0, _lines.Count - available);
        int count = Math.Min(_lines.Count - startLine, available);

        for (int i = 0; i < count; i++)
        {
            int lineIdx = startLine + i;
            var (text, color) = _lines[lineIdx];

            if (_eqStartIndex >= 0 && lineIdx == _eqStartIndex)
            {
                if (_eqPhase == 1) continue;

                const string bars = "▁▂▃▄▅▆▇";
                bool flashFull = (_eqPhase == 2);

                for (int col = 0; col < EqCols; col++)
                {
                    int h = flashFull ? EqMaxBar : Math.Clamp(_eqHeights[col], 1, EqMaxBar);

                    string barColor;
                    if (_eqPhase < 3)
                        barColor = flashFull ? ExoColors.SignalText : ExoColors.SignalDim;
                    else
                    {
                        int dist = Math.Abs(col - EqCarrier);
                        barColor = dist <= 3 ? ExoColors.SignalText : ExoColors.SignalDim;
                    }

                    buffer.WriteAt(startX + col * 2, marginTop + i, bars[h - 1].ToString(), barColor);
                }
            }
            else if (_eqAxisIndex >= 0 && lineIdx == _eqAxisIndex)
            {
                buffer.WriteAt(startX, marginTop + i, _eqAxisString, ExoColors.ProksDark);
                int markerCol = Math.Clamp((int)Math.Round(_eqMarkerPos), 0, EqCols - 1);
                buffer.WriteAt(startX + markerCol * 2, marginTop + i, "▲", ExoColors.SignalText);
            }
            else
            {
                buffer.WriteAt(startX, marginTop + i, text, color);
            }
        }

        bool longWait = _eventIndex < _events.Count && _events[_eventIndex].DelayMs >= 800;
        bool nextIsUpdateLast = _eventIndex < _events.Count && _events[_eventIndex].UpdateLast;

        // spinner during long waits — replaces cursor
        if (longWait && _lastNow < _nextEventTime)
        {
            const string sp = "|/-\\";
            int frame = (int)((long)_lastNow.TotalMilliseconds / 120) % sp.Length;
            for (int i = startLine + count - 1; i >= startLine; i--)
            {
                var (lt, lc) = _lines[i];
                if (string.IsNullOrEmpty(lt)) continue;
                if (lt == EqMarker || lt == EqAxisMarker) continue;
                if (lc == ExoColors.FaultText) break;
                int row = marginTop + (i - startLine);
                buffer.WriteAt(startX + lt.Length + 2, row, sp[frame].ToString(), ExoColors.ProksPale);
                break;
            }
        }

        // status bar: pulsing uplink indicator + ETA countdown
        double elapsed = (_lastNow - _phaseStartTime).TotalMilliseconds;
        double remaining = Math.Max(0.0, _totalDurationMs - elapsed);
        int remSec = (int)(remaining / 1000.0);
        string etaText = $"ETA  {remSec / 60:D2}:{remSec % 60:D2}";

        buffer.WriteAt(startX, buffer.Height - 3, "◈", _blinkVisible ? ExoColors.ProksText : ExoColors.ProksDark);
        buffer.WriteAt(startX + 2, buffer.Height - 3, "UPLINK ACTIVE", ExoColors.ProksPale);
        buffer.WriteAt(startX + 62 - etaText.Length, buffer.Height - 3, etaText, ExoColors.ProksPale);

        if ((_lastNow - _phaseStartTime).TotalMilliseconds > 2000)
            buffer.WriteAt((buffer.Width - 23) / 2, buffer.Height - 2, "F4 Skip QLINK handshake", ExoColors.ProksDark);
    }
}
