using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data.Mission;

// Loads Content/convoy_route.yaml. Shared content — malformed just means "no convoy".
public static class ConvoyRoute
{
    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static List<(int X, int Y)> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var file = _deserializer.Deserialize<RouteFile>(File.ReadAllText(path));
            return (file?.Points ?? [])
                .Where(p => p.Count >= 2)
                .Select(p => (p[0], p[1]))
                .ToList();
        }
        catch { return []; }
    }

    private sealed class RouteFile
    {
        public List<List<int>> Points { get; set; } = [];
    }
}
