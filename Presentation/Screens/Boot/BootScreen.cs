using ExoProxy.Core;
using ExoProxy.Data;

namespace ExoProxy.Presentation.Screens.Boot;

public sealed class BootScreen : IScreen
{
    public string ScreenId => "boot";

    private readonly List<IBootPhase> _phases;
    private int _currentPhase;
    private readonly OperatorRegistry _registry;

    public OperatorAccount? LoggedInAccount =>
        (_phases.OfType<LoginPhase>().FirstOrDefault())?.LoggedInAccount;

    public bool IsBooted => _currentPhase >= _phases.Count;

    public bool IsDevLogin =>
        (_phases.OfType<LoginPhase>().FirstOrDefault())?.IsDevLogin ?? false;

    public OperatorRegistry Registry => _registry;

    public BootScreen()
    {
        _registry = new OperatorRegistry();
        _registry.Load();

        var loginPhase = new LoginPhase(_registry);
        _phases =
        [
            new CrtWarmupPhase(),
            new BiosHeaderPhase(),
            loginPhase,
            new QlinkHandshakePhase(loginPhase),
        ];
        _currentPhase = 0;
    }

    public Task OnEnterAsync(CancellationToken ct) => Task.CompletedTask;
    public Task OnExitAsync(CancellationToken ct) => Task.CompletedTask;

    public void Update(GameTime time, InputEvent? input)
    {
        if (_currentPhase >= _phases.Count) return;

        var phase = _phases[_currentPhase];
        phase.Update(time, input);

        if (phase.IsDone)
        {
            _currentPhase++;

            // DEV login skips the rest of the boot ceremony (Q-LINK handshake)
            // and drops straight into the base screen.
            if (phase is LoginPhase { IsDevLogin: true })
                _currentPhase = _phases.Count;
        }
    }

    public void Render(IRenderBuffer buffer)
    {
        if (_currentPhase >= _phases.Count) return;

        _phases[_currentPhase].Render(buffer);
    }
}
