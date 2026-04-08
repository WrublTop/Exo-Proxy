using ExoProxy.Core;

namespace ExoProxy.Engine;

public sealed class ScreenManager
{
    private readonly List<IScreen> _screens;
    private IScreen? _activeScreen;

    public ScreenManager()
    {
        _screens = new List<IScreen>();
        _activeScreen = null;
    }

    public void AddScreen(IScreen screen)
    {
        _screens.Add(screen);
    }

    public void SetActive(IScreen screen, CancellationToken ct = default)
    {
        _activeScreen = screen;
        _ = screen.OnEnterAsync(ct);
    }

    public void Update(GameTime gt, InputEvent? input)
    {
        _activeScreen?.Update(gt, input);
    }

    public void Render(IRenderBuffer buffer)
    {
        _activeScreen?.Render(buffer);
    }
}

