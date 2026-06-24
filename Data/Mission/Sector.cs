namespace ExoProxy.Data.Mission;

// One authored survey sector — a labelled, coloured RECTILINEAR POLYGON the SUIRDC
// grid overlays on the operator's map. Shared world content (the same for every
// operator), loaded from Content/sectors.yaml; never part of the per-operator
// save. Quests reference sectors by label ("survey sector D").
//
// Mutable auto-properties because YamlDotNet deserialises straight into them.
// Points is a closed polygon, [[x,y], ...] in world units; the edges run
// horizontal/vertical (a staircase that hugs the terrain). The last point joins
// back to the first automatically.
public sealed class Sector
{
    public string Label { get; set; } = "";
    public string Color { get; set; } = "255,255,255";   // "r,g,b" — theme-independent
    public List<List<int>> Points { get; set; } = [];

    // Parsed vertices, cached after the first access (YAML fills Points first).
    private (int X, int Y)[]? _verts;
    public (int X, int Y)[] Vertices =>
        _verts ??= Points.Where(p => p.Count >= 2).Select(p => (p[0], p[1])).ToArray();

    // Axis-aligned bounds, for label placement and quick viewport culling.
    private (int MinX, int MaxX, int MinY, int MaxY)? _bounds;
    private (int MinX, int MaxX, int MinY, int MaxY) Bounds
    {
        get
        {
            if (_bounds is { } b) return b;
            var v = Vertices;
            if (v.Length == 0) return (_bounds = (0, 0, 0, 0)).Value;
            int mnx = v[0].X, mxx = v[0].X, mny = v[0].Y, mxy = v[0].Y;
            foreach (var p in v)
            {
                mnx = Math.Min(mnx, p.X); mxx = Math.Max(mxx, p.X);
                mny = Math.Min(mny, p.Y); mxy = Math.Max(mxy, p.Y);
            }
            _bounds = (mnx, mxx, mny, mxy);
            return _bounds.Value;
        }
    }

    public int MinX => Bounds.MinX;
    public int MaxX => Bounds.MaxX;
    public int MinY => Bounds.MinY;
    public int MaxY => Bounds.MaxY;

    // Even-odd ray cast. A point exactly on an edge may read either way — fine for
    // a "which sector am I in" readout.
    public bool Contains(int x, int y)
    {
        var v = Vertices;
        if (v.Length < 3) return false;
        bool inside = false;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
        {
            if ((v[i].Y > y) != (v[j].Y > y))
            {
                double xc = (double)(v[j].X - v[i].X) * (y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X;
                if (x < xc) inside = !inside;
            }
        }
        return inside;
    }

    // Parsed once from the "r,g,b" string. A malformed value falls back to white
    // so a typo in the content file can't crash the render.
    private (int R, int G, int B)? _rgb;
    public (int R, int G, int B) Rgb
    {
        get
        {
            if (_rgb is { } v) return v;
            var p = Color.Split(',');
            if (p.Length == 3
                && int.TryParse(p[0].Trim(), out int r)
                && int.TryParse(p[1].Trim(), out int g)
                && int.TryParse(p[2].Trim(), out int b))
                _rgb = (r, g, b);
            else
                _rgb = (255, 255, 255);
            return _rgb.Value;
        }
    }
}
