using ExoProxy.Core;

namespace ExoProxy.Presentation.Screens.Boot;

public sealed class BootScreen : IScreen
{
    public string ScreenId => "boot";

    private readonly List<IBootPhase> _phases;
    private int _currentPhase;

    public BootScreen()
    {
        _phases =
        [
            new CrtWarmupPhase(),
            new BiosHeaderPhase(),
        ];
        _currentPhase = 0;
    }

    public Task OnEnterAsync(CancellationToken ct) => Task.CompletedTask;
    public Task OnExitAsync(CancellationToken ct) => Task.CompletedTask;

    public void Update(GameTime time, InputEvent? input)
    {
        if (_currentPhase >= _phases.Count) return;

        var phase = _phases[_currentPhase];
        phase.Update(DateTimeOffset.UtcNow, input);

        if (phase.IsDone)
            _currentPhase++;
    }

    public void Render(IRenderBuffer buffer)
    {
        if (_currentPhase >= _phases.Count) return;

        _phases[_currentPhase].Render(buffer);
    }
}
