using ExoProxy.Core;
namespace ExoProxy.Presentation.Animation;

public sealed class FadeInOutEffect
{
    private enum FadeState { FadingIn, Holding, FadingOut, Done }
    private FadeState _state;

    public bool IsDone => _state == FadeState.Done;
    public bool IsHolding => _state == FadeState.Holding;

    private readonly List<string> _fadeColors;
    private readonly int _stepMs;
    private int _currentIndex;
    private DateTimeOffset _lastUpdate;

    public string GetCurrentColor() => _fadeColors[_currentIndex];

    public FadeInOutEffect(string[] fadeColors, int stepMs)
    {
        _fadeColors = fadeColors.ToList();
        _stepMs = stepMs;
        _currentIndex = 0;
        _lastUpdate = DateTimeOffset.UtcNow;
        _state = FadeState.FadingIn;
    }

    public void Update(GameTime gt)
    {
        if (_state == FadeState.Holding || _state == FadeState.Done)
            return;

        var timeElapsed = DateTimeOffset.UtcNow - _lastUpdate;
        if (timeElapsed < TimeSpan.FromMilliseconds(_stepMs))
            return;

        _lastUpdate = DateTimeOffset.UtcNow;

        if (_state == FadeState.FadingIn)
        {
            _currentIndex++;
            if (_currentIndex >= _fadeColors.Count)
            {
                _currentIndex = _fadeColors.Count - 1;
                _state = FadeState.Holding;
            }
        }
        else if (_state == FadeState.FadingOut)
        {
            _currentIndex--;
            if (_currentIndex < 0)
            {
                _currentIndex = 0;
                _state = FadeState.Done;
            }
        }
    }

    public void StartFadeOut()
    {
        if (_state != FadeState.Holding)
            return;
        _state = FadeState.FadingOut;
    }
}


