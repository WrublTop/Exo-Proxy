using ExoProxy.Core;
using ExoProxy.Presentation.Animation;

namespace ExoProxy.Presentation.Screens;

public sealed class BootScreen : IScreen
{
    public string ScreenId => "boot";

    private BootPhaseId _phaseId;
    private DateTimeOffset _startTime;
    private TypewriterEffect? _biosTypewriter;
    private FadeInOutEffect? _crtFade;

    public Task OnEnterAsync(CancellationToken ct)
    {
        _phaseId = BootPhaseId.CrtWarmup;
        _startTime = DateTimeOffset.UtcNow;
        _crtFade = new FadeInOutEffect(ExoColors.FadeAmber, 40);
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
            _crtFade?.Update(time);

            if (_crtFade?.IsHolding == true && elapsed >= TimeSpan.FromMilliseconds(1500))
                _crtFade.StartFadeOut();

            if (_crtFade?.IsDone == true)
            {
                _phaseId = BootPhaseId.BiosHeader;
                _startTime = DateTimeOffset.UtcNow;
                _biosTypewriter = new TypewriterEffect("EXO BIOS Version 1.0");
            }
        }

        _biosTypewriter?.Update(time);
    }

    public void Render(IRenderBuffer buffer)
    {
        switch (_phaseId)
        {
            case BootPhaseId.CrtWarmup:
                buffer.WriteAt(1, 1, "SUPERVISORY UNITED INTERSTELLAR RESEARCH AND DEVELOPMENT CONSORTIUM", _crtFade?.GetCurrentColor() ?? ExoColors.ColorAmber);
                break;
            case BootPhaseId.BiosHeader:
                buffer.WriteAt(1, 1, _biosTypewriter?.GetCurrentText() ?? string.Empty, ExoColors.ColorAmber);
                break;
        }
    }

    public BootScreen()
    {
        _phaseId = BootPhaseId.CrtWarmup;
        _startTime = DateTimeOffset.UtcNow;
    }
}


