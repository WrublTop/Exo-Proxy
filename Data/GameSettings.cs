using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data;

// Global display/system preferences. Player progress (SOL etc.) does NOT
// belong here — it is per-operator and lives in OperatorProgress.
public class GameSettings
{
    private static readonly string _savePath =
        Path.Combine(AppContext.BaseDirectory, "SaveData", "settings.yaml");

    private static readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()   // tolerates legacy keys (e.g. old `sol:`)
        .Build();

    public string Theme           { get; set; } = "AMBER";   // wired → ExoColors.Apply
    public string Brightness      { get; set; } = "NORMAL";  // wired → ExoColors.Apply
    public string Contrast        { get; set; } = "MEDIUM";  // TODO: unwired — implement when gameplay UI pass starts
    public bool   Animations      { get; set; } = true;      // TODO: unwired — should gate boot/transfer animations
    public string Language        { get; set; } = "ENGLISH"; // TODO: unwired — requires a string table before wiring
    public string TypewriterSpeed { get; set; } = "NORMAL";  // TODO: unwired — no consumer yet

    public static GameSettings Load()
    {
        if (!File.Exists(_savePath)) return new GameSettings();

        try
        {
            var yaml = File.ReadAllText(_savePath);
            return _deserializer.Deserialize<GameSettings>(yaml) ?? new GameSettings();
        }
        catch
        {
            SaveGuard.Quarantine(_savePath);
            return new GameSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        File.WriteAllText(_savePath, _serializer.Serialize(this));
    }
}
