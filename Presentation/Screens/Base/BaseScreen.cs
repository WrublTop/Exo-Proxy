using ExoProxy.Core;
using ExoProxy.Data;

namespace ExoProxy.Presentation.Screens.Base;

public sealed class BaseScreen : IScreen
{
    public string ScreenId => "base";

    private readonly Dictionary<string, IBaseSection> _sections;
    private IBaseSection _activeSection;
    private readonly bool _devMode;
    private readonly OperatorProgress _progress;

    // Operator ended the session — Program.cs returns to the boot screen.
    public bool LogoutRequested { get; private set; }

    // Operator powered the terminal down — Program.cs shuts down cleanly.
    public bool ExitRequested { get; private set; }

    public BaseScreen(OperatorAccount account, GameSettings settings,
                      OperatorProgress progress, OperatorRegistry registry,
                      bool devMode = false)
    {
        _devMode  = devMode;
        _progress = progress;

        var commsRepo = new CommsRepository();
        commsRepo.Load(account.Login);

        var memRepo = new MemoryRepository();
        memRepo.Load(account.Login);

        // Surface the first save-integrity warning diegetically in the hub.
        string? loadWarning = progress.LoadWarning
                              ?? memRepo.LoadWarning
                              ?? commsRepo.LoadWarning;

        var hub       = new Sections.HubSection(account, progress, loadWarning,
                                                devMode, commsRepo, memRepo);
        var settings_ = new Sections.SettingsSection(settings, account, registry);
        var comms     = new Sections.CommsSection(account, commsRepo, progress);
        var memory    = new Sections.MemorySection(account, memRepo, progress);

        _sections = new Dictionary<string, IBaseSection>
        {
            [hub.SectionId]       = hub,
            [settings_.SectionId] = settings_,
            [comms.SectionId]     = comms,
            [memory.SectionId]    = memory,
        };

        _activeSection = hub;
    }

    public Task OnEnterAsync(CancellationToken ct) => Task.CompletedTask;
    public Task OnExitAsync(CancellationToken ct) => Task.CompletedTask;

    public void Update(GameTime time, InputEvent? input)
    {
        _activeSection.Update(time, input);

        var response = _activeSection.Response;

        if (response.Request == BaseSectionRequest.GoToSection &&
            response.SectionName is not null &&
            _sections.TryGetValue(response.SectionName, out var next))
        {
            _activeSection = next;
        }
        else if (response.Request == BaseSectionRequest.GoToHub)
        {
            _activeSection = _sections[SectionIds.Hub];
        }
        else if (response.Request == BaseSectionRequest.Logout)
        {
            LogoutRequested = true;
        }
        else if (response.Request == BaseSectionRequest.ExitGame)
        {
            ExitRequested = true;
        }
    }

    public void Render(IRenderBuffer buffer)
    {
        _activeSection.Render(buffer);

        // Dev overlay draws on top of every section so the tester always
        // knows the current state — and that they're not in a real session.
        if (_devMode)
            buffer.WriteAt(0, buffer.Height - 1, $"[DEV] {_progress.SolDisplay}", ExoColors.FaultText);
    }
}
