using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data;

public class RoverElectronics
{
    private static readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private bool _isDamaged;
    private bool _isShortCircuited;
    private bool _isOpenCircuit;

    public int Resistance { get; set; }
    public int ID { get; set; }
    public string Name { get; set; } = "";
    public int DisplayedResistance =>
        IsShortCircuited ? 0 :
        IsOpenCircuit ? 9998 :
        Resistance;

    public bool IsDamaged
    {
        get => _isDamaged;
        set
        {
            _isDamaged = value;
            if (!value)
            {
                _isShortCircuited = false;
                _isOpenCircuit = false;
            }
        }
    }

    public bool IsShortCircuited
    {
        get => _isDamaged && _isShortCircuited;
        set
        {
            if (!value)
            {
                _isShortCircuited = false;
                return;
            }

            _isDamaged = true;
            _isShortCircuited = true;
            _isOpenCircuit = false;
        }
    }

    public bool IsOpenCircuit
    {
        get => _isDamaged && _isOpenCircuit;
        set
        {
            if (!value)
            {
                _isOpenCircuit = false;
                return;
            }

            _isDamaged = true;
            _isOpenCircuit = true;
            _isShortCircuited = false;
        }
    }

    public static IReadOnlyList<RoverElectronics> CreateDefaultReadings(int roverTotalResistance)
    {
        int safeTotal = Math.Max(0, roverTotalResistance);
        int[] weights = [15, 10, 10, 10, 10, 15, 10, 20];
        int assigned = 0;

        int GetResistance(int index)
        {
            if (index == weights.Length - 1)
                return safeTotal - assigned;

            int resistance = safeTotal * weights[index] / 100;
            assigned += resistance;
            return resistance;
        }

        return
        [
            new() { ID = 1, Name = "Power Supply Resistance", Resistance = GetResistance(0) },
            new() { ID = 2, Name = "ES field meter output Resistance", Resistance = GetResistance(1) },
            new() { ID = 3, Name = "EM field meter output Resistance", Resistance = GetResistance(2) },
            new() { ID = 4, Name = "Thermometer Output Resistance", Resistance = GetResistance(3) },
            new() { ID = 5, Name = "Higrometer Output Resistance", Resistance = GetResistance(4) },
            new() { ID = 6, Name = "UES&SR Output Resistance", Resistance = GetResistance(5) },
            new() { ID = 7, Name = "Filters Resistance", Resistance = GetResistance(6) },
            new() { ID = 8, Name = "Engine input Resistance", Resistance = GetResistance(7) },
        ];
    }

    public static List<RoverElectronics> Load(string operatorLogin, int roverTotalResistance)
    {
        string path = GetSavePath(operatorLogin);
        var defaults = new List<RoverElectronics>(CreateDefaultReadings(roverTotalResistance));

        if (!File.Exists(path))
            return defaults;

        try
        {
            var loaded = _deserializer.Deserialize<List<RoverElectronics>>(File.ReadAllText(path));
            if (loaded is null || loaded.Count == 0)
                return defaults;

            return NormalizeLoadedReadings(loaded, defaults);
        }
        catch
        {
            SaveGuard.Quarantine(path);
            return defaults;
        }
    }

    public static void Save(string operatorLogin, IReadOnlyList<RoverElectronics> electronics)
    {
        string path = GetSavePath(operatorLogin);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, _serializer.Serialize(electronics));
    }

    private static string GetSavePath(string operatorLogin) =>
        Path.Combine(AppContext.BaseDirectory, "SaveData",
            $"rover_electronics_{operatorLogin.ToUpper()}.yaml");

    private static List<RoverElectronics> NormalizeLoadedReadings(
        List<RoverElectronics> loaded,
        List<RoverElectronics> defaults)
    {
        var normalized = new List<RoverElectronics>(defaults.Count);

        foreach (var fallback in defaults)
        {
            var item = loaded.FirstOrDefault(e => e.ID == fallback.ID) ?? fallback;
            item.Name = fallback.Name;

            if (!item.IsDamaged)
            {
                item.IsOpenCircuit = false;
                item.IsShortCircuited = false;
            }

            normalized.Add(item);
        }

        return normalized;
    }
}
