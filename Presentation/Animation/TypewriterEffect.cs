using ExoProxy.Core;

namespace ExoProxy.Presentation.Animation;

public sealed class TypewriterEffect
{
    private readonly string _fullText;
    private int _currentIndex;
    private DateTimeOffset _lastUpdate;

    public TypewriterEffect(string fullText)
    {
        _fullText = fullText;
        _currentIndex = 0;
        _lastUpdate = DateTimeOffset.UtcNow;
    }

    public void Update(GameTime gt)
    {
        var timeElapsed = DateTimeOffset.UtcNow - _lastUpdate;
        if (timeElapsed >= TimeSpan.FromMilliseconds(50))
        {
            _currentIndex++;
            _lastUpdate = DateTimeOffset.UtcNow;
        }
        _currentIndex = Math.Min(_currentIndex, _fullText.Length);
    }

    public string GetCurrentText() => _fullText.Substring(0, _currentIndex);

    public bool IsDone => _currentIndex >= _fullText.Length;
}


