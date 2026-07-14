using ExoProxy.Core;

namespace ExoProxy.Presentation.Screens.Boot;

public sealed class BiosHeaderPhase : IBootPhase
{
    private static readonly string[] _biosLines =
    [
        "SUIRDC MODULAR TERMINAL SYSTEMS v.7.12.6LTS, A Remote Acquisition Division Ally",
        "Copyright (C) 1284-97 AJD, Supervisory United Interstellar Research and Development Consortium",
        "",
        "RAD-9000XD Core Processor @ 1.2THz    COMM: VECT-9 Uplink    Status: NOMINAL",
    ];

    private static readonly string[] _logoLines =
    [
        "          SRSR        SRSRSR       ",
        "      SRSRS        RSRSRSRSRSRSR   ",
        "    SRSRS        RSRSR    SRSRSRS  ",
        "   SRSRS        7474        RSRSRS ",
        " SRSRSR         7474         SRSRSR",
        " SRSRSR         7474          SRSRSR",
        "SRSRSR          7474           SRSRSR",
        "RSRSR           7474           RSRSRS",
        "SRSRSR          7474           SRSRSR",
        "RSRSRS          7474           RSRSRS",
        "SRSRSR          7474           SRSRSR",
        " SRSRSR         7474           RSRSR ",
        " RSRSRS         7474          RSRSR  ",
        "  SRSRSR        7474          SRSR   ",
        "    SRSRS       7474        SRSRS    ",
        "      SRSRS     7474     SRSRS       ",
        "         96.834.62.161.28.838        ",
        "                7474 R               ",
        "                7474                 ",
        "                7474                 ",
        "                7474                 ",
        "                7474                 ",
        "                7474                 ",
        "                7474                 ",
        "                7474                 ",
        "                747                  ",
    ];

    private const string _detectHeader = "Remote Acquisition Division — Hardware Integrity Protocol";

    private static readonly (string Label, string Result, int MinMs, int MaxMs, int SettleMs)[] _detectLines =
    [
        ("Detecting RAD-9000XD Core Processor",          "NOMINAL",        200,  400, 200),
        ("Detecting Math Co-Processor",                  "NOT INSTALLED",  300,  500, 600),
        ("Detecting RAD Signal Arbitration Controller",  "OK",             400,  700, 200),
        ("Detecting Chrono-Sync Reference Module",       "SYNCHRONIZED",   800, 1500, 300),
        ("Detecting Phosphor Output Controller",         "ACTIVE",         400,  800, 200),
        ("Detecting Operator Command Interface",         "READY",          200,  400, 200),
        ("Detecting Storage Primary Master",             "READY",         1500, 3000, 300),
        ("Detecting Storage Primary Slave",              "NONE",           400,  700, 200),
        ("Detecting VECT-9 Uplink Primary",              "ONLINE",         600, 1000, 200),
        ("Detecting VECT-9 Uplink Secondary",            "NO SIGNAL",     1500, 2500, 800),
    ];

    private static readonly (bool Visible, int Ms)[] _flickerSeq =
        [(false, 50), (true, 80), (false, 50), (true, 0)];

    private const int DetectStartRow = 7;
    private const int DetectLabelWidth = 45;

    private readonly IAudioService _audio;
    private readonly Random _rng;
    private int _detectIndex;
    private bool _detectResultShown;
    private TimeSpan? _anchor;          // set on first Update tick
    private TimeSpan _detectLineStart;
    private int _detectDelay;
    private bool _flickerVisible;
    private int _flickerStep;
    private bool _endFlickerDone;
    private TimeSpan _flickerTimer;
    private bool _blinkVisible;
    private int _blinkInterval;
    private TimeSpan _blinkTimer;

    public bool IsDone { get; private set; }

    public BiosHeaderPhase(IAudioService audio)
    {
        _audio            = audio;
        _rng              = new Random();
        _detectIndex      = 0;
        _detectResultShown = false;
        _detectDelay      = _rng.Next(_detectLines[0].MinMs, _detectLines[0].MaxMs);
        _blinkVisible     = true;
        _blinkInterval    = 167;
        _flickerVisible   = false;
        _flickerStep      = 0;
        _endFlickerDone   = false;
    }

