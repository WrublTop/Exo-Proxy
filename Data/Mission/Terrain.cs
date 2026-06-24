namespace ExoProxy.Data.Mission;

// Authored heightmap for the mission world. Loaded from a base-36 grid (one
// char per node, 0-9A-Z = level 0..35); level 0 is "too low" — impassable
// lowland / sea / erosion canyon. The grid is placed centred on the base, and
// anything outside it reads as level 0, so the rover is hemmed into the
// surveyed region without a hard wall in sight.
//
// Shared world content (same for every operator) — NOT part of the per-operator
// save, so it hangs off MissionWorld but never rides through MissionState.
public sealed class Terrain
{
    public const int MaxLevel = 30;          // matches the converter
    private const int UnitsPerLevel = 100;   // 1 level ≈ 100 m of displayed altitude

    private const string B36 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private readonly byte[,] _level;         // [col, row]
    public int Width  { get; }
    public int Height { get; }

    // ~95th-percentile land level. The display ramp maps 0..this, so the
    // populated height band fills the colour ramp instead of a few rare peaks
    // owning the bright half. Computed from the data, so a re-authored heightmap
    // self-tunes — no hand-set ceiling per map.
    public double DisplayCeiling { get; }

    // World coordinate of column 0 / row 0. Row 0 is the northern edge, so world
    // Y decreases as the row index grows.
    private readonly int _originX;
    private readonly int _originYTop;

    private Terrain(byte[,] level, int width, int height)
    {
        _level  = level;
        Width   = width;
        Height  = height;
        _originX    = -width / 2;       // centred on the base at (0,0)
        _originYTop =  height / 2;

        var hist = new int[MaxLevel + 1];
        int landCount = 0;
        foreach (var v in _level)
            if (v > 0) { hist[v]++; landCount++; }
        if (landCount == 0)
            DisplayCeiling = MaxLevel / 2.0;
        else
        {
            int acc = 0, ceil = 1;
            for (int l = 1; l <= MaxLevel; l++)
            {
                acc += hist[l];
                ceil = l;
                if (acc >= landCount * 0.95) break;
            }
            DisplayCeiling = Math.Max(4, ceil);
        }
    }

    public static Terrain? Load(string path)
    {
        if (!File.Exists(path)) return null;

        var lines = File.ReadAllLines(path);
        var rows  = lines.Where(l => l.Length > 0).ToArray();
        if (rows.Length == 0) return null;

        int h = rows.Length;
        int w = rows.Max(r => r.Length);
        var grid = new byte[w, h];
        for (int r = 0; r < h; r++)
            for (int c = 0; c < rows[r].Length; c++)
            {
                int v = B36.IndexOf(rows[r][c]);
                grid[c, r] = (byte)(v < 0 ? 0 : v);
            }
        return new Terrain(grid, w, h);
    }

    // Height level 0..MaxLevel at a world cell. Outside the surveyed grid → 0.
    public int LevelAt(int worldX, int worldY)
    {
        int c = worldX - _originX;
        int r = _originYTop - worldY;
        if (c < 0 || c >= Width || r < 0 || r >= Height) return 0;
        return _level[c, r];
    }

    public bool IsImpassable(int worldX, int worldY) => LevelAt(worldX, worldY) == 0;

    // Displayed altitude: the coarse level scaled to metres plus a deterministic
    // per-cell jitter, so a flat plain doesn't read as a wall of identical
    // numbers. Jitter stays within one level step, so ordering never inverts.
    public int DisplayAltitude(int worldX, int worldY)
    {
        int lv = LevelAt(worldX, worldY);
        int jitter = Hash(worldX, worldY) % UnitsPerLevel;
        return lv * UnitsPerLevel + jitter;
    }

    private static int Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }
}
