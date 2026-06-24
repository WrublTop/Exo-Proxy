using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data.Mission;

// Loads the authored survey-sector overlay from Content/sectors.yaml. Mirrors
// Terrain.Load: this is shared content, not save data, so a missing or malformed
// file just means "no overlay" — it is never quarantined and never crashes the
// session.
public static class SectorMap
{
    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static IReadOnlyList<Sector> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var file = _deserializer.Deserialize<SectorFile>(File.ReadAllText(path));
            return file?.Sectors ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class SectorFile
    {
        public List<Sector> Sectors { get; set; } = [];
    }
}
