namespace ExoProxy.Data.Mission;

// The mission terrain and everything that lives on it. For the walking
// skeleton that is just the rover and the base station; deposits, anomalies
// and per-operator persistence arrive later.
//
// Coordinates are world units — one unit is one grid cell on the map. The
// base station sits at (0,0) and the world extends into negative coordinates
// in every direction. Bounds are placeholders until the team tunes them.
public sealed class MissionWorld
{
    // Survey rectangle — the playable bounds. The authored land fits inside with a
    // thin ocean margin; the rover hard-stops here (the lethal deep ocean and the
    // off-map void lie beyond, and there's nothing to find there). Move clamps to
    // this box, so the edge reads as a survey boundary rather than a wall in view.
    public const int MinX = -180, MaxX = 180;
    public const int MinY = -160, MaxY = 160;

    // ── battery economy ───────────────────────────────────────────────────────
    // Charge is stored in fine-grained "units" (like a phone's mAh) so the per-
    // cell cost can carry sub-percent precision once sensors, terrain and upgrade
    // modifiers stack onto it. The player only ever sees the derived percent.
    //
    // 10000 capacity ÷ 65 base cost ≈ 154 flat cells — a ceiling never reached,
    // because slope (charged on Δlevel²), scanning and bad routing pile on. Real
    // range lands ~60 cells driving blind, ~110–120 routing well. See the
    // battery-economy design notes.
    public const int Capacity = 10000;
    public const int BaseMoveCost = 65;

    // Survey bases on Cyra-6. Base 1 is the campaign start and the rover's home
    // (spawn, docking, recharge), up in the northern highlands (Maciek's authored
    // placement). Bases 2 and 3 — west-centre and south — start hidden-dim on the
    // map and reveal when the rover reaches them; docking/recharge there is later
    // gameplay. The terrain stays centred on the world origin, so the opening POS
    // reads a non-zero coordinate by design.
    public readonly record struct BaseStation(string Label, int X, int Y);
    public static readonly BaseStation[] Bases =
    [
        new("1",  20, 100),
        new("2", -30,  10),
        new("3",  30, -50),
    ];

    // BASE 1 is "home" for the docking / IsAtBase logic.
    public int BaseX => Bases[0].X;
    public int BaseY => Bases[0].Y;

    private readonly HashSet<string> _discovered = ["1"];
    public bool IsBaseDiscovered(string label) => _discovered.Contains(label);

    public int RoverX { get; private set; }
    public int RoverY { get; private set; }

    // True charge in units; the readout is the derived percent. Starts full.
    public int Charge { get; private set; }

    // Percent shown to the operator. Capacity 10000 makes this exact.
    public int PercentDisplay => Charge / 100;

    public bool IsAtBase => RoverX == BaseX && RoverY == BaseY;

    // Docking is a latch, not just "sitting on the base tile": after an
    // excursion the rover can be ON the base and still undocked, which is what
    // makes the section offer the DOCK prompt. The session starts docked.
    public bool IsDocked { get; private set; } = true;

    // Confirmed dock — advances the loop (the section raises SOL via BaseScreen)
    // and, later, starts the recharge.
    public void Dock() => IsDocked = true;

    public const int MaxMarks = 5;

    private readonly List<MapMark> _marks = [];
    public IReadOnlyList<MapMark> Marks => _marks;

    // The rover's path this session: visited nodes plus the lattice segments
    // between them. The renderer recolours the grid characters along this path (it
    // doesn't stamp dots), so the trail reads as a track drawn on the grid.
    // Session-only — not saved (a campaign-long trail would bloat the file and
    // isn't needed for navigation). Edge key (x, y, H): a horizontal edge stores
    // its west node, a vertical edge stores its south node.
    private readonly HashSet<(int X, int Y)> _trail = [];
    private readonly HashSet<(int X, int Y, bool H)> _trailEdges = [];
    public IReadOnlySet<(int X, int Y)> Trail => _trail;
    public IReadOnlySet<(int X, int Y, bool H)> TrailEdges => _trailEdges;

    // Set by MissionRepository to an autosave callback. The section and the
    // coordinator invoke it at checkpoints (move done, mark set, dock, logout) —
    // never per-frame, so the recharge tick doesn't hammer the disk.
    public Action? Persist { get; set; }

    // Shared authored heightmap. Loaded once and attached here; never part of
    // the per-operator save (ToState/FromState don't touch it).
    public Terrain? Terrain { get; set; }

    // Authored survey-sector overlay (the SUIRDC grid). Shared world content
    // like Terrain — loaded once, never part of the per-operator save.
    public IReadOnlyList<Sector> Sectors { get; set; } = [];

