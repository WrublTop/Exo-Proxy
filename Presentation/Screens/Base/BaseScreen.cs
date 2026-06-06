using ExoProxy.Core;
using ExoProxy.Data;

namespace ExoProxy.Presentation.Screens.Base;

public sealed class BaseScreen : IScreen
{
    public string ScreenId => "base";

    private readonly Dictionary<string, IBaseSection> _sections;
    private IBaseSection _activeSection;

    public BaseScreen(OperatorAccount account, GameSettings settings, OperatorRegistry registry)
    {
        var commsRepo = new CommsRepository();
        commsRepo.Load(account.Login);

        var hub      = new Sections.HubSection(account, settings);
        var settings_ = new Sections.SettingsSection(settings, account, registry);
        var comms    = new Sections.CommsSection(account, commsRepo, settings);

        _sections = new Dictionary<string, IBaseSection>
        {
            [hub.SectionId]       = hub,
            [settings_.SectionId] = settings_,
            [comms.SectionId]     = comms,
        };

        _activeSection = hub;
    }

    public Task OnEnterAsync(CancellationToken ct) => Task.CompletedTask;
    public Task OnExitAsync(CancellationToken ct) => Task.CompletedTask;

    public void Update(GameTime time, InputEvent? input)
    {
        _activeSection.Update(DateTimeOffset.UtcNow, input);

        var response = _activeSection.Response;

        if (response.Request == BaseSectionRequest.GoToSection &&
            response.SectionName is not null &&
            _sections.TryGetValue(response.SectionName, out var next))
        {
            _activeSection = next;
        }
        else if (response.Request == BaseSectionRequest.GoToHub)
        {
            _activeSection = _sections["hub"];
        }
    }

    public void Render(IRenderBuffer buffer)
    {
        _activeSection.Render(buffer);
    }
}
