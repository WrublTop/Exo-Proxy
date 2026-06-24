namespace ExoProxy.Data.Mission;

// Plain serializable snapshot of a mission in progress, written per-operator to
// SaveData/mission_state_{LOGIN}.yaml. Kept separate from MissionWorld so the
// domain object can keep its logic and private setters while YAML round-trips a
// flat, mutable bag. MapMark is a positional record (no parameterless ctor), so
// marks ride along as their own simple DTO.
public sealed class MissionState
{
    public int RoverX { get; set; }
    public int RoverY { get; set; }
    public int Charge { get; set; }

    // Hull integrity. Defaults to full so a pre-integrity save loads undamaged.
    public int Integrity { get; set; } = MissionWorld.MaxIntegrity;

    public bool IsDocked { get; set; }
    public List<MarkState> Marks { get; set; } = [];

    // Labels of bases the operator has reached. Base 1 (home) is always known.
    public List<string> DiscoveredBases { get; set; } = ["1"];
}

public sealed class MarkState
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
}
