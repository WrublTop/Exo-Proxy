using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data;

public sealed class RoverStats
{
    private string _savePath = "";

    private static readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public int BatteryCapacity { get; set; } = 1000;
    public int CurrentBatteryCapacity { get; set; } = 1000;
    public double ESDResistance { get; set; } = 0.10;
    public double EMResistance { get; set; } = 0.10;
    public double ChassisTightness { get; set; } = 0.50;
    public double HighTemperatureResistance { get; set; } = 0.20;
    public double LowTemperatureResistance { get; set; } = 0.20;
    public double RadiationResistance { get; set; } = 0.10;
    public int RoverTotalResistance { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public int CurrentHealth { get; set; } = 100;
    public int ProcessedDamageThresholds { get; set; }

    [YamlIgnore]
    public string? LoadWarning { get; private set; }

    public static RoverStats Load(string operatorLogin)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "SaveData",
            $"rover_stats_{operatorLogin.ToUpper()}.yaml");

        RoverStats stats;

        if (!File.Exists(path))
        {
            stats = new RoverStats();
        }
        else
        {
            try
            {
                stats = _deserializer.Deserialize<RoverStats>(File.ReadAllText(path))
                        ?? new RoverStats();
            }
            catch
            {
                SaveGuard.Quarantine(path);
                stats = new RoverStats
                {
                    LoadWarning = "ROVER UPGRADE DATA FAILURE - STATS RESTORED FROM DEFAULTS"
                };
            }
        }

        stats._savePath = path;
        return stats;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        File.WriteAllText(_savePath, _serializer.Serialize(this));
    }

    public void ResetUpgradesToDefaults()
    {
        BatteryCapacity = 1000;
        ESDResistance = 0.10;
        EMResistance = 0.10;
        ChassisTightness = 0.50;
        HighTemperatureResistance = 0.20;
        LowTemperatureResistance = 0.20;
        RadiationResistance = 0.10;
        MaxHealth = 100;

        CurrentBatteryCapacity = Math.Min(CurrentBatteryCapacity, BatteryCapacity);
        CurrentHealth = Math.Min(CurrentHealth, MaxHealth);
    }
}
