using System.Globalization;
using System.Text;

namespace ExoProxy.Data;

// Builds the assay flavour text after a THERMAL extraction. One shared template;
// the numbers and notes move with how well the minigame went, not the file's identity.
public static class ProxAssayGenerator
{
    // One source of truth for the tier ladder — TierIndex and the printed table both use it.
    private sealed record Tier(string Name, int LockMin, int LockMax, int ConcMin, int ConcMax, string Note, string Flag);

    private static readonly Tier[] _tiers =
    [
        new("POOR",       0,  34, 10, 24, "Marginal yield. Core badly fractured — extraction technique flagged for review.", ""),
        new("FAIR",      35,  64, 25, 44, "Usable yield. Core shows minor fracturing from extraction stress.", ""),
        new("GOOD",      65,  89, 45, 64, "Concentration well above nominal. Clean core, minimal contamination.", ""),
        new("EXCELLENT", 90, 100, 65, 78, "Active Prox reads warm — the vein resealed the borehole before the bit fully cleared. Flagged for priority follow-up.", "[!!]"),
    ];

    // Deposit grade picks the band; lock% only slides within it. Tiers 1..3.
    private static readonly (int Min, int Max)[] _tierBands =
    [
        (0, 0),        // tier 0 — unused
        (30, 58),      // tier 1 COMMON
        (55, 80),      // tier 2 RICH
        (78, 100),     // tier 3 PRIME
    ];

    // depositTier picks the band, lockPercent slides within it; strikes feed the log.
    public static MemoryFile Build(string fileId, int depositTier, int lockPercent, int strikesTotal, int strikesHit, string extraNote, Random rng)
    {
        int t = Math.Clamp(depositTier, 1, 3);
        var (bandMin, bandMax) = _tierBands[t];
        int quality = Math.Clamp(bandMin + lockPercent * (bandMax - bandMin) / 100, 0, 100);

        int tierIndex = TierIndex(quality);
        var tier = _tiers[tierIndex];
        int conc = tier.ConcMin + rng.Next(tier.ConcMax - tier.ConcMin + 1);
        int sil = 58 - (conc - 12) / 2 + rng.Next(-3, 4);
        int fe = 6 + rng.Next(-2, 3);
        double depth = 2.5 + rng.NextDouble() * 3.5;
        string grade = t == 3 ? "PRIME" : t == 2 ? "RICH" : "COMMON";

        var sb = new StringBuilder();
        sb.Append(string.Format(CultureInfo.InvariantCulture,
            "DRILL CORE ASSAY — SR-74 FIELD EXTRACTION                    DEPTH: {0:0.0}m\n" +
            "SEAM GRADE: {8}\n" +
            "-----------------------------------------------------------------------\n" +
            "COMPONENT                 CONCENTRATION    BASELINE    DEVIATION\n" +
            "ACTIVE PROX                  {1,3}.0 %          12.0 %      {2:+0.0;-0.0} %  {3}\n" +
            "SILICATE MATRIX              {4,3}.0 %          58.0 %      {5:+0;-0} %\n" +
            "FE-NI ALLOY TRACE              {6,2}.0 %           6.0 %      {7:+0;-0} %\n" +
            "-----------------------------------------------------------------------\n\n",
            depth, conc, conc - 12.0, tier.Flag, sil, sil - 58, fe, fe - 6, grade));

        sb.Append("EXTRACTION LOG (SR-74 DRILL TELEMETRY):\n");
        sb.Append($"  DRILL STRIKES: {strikesHit}/{strikesTotal} ON-SIGNAL   FINAL LOCK: {lockPercent}%\n");
        sb.Append("  YIELD CLASSIFICATION (FIELD INSTRUMENT SCALE):\n");
        foreach (var tr in _tiers)
        {
            string range = tr.LockMax == 100 ? $"{tr.LockMin}-100%" : $"{tr.LockMin,2}-{tr.LockMax}%";
            string mark = tr == tier ? "X" : " ";
            sb.Append($"    {tr.Name,-10}{range,9}   [{mark}]{(tr == tier ? "  <- THIS SAMPLE" : "")}\n");
        }

        // NOTE section at the bottom: tier note, plus a special deposit's field notes.
        bool hasNote = !string.IsNullOrWhiteSpace(extraNote);
        sb.Append("\n-----------------------------------------------------------------------\n");
        sb.Append($"NOTE: {tier.Note}\n");
        if (hasNote)
        {
            sb.Append('\n');
            sb.Append(extraNote.TrimEnd());
            sb.Append('\n');
        }

        return new MemoryFile
        {
            Id          = fileId,
            DisplayName = fileId.ToUpperInvariant(),
            Type        = "GEO",
            Blocks      = 2,
            Sol         = "SOL 001",
            Description = $"DRILL CORE ASSAY — {grade} PROX SEAM",
            Content     = sb.ToString(),
            SyncValue   = t * 12 + quality / 4 + (hasNote ? 15 : 0),   // grade + clean core + note bonus
        };
    }

    private static int TierIndex(int quality) => quality switch
    {
        >= 90 => 3,
        >= 65 => 2,
        >= 35 => 1,
        _ => 0,
    };
}