    // The sector the rover currently sits in, or null when between sectors.
    // First match wins if authored rectangles overlap.
    public Sector? SectorAt(int x, int y) => Sectors.FirstOrDefault(s => s.Contains(x, y));

    public MissionWorld()
    {
        RoverX = BaseX;
        RoverY = BaseY;
        Charge = Capacity;
        _trail.Add((RoverX, RoverY));
    }

    // ── persistence snapshot ──────────────────────────────────────────────────
    public MissionState ToState() => new()
    {
        RoverX    = RoverX,
        RoverY    = RoverY,
        Charge    = Charge,
        Integrity = Integrity,
        IsDocked  = IsDocked,
        Marks    = _marks.Select(m => new MarkState { Name = m.Name, X = m.X, Y = m.Y }).ToList(),
        DiscoveredBases = _discovered.ToList(),
    };

    // Rebuilds a world from a saved snapshot, clamping anything a corrupt save
    // could push out of range so the rover can never load off the map or with an
    // impossible charge.
    public static MissionWorld FromState(MissionState s)
    {
        var w = new MissionWorld
        {
            RoverX    = Math.Clamp(s.RoverX, MinX, MaxX),
            RoverY    = Math.Clamp(s.RoverY, MinY, MaxY),
            Charge    = Math.Clamp(s.Charge, 0, Capacity),
            Integrity = Math.Clamp(s.Integrity, 0, MaxIntegrity),
            IsDocked  = s.IsDocked,
        };
        // A latch can't be docked away from the base — guard against a bad save.
        if (!w.IsAtBase) w.IsDocked = false;

        foreach (var m in s.Marks)
            if (w._marks.Count < MaxMarks)
                w._marks.Add(new MapMark(m.Name, m.X, m.Y));

        w._discovered.Clear();
        w._discovered.Add("1");                       // home base is always known
        foreach (var lbl in s.DiscoveredBases) w._discovered.Add(lbl);

        w._trail.Clear();
        w._trailEdges.Clear();
        w._trail.Add((w.RoverX, w.RoverY));           // trail is session-only

        return w;
    }

    // Dev RESET — wipe the excursion back to a fresh docked rover.
    public void Reset()
    {
        RoverX = BaseX;
        RoverY = BaseY;
        Charge = Capacity;
        _chargeCarry = 0;
        Integrity = MaxIntegrity;
        IsDocked = true;
        _marks.Clear();
        _trail.Clear();
        _trailEdges.Clear();
        _trail.Add((RoverX, RoverY));
        _discovered.Clear();
        _discovered.Add("1");
        Persist?.Invoke();
    }

    // Battery surcharge for elevation change between the current cell and the
    // next, charged on |Δlevel|² — a convex curve, so gentle ground stays cheap
    // while steep climbs and drops cost disproportionately more. Climbing and
    // descending both tax the drive. Main lever for how much terrain shapes a
    // route: on Cyra-6 ~80% of steps are Δ≤2 (cheap) while the steep 7% tail
    // (Δ5+) carries the real cost, so avoiding it ≈ doubles range. Tunable.
    public const int SlopeCostSquared = 18;

    // Battery surcharge per step while a sensor is online — a scan is power-
    // hungry, so you can't survey the whole map with one running. Tunable.
    public const int SensorStepDrain = 20;

    // Cost of the next single cell of travel in `dir`: the base move cost, plus
    // the squared terrain slope onto that cell, plus any active-sensor drain.
    // Upgrade discounts will plug in here later. With no terrain loaded the slope
    // term is zero.
    public int StepCost(Direction dir, bool sensorActive)
    {
        int cost = BaseMoveCost;
        if (Terrain is { } t)
        {
            var (dx, dy) = dir.Delta();
            int d = Math.Abs(t.LevelAt(RoverX + dx, RoverY + dy) - t.LevelAt(RoverX, RoverY));
            cost += d * d * SlopeCostSquared;
        }
        if (sensorActive) cost += SensorStepDrain;
        return cost;
    }

    // Draws the cost of one cell from the battery. Returns false — and leaves
    // the charge untouched — when there isn't enough for a full step, so the
    // rover simply stops where it is. Like an ATM: it checks before it pays.
    public bool TrySpendForStep(Direction dir, bool sensorActive)
    {
        int cost = StepCost(dir, sensorActive);
        if (Charge < cost) return false;
        Charge -= cost;
        return true;
    }

