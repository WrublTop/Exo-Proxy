namespace ExoProxy.Data.Mission;

// The mission terrain and everything on it: the rover, bases, deposits and the
// supply convoy. Coordinates are world units (one unit = one grid cell); base 1
// is the campaign start and the world extends into negative coords in every dir.
public sealed class MissionWorld
{
    // Survey rectangle — the playable bounds. Move clamps here; beyond lies the
    // lethal deep ocean / off-map void with nothing to find.
    public const int MinX = -180, MaxX = 180;
    public const int MinY = -160, MaxY = 160;

    // ── battery economy ───────────────────────────────────────────────────────
    // Charge is stored in fine "units" so per-cell cost carries sub-percent
    // precision; the player only sees the derived percent. Real range ~60 cells
    // blind, ~110-120 routing well (slope/scan/routing eat the rest).
    public const int Capacity = 10000;
    public const int BaseMoveCost = 65;

    // Survey bases on Cyra-6. Base 1 is home (spawn/dock/recharge); 2 and 3 start
    // hidden-dim and reveal when the rover reaches them (activation is later gameplay).
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

    // A latch, not just "on the base tile" — the rover can be at base yet undocked
    // (which raises the DOCK prompt). Starts docked.
    public bool IsDocked { get; private set; } = true;

    // Confirmed dock — advances the loop and clears the per-SOL convoy attempt.
    public void Dock()
    {
        IsDocked = true;
        ConvoyAttemptedThisSol = false;
    }

    public const int MaxMarks = 5;

    private readonly List<MapMark> _marks = [];
    public IReadOnlyList<MapMark> Marks => _marks;

    // The rover's path this session: visited nodes + the lattice edges between
    // them (the renderer recolours the grid along it). Session-only, not saved.
    // Edge key (x, y, H): horizontal stores its west node, vertical its south node.
    private readonly HashSet<(int X, int Y)> _trail = [];
    private readonly HashSet<(int X, int Y, bool H)> _trailEdges = [];
    public IReadOnlySet<(int X, int Y)> Trail => _trail;
    public IReadOnlySet<(int X, int Y, bool H)> TrailEdges => _trailEdges;

    // Autosave callback (set by MissionRepository), invoked at checkpoints only — never per-frame.
    public Action? Persist { get; set; }

    // Shared authored heightmap. Loaded once and attached here; never part of
    // the per-operator save (ToState/FromState don't touch it).
    public Terrain? Terrain { get; set; }

    // Authored survey-sector overlay (the SUIRDC grid). Shared world content
    // like Terrain — loaded once, never part of the per-operator save.
    public IReadOnlyList<Sector> Sectors { get; set; } = [];

    // Authored deposit placement list (Content/deposits.yaml). Shared content
    // like Terrain/Sectors — the source EnsureFieldEntities rolls positions
    // from. Held on the world so a DEV RESET can re-roll without the repository.
    public IReadOnlyList<DepositDefinition> DepositDefs { get; set; } = [];

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
        Deposits = _deposits.Select(d => new DepositState
        {
            Kind = d.Kind, X = d.X, Y = d.Y, FileId = d.FileId, Tier = d.Tier, Hidden = d.Hidden,
            SolStart = d.SolStart, SolEnd = d.SolEnd,
        }).ToList(),
        ExtractedFileIds = _extractedFileIds.ToList(),
        RevealedFileIds  = _revealedHidden.ToList(),
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

