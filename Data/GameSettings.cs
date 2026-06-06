using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data;

public class GameSettings
{
    private static readonly string _savePath =
        Path.Combine(AppContext.BaseDirectory, "SaveData", "settings.yaml");

    private static readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public int    Sol              { get; set; } = 1;
    public string SolDisplay      => $"SOL {Sol:D3}";

    public string Theme           { get; set; } = "AMBER";
    public string Brightness      { get; set; } = "NORMAL";
    public string Contrast        { get; set; } = "MEDIUM";
    public string Animations      { get; set; } = "ON";
    public string Language        { get; set; } = "ENGLISH";
    public string TypewriterSpeed { get; set; } = "NORMAL";

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
            return new GameSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        File.WriteAllText(_savePath, _serializer.Serialize(this));
    }
}
