using ExoProxy.Core;

namespace ExoProxy.Engine;

public sealed class ScreenManager
{
    private IScreen? _activeScreen;

    public async Task SetActiveAsync(IScreen screen, CancellationToken ct = default)
    {
        if (_activeScreen is not null)
            await _activeScreen.OnExitAsync(ct);
        _activeScreen = screen;
        await screen.OnEnterAsync(ct);
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

