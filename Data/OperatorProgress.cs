using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data;

// Per-operator mission progress. SOL lives here — not in GameSettings —
// so one operator's progress can never advance another operator's inbox.
// Saved to SaveData/progress_{LOGIN}.yaml.
public class OperatorProgress
{
    private string _savePath = "";

    private static readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public int Sol { get; set; } = 1;

    [YamlIgnore]
    public string SolDisplay => $"SOL {Sol:D3}";

    // Set when the save file existed but could not be parsed.
    [YamlIgnore]
    public string? LoadWarning { get; private set; }

    public static OperatorProgress Load(string operatorLogin)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "SaveData",
            $"progress_{operatorLogin.ToUpper()}.yaml");

        OperatorProgress progress;

        if (!File.Exists(path))
        {
            progress = new OperatorProgress();
        }
        else
        {
            try
            {
                progress = _deserializer.Deserialize<OperatorProgress>(File.ReadAllText(path))
                           ?? new OperatorProgress();
            }
            catch
            {
                SaveGuard.Quarantine(path);
                progress = new OperatorProgress
                {
                    LoadWarning = "PROGRESS CHECKSUM FAILURE — SOL COUNTER RESTORED FROM DEFAULTS"
                };
            }
        }

        progress._savePath = path;
        return progress;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        File.WriteAllText(_savePath, _serializer.Serialize(this));
    }
}
