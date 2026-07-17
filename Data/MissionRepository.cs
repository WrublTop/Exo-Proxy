using ExoProxy.Data.Mission;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data;

// Per-operator persistence for the mission in progress — rover position, charge,
// dock latch and map marks. Mirrors MemoryRepository/CommsRepository: load on
// session start, autosave on checkpoints via MissionWorld.Persist.
public class MissionRepository
{
    private string _statePath = "";

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    // The live world the section drives. Defaults to a fresh docked rover until
    // a save is loaded over it.
    public MissionWorld World { get; private set; } = new();

    // Set when a save existed but could not be parsed.
    public string? LoadWarning { get; private set; }

    public void Load(string operatorLogin)
    {
        _statePath = Path.Combine(AppContext.BaseDirectory, "SaveData",
            $"mission_state_{operatorLogin.ToUpper()}.yaml");

        if (File.Exists(_statePath))
        {
            try
            {
                var state = _deserializer.Deserialize<MissionState>(File.ReadAllText(_statePath));
                World = state is null ? new MissionWorld() : MissionWorld.FromState(state);
            }
            catch
            {
                SaveGuard.Quarantine(_statePath);
                World = new MissionWorld();
                Save();
                LoadWarning = "FIELD LINK CHECKSUM FAILURE — ROVER STATE RESTORED FROM DEFAULTS";
            }
        }
        else
        {
            World = new MissionWorld();
        }

        // Wire the autosave hook so checkpoints persist without the section ever
        // touching the repository directly.
        World.Persist = Save;

        // Shared authored terrain — content, not save data, so it loads the same
        // for every operator and isn't quarantined on a bad parse.
        World.Terrain = Terrain.Load(
            Path.Combine(AppContext.BaseDirectory, "Content", "terrain.txt"));

        // Shared authored survey-sector overlay — content like the terrain.
        World.Sectors = SectorMap.Load(
            Path.Combine(AppContext.BaseDirectory, "Content", "sectors.yaml"));

        // Authored deposit placement — content; EnsureFieldEntities rolls it once per campaign.
        World.DepositDefs = DepositDefinitions.Load(
            Path.Combine(AppContext.BaseDirectory, "Content", "deposits.yaml"));
        World.EnsureFieldEntities(new Random());

        // Convoy patrol loop — authored content; the runner just gets a random start each session.
        World.ConvoyPath = ConvoyRoute.Load(
            Path.Combine(AppContext.BaseDirectory, "Content", "convoy_route.yaml"));
        World.SeedConvoy(new Random());
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, _serializer.Serialize(World.ToState()));
    }
}