        foreach (var d in s.Deposits)
            w._deposits.Add(new Deposit(d.Kind, d.X, d.Y, d.FileId, d.Tier, d.Hidden, d.SolStart, d.SolEnd));
        foreach (var id in s.ExtractedFileIds)
            w._extractedFileIds.Add(id);
        foreach (var id in s.RevealedFileIds)
            w._revealedHidden.Add(id);

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
        ConvoyAttemptedThisSol = false;
        _extractedFileIds.Clear();
        _revealedHidden.Clear();
        _deposits.Clear();
        EnsureFieldEntities(new Random());   // re-roll fresh positions from the authored defs
        Persist?.Invoke();
    }

    // Slope surcharge on |Δlevel|² — convex, so gentle ground stays cheap while
    // steep climbs/drops cost far more. Main lever for how terrain shapes a route.
    public const int SlopeCostSquared = 18;

    // Surcharge per step per active sensor — scans stack, so running all three isn't free.
    public const int SensorStepDrain = 20;

    // Cost of the next cell in `dir`: base cost + squared slope + per-sensor drain.
    public int StepCost(Direction dir, int activeSensorCount)
    {
        int cost = BaseMoveCost;
        if (Terrain is { } t)
        {
            var (dx, dy) = dir.Delta();
            int d = Math.Abs(t.LevelAt(RoverX + dx, RoverY + dy) - t.LevelAt(RoverX, RoverY));
            cost += d * d * SlopeCostSquared;
        }
        cost += activeSensorCount * SensorStepDrain;
        return cost;
    }

    // Spends one cell's cost; returns false and leaves charge untouched if it can't cover a full step.
    public bool TrySpendForStep(Direction dir, int activeSensorCount)
    {
        int cost = StepCost(dir, activeSensorCount);
        if (Charge < cost) return false;
        Charge -= cost;
        return true;
    }

    // ── recharge (docked at base) ─────────────────────────────────────────────
    // Two-phase diminishing curve: fast to 80%, slow trickle for the last 20% (~5
    // min full), so leaving around 80% is rational. Only runs while docked.
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
    // Band 0 (sea/void/canyon floor) is passable; damage comes only from the fall
    // onto it, not driving along or climbing out. A few falls and the hull fails.
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

    // ── field entities: deposits + the automated supply convoy ───────────────
    // SolStart/SolEnd null for THERMAL (no window); set for EM (live that
    // inclusive range only, then gone — missing it is a real loss).
    public readonly record struct Deposit(string Kind, int X, int Y, string FileId, int Tier, bool Hidden, int? SolStart, int? SolEnd);

    private readonly List<Deposit> _deposits = [];
    public IReadOnlyList<Deposit> Deposits => _deposits;

    private readonly HashSet<string> _extractedFileIds = [];
    public bool IsDepositExtracted(string fileId) => _extractedFileIds.Contains(fileId);
    public void MarkExtracted(string fileId) => _extractedFileIds.Add(fileId);

    // Hidden deposits the operator has uncovered — one-way, persisted campaign.
    private readonly HashSet<string> _revealedHidden = [];
    public void RevealVein(string fileId) { if (_revealedHidden.Add(fileId)) Persist?.Invoke(); }

    // A hidden deposit still waiting to be uncovered (the convoy's reward).
    public bool TryGetHiddenVein(out Deposit vein)
    {
        foreach (var d in _deposits)
            if (d.Hidden && !_revealedHidden.Contains(d.FileId) && !IsDepositExtracted(d.FileId))
            { vein = d; return true; }
        vein = default;
        return false;
    }

    // Authored extra notes for a deposit's file (special proks); looked up at
    // extraction time so nothing long rides the save.
    public string DepositNote(string fileId) =>
        DepositDefs.FirstOrDefault(d => d.FileId == fileId)?.Note ?? "";

    // Live = not extracted, revealed if hidden, and (EM only) within [SolStart, SolEnd].
    public bool IsDepositLive(Deposit d, int currentSol) =>
        !IsDepositExtracted(d.FileId) &&
        (!d.Hidden || _revealedHidden.Contains(d.FileId)) &&
        (d.SolStart is null || (currentSol >= d.SolStart && currentSol <= d.SolEnd));

    // The deposit sitting exactly under the rover, if any and currently live.
    // Extraction requires standing on the tile — no fuzzy radius.
    public bool TryGetDepositAt(int x, int y, int currentSol, out Deposit deposit)
    {
        foreach (var d in _deposits)
            if (d.X == x && d.Y == y && IsDepositLive(d, currentSol)) { deposit = d; return true; }
        deposit = default;
        return false;
    }

    // Unmanned legacy SUIRDC runner lapping a fixed authored loop near base. The
    // track is content (same every campaign); only its position is dynamic (random
    // start each session), not persisted.
    public IReadOnlyList<(int X, int Y)> ConvoyPath { get; set; } = [];
    public int ConvoyX { get; private set; }
    public int ConvoyY { get; private set; }
    private double _convoyDist;                        // distance travelled along the loop
    private const double ConvoyUnitsPerSecond = 1.0;   // one tile/sec — reads the map scale

    public bool HasConvoy => ConvoyPath.Count >= 2;

    // One sync attempt per SOL: set when an attempt resolves (win OR lose),
    // cleared on Dock() so the next excursion offers a fresh try.
    public bool ConvoyAttemptedThisSol { get; private set; }
    public void MarkConvoyAttempted() => ConvoyAttemptedThisSol = true;

    private double ConvoyPerimeter()
    {
        double p = 0;
        for (int i = 0; i < ConvoyPath.Count; i++)
        {
            var a = ConvoyPath[i];
            var b = ConvoyPath[(i + 1) % ConvoyPath.Count];
            p += Math.Sqrt((double)(b.X - a.X) * (b.X - a.X) + (double)(b.Y - a.Y) * (b.Y - a.Y));
        }
        return Math.Max(1, p);
    }

    // Drops the runner at a random point on the loop — "respawns anywhere".
    public void SeedConvoy(Random rng)
    {
        if (!HasConvoy) return;
        _convoyDist = rng.NextDouble() * ConvoyPerimeter();
        UpdateConvoyPosition();
    }

    // Rolls deposit positions once per campaign from deposits.yaml, then frozen —
    // a restored save already has _deposits, so the guard makes this a no-op.
    // "fixed" places at exact coords; "area" rolls once within anchor±variation.
    public void EnsureFieldEntities(Random rng)
    {
        if (_deposits.Count > 0) return;   // already rolled — this campaign or a restored save

        foreach (var def in DepositDefs)
        {
            (int x, int y) = def.Placement == "area"
                ? FindValidTileInRect(rng, def.AnchorX, def.AnchorY, def.VariationX, def.VariationY)
                : (def.X, def.Y);
            _deposits.Add(new Deposit(def.Kind, x, y, def.FileId, def.Tier, def.Hidden, def.SolStart, def.SolEnd));
        }
    }

    // Random point in a rectangle centred on (cx,cy) spanning ±varX/±varY,
    // retried until it lands on a free tile. Falls back to the anchor itself
    // (an author's job to pick a sane one) if nothing turns up in range.
    private (int X, int Y) FindValidTileInRect(Random rng, int cx, int cy, int varX, int varY)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            int x = Math.Clamp(cx + rng.Next(-varX, varX + 1), MinX, MaxX);
            int y = Math.Clamp(cy + rng.Next(-varY, varY + 1), MinY, MaxY);
            if (IsFreePlacementTile(x, y)) return (x, y);
        }
        return (cx, cy);
    }

    // Passable ground, not on a base, not on another rolled deposit. (Fixed
    // placement skips this — the author owns those coords.)
    private bool IsFreePlacementTile(int x, int y)
    {
        if (Terrain is not null && Terrain.IsImpassable(x, y)) return false;
        foreach (var b in Bases) if (b.X == x && b.Y == y) return false;
        foreach (var d in _deposits) if (d.X == x && d.Y == y) return false;
        return true;
    }

    public void UpdateConvoy(double seconds)
    {
        if (!HasConvoy) return;
        double perim = ConvoyPerimeter();
        _convoyDist = (_convoyDist + ConvoyUnitsPerSecond * seconds) % perim;
        if (_convoyDist < 0) _convoyDist += perim;
        UpdateConvoyPosition();
    }

    private void UpdateConvoyPosition()
    {
        if (!HasConvoy) return;
        double d = _convoyDist;
        for (int i = 0; i < ConvoyPath.Count; i++)
        {
            var a = ConvoyPath[i];
            var b = ConvoyPath[(i + 1) % ConvoyPath.Count];
            double segLen = Math.Sqrt((double)(b.X - a.X) * (b.X - a.X) + (double)(b.Y - a.Y) * (b.Y - a.Y));
            if (d <= segLen || i == ConvoyPath.Count - 1)
            {
                double t = segLen > 0 ? d / segLen : 0;
                ConvoyX = (int)Math.Round(a.X + (b.X - a.X) * t);
                ConvoyY = (int)Math.Round(a.Y + (b.Y - a.Y) * t);
                return;
            }
            d -= segLen;
        }
    }

    public bool IsNearConvoy(int range = 2) =>
        HasConvoy && Math.Abs(RoverX - ConvoyX) <= range && Math.Abs(RoverY - ConvoyY) <= range;

    // "Hitch a ride": slide the rover up to maxTiles in (dx,dy) for free — stops
    // at the edge or before a band-0 pit. Returns tiles ridden.
    public int RideConvoy(int dx, int dy, int maxTiles)
    {
        if (dx == 0 && dy == 0) return 0;
        int moved = 0;
        for (int i = 0; i < maxTiles; i++)
        {
            int nx = RoverX + dx, ny = RoverY + dy;
            if (nx < MinX || nx > MaxX || ny < MinY || ny > MaxY) break;
            if (Terrain is { } t && t.IsImpassable(nx, ny)) break;
            int ox = RoverX, oy = RoverY;
            RoverX = nx; RoverY = ny;
            RecordTrail(ox, oy, nx, ny);
            moved++;
        }
        if (moved > 0)
        {
            if (!IsAtBase) IsDocked = false;
            DiscoverBaseHere();
            Persist?.Invoke();
        }
        return moved;
    }

    // Step direction the runner is currently travelling — used by the "hitch a
    // ride" outcome to slide the rover the same way.
    public (int DX, int DY) ConvoyHeading()
    {
        if (!HasConvoy) return (0, 0);
        double d = _convoyDist;
        for (int i = 0; i < ConvoyPath.Count; i++)
        {
            var a = ConvoyPath[i];
            var b = ConvoyPath[(i + 1) % ConvoyPath.Count];
            double segLen = Math.Sqrt((double)(b.X - a.X) * (b.X - a.X) + (double)(b.Y - a.Y) * (b.Y - a.Y));
            if (d <= segLen || i == ConvoyPath.Count - 1)
                return (Math.Sign(b.X - a.X), Math.Sign(b.Y - a.Y));
            d -= segLen;
        }
        return (0, 0);
    }
}
