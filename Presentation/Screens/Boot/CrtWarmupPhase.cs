using ExoProxy.Core;
namespace ExoProxy.Presentation.Screens.Boot;


public sealed class CrtWarmupPhase : IBootPhase
{
    private static readonly string _suirdcText = "SUPERVISORY UNITED INTERSTELLAR RESEARCH AND DEVELOPMENT CONSORTIUM";
    private static readonly string _suirdcLine = new string('═', 70);
    private DateTimeOffset _startTime;
    private DateTimeOffset _blinkTimer;
    private Random _rng;
    private bool _blinkVisible;
    private int _blinkInterval;

    public CrtWarmupPhase()
    {
        _startTime = DateTimeOffset.UtcNow;
        _blinkTimer = DateTimeOffset.UtcNow;
        _rng = new Random();
        _blinkVisible = true;
        _blinkInterval = 150;
    }

    public void Update(DateTimeOffset now, InputEvent? input)
    {

        var elapsed = now - _startTime;
        if (elapsed < TimeSpan.FromMilliseconds(800))
        {
            if (now - _blinkTimer >= TimeSpan.FromMilliseconds(_blinkInterval))
            {
                _blinkVisible  = !_blinkVisible;
                _blinkTimer    = now;
                _blinkInterval = _blinkVisible ? _rng.Next(150, 350) : _rng.Next(40, 120);
            }
        }
        else if (elapsed < TimeSpan.FromMilliseconds(2000)) { _blinkVisible = true; }
        else if (elapsed < TimeSpan.FromMilliseconds(2150)) { _blinkVisible = false; }
        else if (elapsed < TimeSpan.FromMilliseconds(2350)) { _blinkVisible = true; }
        else { _blinkVisible = false; }

        if (elapsed >= TimeSpan.FromMilliseconds(2650))
            IsDone = true;
    }
    public void Render(IRenderBuffer buffer)
    {
        if (!_blinkVisible) return;

        int textX = (buffer.Width - _suirdcText.Length) / 2;
        int lineX = (buffer.Width - _suirdcLine.Length) / 2;
        int centerY = buffer.Height / 2;

        buffer.WriteAt(lineX, centerY - 1, _suirdcLine, ExoColors.ColorAmber);
        buffer.WriteAt(textX, centerY, _suirdcText, ExoColors.ColorAmber);
        buffer.WriteAt(lineX, centerY + 1, _suirdcLine, ExoColors.ColorAmber);
    }

    public bool IsDone { get; private set; }

}