    // ── recharge (docked at base) ─────────────────────────────────────────────
    // Two-phase, diminishing-returns curve: a fast climb to 80% then a slow
    // trickle for the last 20%. A full charge takes ~5 minutes, but the slow tail
    // means a rational operator leaves around 80% rather than idling for 100% —
    // the curve itself discourages dead waiting. Charging only runs while docked
    // in the base; entering the field freezes it (driven by BaseScreen).
    public const int FastChargeCeiling = 8000;                              // 80%
    private const double FastChargeRate = FastChargeCeiling / 180.0;        // 0→80% in 3 min
    private const double TrickleRate = (Capacity - FastChargeCeiling) / 120.0; // last 20% in 2 min

    // Sub-unit remainder so the slow trickle rate doesn't round down to zero
    // gain every frame.
    private double _chargeCarry;

    public bool IsFull => Charge >= Capacity;

    public void Recharge(double seconds)
    {
        if (Charge >= Capacity) { _chargeCarry = 0; return; }

        double rate = Charge < FastChargeCeiling ? FastChargeRate : TrickleRate;
        _chargeCarry += rate * seconds;

        int gained = (int)_chargeCarry;
        if (gained > 0)
        {
            Charge = Math.Min(Capacity, Charge + gained);
            _chargeCarry -= gained;
        }
        if (Charge >= Capacity) _chargeCarry = 0;
    }

    // ── hull integrity ────────────────────────────────────────────────────────
    // The rover drives anywhere — band 0 (sea, void, canyon floor) is passable.
    // The hit comes from the *fall*: dropping off solid ground onto band 0 slams
    // the chassis for a fixed chunk of integrity (the slope cost of the drop, and
    // of the climb back out, is charged on top). Driving along the bottom or
    // climbing out costs no integrity — only the fall does. A few falls and the
    // hull fails. Repairs happen at base later (DIAG); for now integrity only ever
    // falls. (Wandering the open ocean is fenced off by the survey bounds above,
    // not by hull damage.)
    public const int MaxIntegrity = 100;
    public const int ImpactDamage = 25;   // ~4 falls and the hull fails

    public int Integrity { get; private set; } = MaxIntegrity;
    public int IntegrityDisplay => Integrity;
    public bool IsDestroyed => Integrity <= 0;

    // The cell the rover is standing on is lethal low ground (band 0 / off-grid).
    public bool IsOnHazard => Terrain?.IsImpassable(RoverX, RoverY) ?? false;

    public void Damage(int amount) => Integrity = Math.Max(0, Integrity - amount);

    public bool MarksFull => _marks.Count >= MaxMarks;

    public bool HasMark(string name) => _marks.Any(m => m.Name == name);

    public void AddMark(string name) => _marks.Add(new MapMark(name, RoverX, RoverY));

    // Moves the rover, clamping at sector edges. Returns the number of units
    // actually travelled so the caller can report a cut-short move.
    public int Move(Direction dir, int steps)
    {
        var (dx, dy) = dir.Delta();
        int ox = RoverX, oy = RoverY;
        int nx = Math.Clamp(RoverX + dx * steps, MinX, MaxX);
        int ny = Math.Clamp(RoverY + dy * steps, MinY, MaxY);
        int moved = Math.Abs(nx - RoverX) + Math.Abs(ny - RoverY);
        RoverX = nx;
        RoverY = ny;

        // Leaving the base tile breaks the dock — the link to SUIRDC's servers
        // drops and the field excursion has begun.
        if (!IsAtBase) IsDocked = false;

        RecordTrail(ox, oy, nx, ny);
        DiscoverBaseHere();

        return moved;
    }

    // Adds the just-driven path to the trail: every node stepped through and the
    // lattice edge between each pair, so the renderer can recolour the track.
    private void RecordTrail(int fromX, int fromY, int toX, int toY)
    {
        _trail.Add((fromX, fromY));
        int dx = Math.Sign(toX - fromX), dy = Math.Sign(toY - fromY);
        int x = fromX, y = fromY;
        while (x != toX || y != toY)
        {
            int px = x, py = y;
            x += dx; y += dy;
            _trail.Add((x, y));
            if (dy == 0) _trailEdges.Add((Math.Min(px, x), y, true));   // horizontal: west node
            else _trailEdges.Add((x, Math.Min(py, y), false));  // vertical:   south node
        }
    }

    // Driving onto a base reveals it on the map (dim → lit). Activation / docking
    // at bases 2 and 3 is later gameplay; for now, reaching one is discovery.
    private void DiscoverBaseHere()
    {
        foreach (var b in Bases)
            if (b.X == RoverX && b.Y == RoverY) _discovered.Add(b.Label);
    }
}