    public void Update(GameTime time, InputEvent? input)
    {
        var now = time.Total;

        // Anchor all timers on the first tick this phase actually runs —
        // anchoring in the constructor would start the clocks during CRT warmup.
        if (_anchor is null)
        {
            _anchor          = now;
            _detectLineStart = now;
            _blinkTimer      = now;
            _flickerTimer    = now;
            _audio.Play("boot.post");   // single POST beep — "memory OK, all systems go"
        }

        if (input?.Key.Key == ConsoleKey.F4)
        {
            IsDone = true;
            return;
        }

        UpdateFlicker(now);

        if (now - _blinkTimer >= TimeSpan.FromMilliseconds(_blinkInterval))
        {
            _blinkVisible = !_blinkVisible;
            _blinkTimer   = now;
            if (_detectIndex >= _detectLines.Length)
                _blinkInterval = _blinkVisible ? _rng.Next(150, 350) : _rng.Next(40, 120);
        }

        if (_detectIndex >= _detectLines.Length)
        {
            TimeSpan wait = now - _detectLineStart;
            if (!_endFlickerDone && wait >= TimeSpan.FromMilliseconds(2500))
            {
                _endFlickerDone = true;
                StartFlicker(now);
            }
            if (wait >= TimeSpan.FromMilliseconds(3000))
                IsDone = true;
            return;
        }

        TimeSpan lineElapsed = now - _detectLineStart;

        if (!_detectResultShown && lineElapsed >= TimeSpan.FromMilliseconds(_detectDelay))
        {
            _detectResultShown = true;
        }
        else if (_detectResultShown && lineElapsed >= TimeSpan.FromMilliseconds(_detectDelay + _detectLines[_detectIndex].SettleMs))
        {
            _detectIndex++;
            _detectLineStart = now;
            if (_detectIndex < _detectLines.Length)
            {
                _detectResultShown = false;
                _detectDelay       = _rng.Next(_detectLines[_detectIndex].MinMs, _detectLines[_detectIndex].MaxMs);
            }
            else
            {
                _blinkInterval = _rng.Next(150, 350);
                _blinkVisible  = true;
                _blinkTimer    = now;
            }
        }
    }

    public void Render(IRenderBuffer buffer)
    {
        for (int i = 0; i < _biosLines.Length; i++)
            buffer.WriteAt(1, 1 + i, _biosLines[i], ExoColors.ProksText);

        // Disabled: SUIRDC logo in the top-right corner during the BIOS header.
        // if (_flickerVisible)
        // {
        //     int logoX = buffer.Width - 40;
        //     for (int i = 0; i < _logoLines.Length; i++)
        //         buffer.WriteAt(logoX, 4 + i, _logoLines[i], ExoColors.ProksDark);
        // }

        buffer.WriteAt(1, DetectStartRow - 1, _detectHeader, ExoColors.ProksText);

        for (int i = 0; i < _detectIndex && i < _detectLines.Length; i++)
            RenderDetectLine(buffer, i, DetectStartRow + i);

        int bottomRow = buffer.Height - 2;
        buffer.WriteAt(1, bottomRow, "Press ", ExoColors.ProksDark);
        buffer.WriteAt(7, bottomRow, "DEL", ExoColors.ProksPale);
        buffer.WriteAt(10, bottomRow, " to enter SETUP", ExoColors.ProksDark);
        buffer.WriteAt(1, bottomRow + 1, "1294-12-RAD9000XD,SUIRDC-7.12.6LTS-SR74-00", ExoColors.ProksDark);

        if (_detectIndex >= _detectLines.Length)
        {
            if (_blinkVisible)
            {
                int loadRow = DetectStartRow + _detectLines.Length + 1;
                buffer.WriteAt(1, loadRow, "Initializing Memory Diagnostic Sequence...", ExoColors.ProksText);
            }
            return;
        }

        int row = DetectStartRow + _detectIndex;

        if (_detectResultShown)
        {
            RenderDetectLine(buffer, _detectIndex, row);
        }
        else
        {
            string label = "   " + _detectLines[_detectIndex].Label.PadRight(DetectLabelWidth) + "... ";
            buffer.WriteAt(1, row, label, ExoColors.ProksPale);

            int x = 1 + label.Length;
            buffer.WriteAt(x, row, "[Press F4 to skip]", ExoColors.ProksDark);

            if (_blinkVisible)
                buffer.WriteAt(x + "[Press F4 to skip]".Length, row, "_", ExoColors.ProksDark);
        }
    }

    private void RenderDetectLine(IRenderBuffer buffer, int i, int row)
    {
        string label = "   " + _detectLines[i].Label.PadRight(DetectLabelWidth) + "... ";
        buffer.WriteAt(1, row, label, ExoColors.ProksPale);
        buffer.WriteAt(1 + label.Length, row, _detectLines[i].Result, ResultColor(_detectLines[i].Result));
    }

    private static string ResultColor(string result) => result switch
    {
        "NO SIGNAL" => ExoColors.FaultText,
        "NOT INSTALLED" or "NONE" => ExoColors.ProksDark,
        _ => ExoColors.ProksText,
    };

    private void StartFlicker(TimeSpan now)
    {
        _flickerStep    = 0;
        _flickerVisible = _flickerSeq[0].Visible;
        _flickerTimer   = now;
    }

    private void UpdateFlicker(TimeSpan now)
    {
        if (_flickerStep < 0) return;

        int stepMs = _flickerSeq[_flickerStep].Ms;
        if (stepMs == 0 || now - _flickerTimer < TimeSpan.FromMilliseconds(stepMs)) return;

        _flickerStep++;
        if (_flickerStep >= _flickerSeq.Length)
        {
            _flickerStep    = -1;
            _flickerVisible = true;
            return;
        }

        _flickerVisible = _flickerSeq[_flickerStep].Visible;
        _flickerTimer   = now;
    }
}
