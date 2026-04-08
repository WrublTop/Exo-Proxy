using ExoProxy.Core;

namespace ExoProxy.Presentation.Screens;

public sealed class BootScreen : IScreen
{
    public string ScreenId => "boot";

    private BootPhaseId _phaseId;
    private DateTimeOffset _startTime;
    private DateTimeOffset _blinkTimer;
    private bool _blinkVisible;
    private int _blinkInterval;
    private readonly Random _rng = new();

    // CrtWarmup
    private static readonly string _suirdcText = "SUPERVISORY UNITED INTERSTELLAR RESEARCH AND DEVELOPMENT CONSORTIUM";
    private static readonly string _suirdcLine = new string('═', 70);

    // BiosHeader
    private static readonly string[] _biosLines =
    [
        "SUIRDC MODULAR TERMINAL SYSTEMS v.7.12.6LTS, A Remote Acquisition Division Ally",
        "Copyright (C) 1284-97 AJD, Supervisory United Interstellar Research and Development Consortium",
        "",
        "RAD-9000XD Core Processor @ 1.2THz    COMM: VECT-9 Uplink    Status: NOMINAL",
    ];
    private bool _biosBlinkVisible;
    private DateTimeOffset _biosBlinkTimer;

    public Task OnEnterAsync(CancellationToken ct)
    {
        _phaseId = BootPhaseId.CrtWarmup;
        _startTime = DateTimeOffset.UtcNow;
        _blinkTimer = DateTimeOffset.UtcNow;
        _blinkVisible = true;
        _blinkInterval = _rng.Next(120, 300);
        return Task.CompletedTask;
    }

    public Task OnExitAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public void Update(GameTime time, InputEvent? input)
    {
        TimeSpan elapsed = DateTimeOffset.UtcNow - _startTime;

        if (_phaseId == BootPhaseId.CrtWarmup)
        {
            if (elapsed < TimeSpan.FromMilliseconds(800))
            {
                if (DateTimeOffset.UtcNow - _blinkTimer >= TimeSpan.FromMilliseconds(_blinkInterval))
                {
                    _blinkVisible = !_blinkVisible;
                    _blinkTimer = DateTimeOffset.UtcNow;
                    _blinkInterval = _blinkVisible
                        ? _rng.Next(150, 350)
                        : _rng.Next(40, 120);
                }
            }
            else if (elapsed < TimeSpan.FromMilliseconds(2000))
            {
                _blinkVisible = true;
            }
            else if (elapsed < TimeSpan.FromMilliseconds(2150))
            {
                _blinkVisible = false;
            }
            else if (elapsed < TimeSpan.FromMilliseconds(2350))
            {
                _blinkVisible = true;
            }
            else
            {
                _blinkVisible = false;
            }

            if (elapsed >= TimeSpan.FromMilliseconds(2650))
            {
                _phaseId = BootPhaseId.BiosHeader;
                _startTime = DateTimeOffset.UtcNow;
                _biosBlinkVisible = false;
                _biosBlinkTimer = DateTimeOffset.UtcNow;
            }
        }
        else if (_phaseId == BootPhaseId.BiosHeader)
        {
            _biosBlinkVisible = true;
        }
    }

    public void Render(IRenderBuffer buffer)
    {
        switch (_phaseId)
        {
            case BootPhaseId.CrtWarmup:
                if (_blinkVisible)
                {
                    int textX = (buffer.Width - _suirdcText.Length) / 2;
                    int lineX = (buffer.Width - _suirdcLine.Length) / 2;
                    int centerY = buffer.Height / 2;
                    buffer.WriteAt(lineX, centerY - 1, _suirdcLine, ExoColors.ColorAmber);
                    buffer.WriteAt(textX, centerY, _suirdcText, ExoColors.ColorAmber);
                    buffer.WriteAt(lineX, centerY + 1, _suirdcLine, ExoColors.ColorAmber);
                }
                break;

            case BootPhaseId.BiosHeader:
                if (_biosBlinkVisible)
                    for (int i = 0; i < _biosLines.Length; i++)
                        buffer.WriteAt(1, 1 + i, _biosLines[i], ExoColors.ColorAmber);
                break;
        }
    }

    public BootScreen()
    {
        _phaseId = BootPhaseId.CrtWarmup;
        _startTime = DateTimeOffset.UtcNow;
        _blinkTimer = DateTimeOffset.UtcNow;
        _blinkVisible = true;
        _blinkInterval = 150;
    }
}
