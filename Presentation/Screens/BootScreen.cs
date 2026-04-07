using ExoProxy.Core;

namespace ExoProxy.Presentation.Screens;

public sealed class BootScreen : IScreen
{
    public string ScreenId => "boot";

    private BootPhaseId _phaseId;
    private DateTimeOffset _startTime;

    public Task OnEnterAsync(CancellationToken ct)
    {
        _phaseId = BootPhaseId.CrtWarmup;
        _startTime = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public Task OnExitAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public void Update(GameTime time, InputEvent? input)
    {
        TimeSpan elapsed = DateTimeOffset.UtcNow - _startTime;

        if (elapsed >= TimeSpan.FromMilliseconds(2500))
        {
            _phaseId = BootPhaseId.BiosHeader;
            _startTime = DateTimeOffset.UtcNow;
        }
    }

    public void Render(IRenderBuffer buffer)
    {
        switch (_phaseId)
        {
            case BootPhaseId.CrtWarmup:
                buffer.WriteAt(0, 0, "EXOPROXY BIOS v1.0", ExoColors.ColorAmber);
                break;
        }
    }

    public BootScreen()
    {
        _phaseId = BootPhaseId.CrtWarmup;
        _startTime = DateTimeOffset.UtcNow;
    }
}