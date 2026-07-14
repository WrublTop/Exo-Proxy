using ExoProxy.Core;
using ExoProxy.Data;
using ExoProxy.Data.Mission;

namespace ExoProxy.Presentation.Screens.Base;

public sealed class BaseScreen : IScreen
{
    public string ScreenId => "base";

    private readonly Dictionary<string, IBaseSection> _sections;
    private IBaseSection _activeSection;
    private readonly bool _devMode;
    private readonly OperatorProgress _progress;
    private readonly IAudioService _audio;

    // The ambient loop currently meant to be playing (base hum vs field bed). We
    // only re-trigger when this actually changes, so bouncing between hub sections
    // doesn't restart the loop.
    private string _currentAmbient = "";

    // The mission world is owned here, not by MissionSection, so the rover's
    // charge keeps ticking up while the operator works the hub, and so its state
    // is loaded and persisted in one place.
    private readonly MissionWorld _world;

    // Operator ended the session — Program.cs returns to the boot screen.
    public bool LogoutRequested { get; private set; }

    // Operator powered the terminal down — Program.cs shuts down cleanly.
    public bool ExitRequested { get; private set; }

    // Rover lost in the field and the operator confirmed termination — Program.cs
    // runs the permadeath (wipe saves, mark the account terminated) and returns to
    // the login screen.
    public bool PerishRequested { get; private set; }

    public BaseScreen(OperatorAccount account, GameSettings settings,
                      OperatorProgress progress, OperatorRegistry registry,
                      IAudioService audio, bool devMode = false)
    {
        _devMode  = devMode;
        _progress = progress;
        _audio    = audio;

        var commsRepo = new CommsRepository();
        commsRepo.Load(account.Login);

        var memRepo = new MemoryRepository();
        memRepo.Load(account.Login);

        var missionRepo = new MissionRepository();
        missionRepo.Load(account.Login);
        _world = missionRepo.World;

        // Surface the first save-integrity warning diegetically in the hub.
        var roverStats = RoverStats.Load(account.Login);

        string? loadWarning = progress.LoadWarning
                              ?? memRepo.LoadWarning
                              ?? commsRepo.LoadWarning
                              ?? roverStats.LoadWarning
                              ?? missionRepo.LoadWarning;

        var hub       = new Sections.HubSection(account, progress, audio, loadWarning,
                                                devMode, commsRepo, memRepo, roverStats, _world);
        var settings_ = new Sections.SettingsSection(settings, account, registry, audio);
        var comms     = new Sections.CommsSection(account, commsRepo, progress, audio);
        var memory    = new Sections.MemorySection(account, memRepo, progress, audio);
        var upgrade   = new Sections.RoverUpgradeSection(roverStats, account, registry);
        var diagnose  = new Sections.DiagnoseSection(account.Login, roverStats);
        var mission   = new Sections.Mission.MissionSection(progress, _world, devMode, audio);

        _sections = new Dictionary<string, IBaseSection>
        {
            [hub.SectionId]       = hub,
            [settings_.SectionId] = settings_,
            [comms.SectionId]     = comms,
            [memory.SectionId]    = memory,
            [upgrade.SectionId]   = upgrade,
            [diagnose.SectionId]  = diagnose,
            [mission.SectionId]   = mission,
        };

        // Resume where the operator actually was: docked → the hub; mid-field →
        // straight back into MISSION. Logging out never escapes the field.
        _activeSection = _world.IsDocked ? hub : (IBaseSection)mission;

        _audio.Play("music.score");   // music begins once the boot ceremony is over
        UpdateAmbient();              // start the right background loop for where we resumed
    }

    // Keep the ambient loop matched to context: the field has its own bed, every
    // base section shares the hub hum. No-op until a file is assigned in the bank.
    private void UpdateAmbient()
    {
        string want = _activeSection.SectionId == SectionIds.Mission
            ? "mission.ambient"
            : "hub.ambient";
        if (want == _currentAmbient) return;
        if (_currentAmbient.Length > 0) _audio.StopLoop(_currentAmbient);
        _currentAmbient = want;
        _audio.Play(want);
    }

    public Task OnEnterAsync(CancellationToken ct) => Task.CompletedTask;
    public Task OnExitAsync(CancellationToken ct) => Task.CompletedTask;

    public void Update(GameTime time, InputEvent? input)
    {
        _audio.Update(time.Delta.TotalSeconds);   // drive music playlist + fade cleanup
        _activeSection.Update(time, input);

        // Recharge while docked anywhere in the base — the hub, comms, memory,
        // diag all count. Stepping into the field (MISSION) ends it: the rover
        // runs on whatever charge it left with.
        if (_world.IsDocked && _activeSection.SectionId != SectionIds.Mission)
            _world.Recharge(time.Delta.TotalSeconds);

        var response = _activeSection.Response;

        if (response.Request == BaseSectionRequest.GoToSection &&
            response.SectionName is not null &&
            _sections.TryGetValue(response.SectionName, out var next))
        {
            if (response.SectionName == SectionIds.Diag &&
                next is Sections.DiagnoseSection diagnose)
            {
                diagnose.BeginScan(time.Total);
            }

            _activeSection = next;
            _audio.Play("section.enter");
        }
        else if (response.Request == BaseSectionRequest.GoToHub)
        {
            _activeSection = _sections[SectionIds.Hub];
            _audio.Play("section.back");
        }
        else if (response.Request == BaseSectionRequest.Dock)
        {
            // Docking advances the mission day and drops the operator back into
            // the hub, reconnected to SUIRDC's servers. SOL is persisted at once
            // so a crash can't replay the day.
            _progress.Sol++;
            _progress.Save();
            _activeSection = _sections[SectionIds.Hub];
            _audio.Play("rover.dock");
        }
        else if (response.Request == BaseSectionRequest.Perish)
        {
            // No persist — Program.cs is about to wipe this operator's saves.
            PerishRequested = true;
        }
        else if (response.Request == BaseSectionRequest.Logout)
        {
            _world.Persist?.Invoke();   // freeze the excursion before leaving
            LogoutRequested = true;
        }
        else if (response.Request == BaseSectionRequest.ExitGame)
        {
            _world.Persist?.Invoke();
            ExitRequested = true;
        }

        UpdateAmbient();   // swap the background loop if we changed context
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
