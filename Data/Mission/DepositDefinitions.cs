using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data.Mission;

// One authored deposit placement from Content/deposits.yaml. Sol window is set
// for EM (live that inclusive range only), null for THERMAL.
public sealed class DepositDefinition
{
    public string FileId     { get; set; } = "";
    public string Kind       { get; set; } = "";
    public int    Tier       { get; set; } = 1;         // proks grade 1-3; ignored for EM
    public string Placement  { get; set; } = "fixed";   // "fixed" | "area"
    public int    X          { get; set; }
    public int    Y          { get; set; }
    public int    AnchorX    { get; set; }
    public int    AnchorY    { get; set; }
    public int    VariationX { get; set; }
    public int    VariationY { get; set; }
    public int?   SolStart   { get; set; }
    public int?   SolEnd     { get; set; }
    public bool   Hidden     { get; set; }              // not sensed until revealed (convoy manifest)
    public string Note       { get; set; } = "";        // extra assay notes for a special deposit
}

// Loads Content/deposits.yaml. Shared content — malformed just means "no deposits".
public static class DepositDefinitions
{
    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static List<DepositDefinition> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try { return _deserializer.Deserialize<List<DepositDefinition>>(File.ReadAllText(path)) ?? []; }
        catch { return []; }
    }
}
