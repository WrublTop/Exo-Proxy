using ExoProxy.Core;
using ExoProxy.Data.Mission;

namespace ExoProxy.Presentation.Screens.Base.Sections.Mission;

// Draws the terrain viewport: coordinate rulers, the grid lattice, the base
// marker and the rover. One world unit = one grid cell; the rover sits on grid
// intersections and moves cell-by-cell. The camera follows the rover and clamps
// at world edges, so near a sector border the rover drifts off-centre instead of
// the map showing void.
//
// Cell size is passed in: 1:1 is the navigation scale, smaller cells zoom out so
// more of the heightmap is visible at once. With MASS online the lattice is
// tinted by elevation interpolated between nodes, so it reads as a topographic
// field at any zoom.
public static class MapRenderer
{
    private const int RulerW = 6;   // left ruler: signed Y label + gap
    private const int RulerH = 1;   // top ruler: X labels

    public static void Render(IRenderBuffer map, MissionWorld world, bool massActive, int cellW, int cellH)
    {
        int plotW = map.Width  - RulerW;
        int plotH = map.Height - RulerH;

        var terrain = world.Terrain;
        bool paint = massActive && terrain is not null;
        double ceiling = terrain?.DisplayCeiling ?? 16;

        // Camera in char-space. Unit x sits at char x*cellW; world Y grows north
        // (up) while screen rows grow down, so unit y sits at char -y*cellH.
        int roverCx = world.RoverX * cellW;
        int roverCy = -world.RoverY * cellH;
        int camX = Math.Clamp(roverCx - plotW / 2,
            MissionWorld.MinX * cellW, MissionWorld.MaxX * cellW - plotW + 1);
        int camY = Math.Clamp(roverCy - plotH / 2,
            -MissionWorld.MaxY * cellH, -MissionWorld.MinY * cellH - plotH + 1);

        int firstV = Mod(-camX, cellW);
        int firstH = Mod(-camY, cellH);

        // ── rulers — thin them out so labels never collide at high zoom ───────
        int labelEveryX = Math.Max(1, (int)Math.Ceiling(5.0 / cellW));
        int labelEveryY = Math.Max(1, (int)Math.Ceiling(2.0 / cellH));
        for (int sx = firstV; sx < plotW; sx += cellW)
        {
            int wx = (camX + sx) / cellW;
            if (Mod(wx, labelEveryX) != 0) continue;
            string label = wx.ToString();
            map.WriteAt(Math.Max(1, RulerW + sx - label.Length / 2), 0, label, ExoColors.ProksDark);
        }
        for (int sy = firstH; sy < plotH; sy += cellH)
        {
            int wy = -(camY + sy) / cellH;
            if (Mod(wy, labelEveryY) != 0) continue;
            map.WriteAt(0, RulerH + sy, wy.ToString().PadLeft(RulerW - 1), ExoColors.ProksDark);
        }

        // ── grid lattice — subtle background, or elevation tint under MASS, with
        //    the rover's trail recolouring the segments it drove over ───────────
        var trail      = world.Trail;
        var trailEdges = world.TrailEdges;
        for (int sy = 0; sy < plotH; sy++)
        {
            if (Mod(camY + sy, cellH) == 0)
            {
                // Horizontal gridline. World Y constant; colour interpolates
                // west→east between node heights.
                int wy = -(camY + sy) / cellH;
                for (int sx = 0; sx < plotW; sx++)
                {
                    int gx  = camX + sx;
                    int mod = Mod(gx, cellW);
                    int wx  = (gx - mod) / cellW;                         // node to the west
                    char ch = mod == 0 ? '┼' : '─';
                    string col = ExoColors.ProksDark;
                    if (paint)
                    {
                        double f = mod / (double)cellW;
                        col = TerrainColor(Lerp(terrain!.LevelAt(wx, wy), terrain.LevelAt(wx + 1, wy), f), ceiling);
                    }
                    // Trail: a crossed horizontal segment (─) just recolours; a
                    // node picks its glyph from the path edges that meet there, so
                    // a straight pass-through reads ─ and a turn reads ┌┐└┘ instead
                    // of every node on the track stamping a bare ┼.
                    bool onTrail = mod == 0
                        ? trail.Contains((wx, wy))
                        : trailEdges.Contains((wx, wy, true));
                    if (onTrail)
                    {
                        col = ExoColors.PhosphorDim;
                        if (mod == 0)
                        {
                            int m = 0;
                            if (trailEdges.Contains((wx,     wy,     true)))  m |= E;
                            if (trailEdges.Contains((wx - 1, wy,     true)))  m |= W;
                            if (trailEdges.Contains((wx,     wy,     false))) m |= N;
                            if (trailEdges.Contains((wx,     wy - 1, false))) m |= S;
                            if (m != 0) ch = LineGlyph(m);   // lone start node keeps ┼
                        }
                    }
                    map.WriteAt(RulerW + sx, RulerH + sy, ch, col);
                }
            }
            else
            {
                // Vertical gridline segments. World X constant; colour
                // interpolates north→south between the two node heights.
                for (int sx = firstV; sx < plotW; sx += cellW)
                {
                    int gx    = camX + sx;
                    int wx    = (gx - Mod(gx, cellW)) / cellW;
                    int gy    = camY + sy;
                    int off   = Mod(gy, cellH);
                    int wyTop = -(gy - off) / cellH;                      // node to the north
                    string col = ExoColors.ProksDark;
                    if (paint)
                    {
                        double g = off / (double)cellH;
                        col = TerrainColor(Lerp(terrain!.LevelAt(wx, wyTop), terrain.LevelAt(wx, wyTop - 1), g), ceiling);
                    }
                    // Trail: the vertical segment (south node = wyTop-1) recolours.
                    if (trailEdges.Contains((wx, wyTop - 1, false)))
                        col = ExoColors.PhosphorDim;
                    map.WriteAt(RulerW + sx, RulerH + sy, '│', col);
                }
            }
        }

        // Survey-sector overlay — always on, independent of the active sensor,
        // so the operator keeps a labelled reference frame even when driving
        // blind. Drawn over the terrain, under the rover/base markers.
        DrawSectors(map, world.Sectors, camX, camY, plotW, plotH, cellW, cellH);

        // Markers last so they sit on top. Operator marks hide under an active
        // sensor overlay so they don't fight the readout.
        if (!massActive)
            foreach (var m in world.Marks)
                DrawMarker(map, m.X, m.Y, camX, camY, plotW, plotH, cellW, cellH, "[" + m.Name + "]", ExoColors.PhosphorText);

        // Survey bases — base 1 is home; 2 and 3 read dim until the rover reaches
        // them. The rover (the operator's own avatar) takes the phosphor colour.
        foreach (var b in MissionWorld.Bases)
            DrawMarker(map, b.X, b.Y, camX, camY, plotW, plotH, cellW, cellH,
                "[BASE " + b.Label + "]",
                world.IsBaseDiscovered(b.Label) ? ExoColors.ProksPale : ExoColors.ProksBorder);

        DrawMarker(map, world.RoverX, world.RoverY, camX, camY, plotW, plotH, cellW, cellH, "[SR]", ExoColors.PhosphorBright);
    }

    // DEV survey overview: the whole authored heightmap squeezed into the
    // viewport so the terrain can be judged at a glance. One character samples a
    // block of cells; both ink and colour ride elevation (sea/void reads as
    // blank). No camera, always painted regardless of the sensor — which is why
    // it is gated to dev mode. 'B' / 'R' mark the base and the rover.
    private const string OverviewRamp = " .:-=+*#";

    public static void RenderOverview(IRenderBuffer map, MissionWorld world)
    {
        var terrain = world.Terrain;
        if (terrain is null) return;
        int plotW = map.Width  - RulerW;
        int plotH = map.Height - RulerH;
        if (plotW <= 0 || plotH <= 0) return;
        double ceiling = terrain.DisplayCeiling;

        for (int sy = 0; sy < plotH; sy++)
            for (int sx = 0; sx < plotW; sx++)
            {
                int gc = (int)((sx + 0.5) / plotW * terrain.Width);
                int gr = (int)((sy + 0.5) / plotH * terrain.Height);
                int lv = terrain.LevelAt(gc - terrain.Width / 2, terrain.Height / 2 - gr);
                if (lv <= 0) continue;                       // sea / void = blank
                double t = Math.Pow(Math.Clamp(lv / ceiling, 0, 1), PaletteGamma);
                char ch = OverviewRamp[Math.Clamp((int)(t * (OverviewRamp.Length - 1)), 0, OverviewRamp.Length - 1)];
                map.WriteAt(RulerW + sx, RulerH + sy, ch, TerrainColor(lv, ceiling));
            }

        OverviewMarker(map, world.BaseX,  world.BaseY,  terrain, plotW, plotH, "B", ExoColors.ProksPale);
        OverviewMarker(map, world.RoverX, world.RoverY, terrain, plotW, plotH, "R", ExoColors.SignalBright);
    }

    private static void OverviewMarker(IRenderBuffer map, int wx, int wy, Terrain t,
                                       int plotW, int plotH, string m, string col)
    {
        int gc = wx + t.Width / 2, gr = t.Height / 2 - wy;
        if (gc < 0 || gc >= t.Width || gr < 0 || gr >= t.Height) return;
        int sx = (int)((gc + 0.5) / t.Width * plotW);
        int sy = (int)((gr + 0.5) / t.Height * plotH);
        if (sx >= 0 && sx < plotW && sy >= 0 && sy < plotH)
            map.WriteAt(RulerW + sx, RulerH + sy, m, col);
    }

    // Draws the authored sectors as coloured rectilinear outlines. Every edge of
    // every sector is accumulated into one per-cell direction mask (clipped to the
    // viewport), so where two sectors share or cross a boundary the cell resolves
    // to a real ┬ ┤ ┼ junction rather than one straight run overwriting another.
    // Vertices need no special case — they fall out as the OR of their two edges.
    // Colour is last-writer-wins; a shared border simply takes the later sector's
    // hue. Labels pin just inside each NW bbox corner and draw on top of the lines.
    private static void DrawSectors(IRenderBuffer map, IReadOnlyList<Sector> sectors,
                                    int camX, int camY, int plotW, int plotH, int cellW, int cellH)
    {
        var cells  = new Dictionary<(int X, int Y), (int Mask, string Col)>();
        var labels = new List<(int X, int Y, string Text, string Col)>();

        void Mark(int x, int y, int bit, string col)
        {
            if (x < 0 || x >= plotW || y < 0 || y >= plotH) return;
            int mask = cells.TryGetValue((x, y), out var prev) ? prev.Mask : 0;
            cells[(x, y)] = (mask | bit, col);
        }

        foreach (var s in sectors)
        {
            var v = s.Vertices;
            if (v.Length < 2) continue;

            int left   =  s.MinX * cellW - camX;
            int right  =  s.MaxX * cellW - camX;
            int top    = -s.MaxY * cellH - camY;       // north edge (max Y is up)
            int bottom = -s.MinY * cellH - camY;
            if (right < 0 || left >= plotW || bottom < 0 || top >= plotH) continue;

            var (r, g, b) = s.Rgb;
            string col = ExoCodes.Fg(r, g, b);

            // Each edge is axis-aligned: a horizontal run adds an E stub to a cell
            // and a W stub to its eastern neighbour, a vertical run adds S then N.
            // The OR at each shared vertex is exactly the junction glyph we want.
            for (int i = 0; i < v.Length; i++)
            {
                var a = v[i];
                var c = v[(i + 1) % v.Length];
                int ax = a.X * cellW - camX, ay = -a.Y * cellH - camY;
                int cx = c.X * cellW - camX, cy = -c.Y * cellH - camY;
                if (ay == cy)
                    for (int x = Math.Min(ax, cx); x < Math.Max(ax, cx); x++)
                    { Mark(x, ay, E, col); Mark(x + 1, ay, W, col); }
                else
                    for (int y = Math.Min(ay, cy); y < Math.Max(ay, cy); y++)
                    { Mark(ax, y, S, col); Mark(ax, y + 1, N, col); }
            }

            // Label just inside the NW bbox corner, when that corner is visible.
            int lx = left + 1, ly = top + 1;
            if (s.Label.Length > 0 && lx >= 0 && lx < plotW && ly >= 0 && ly < plotH)
            {
                int room = plotW - lx;
                labels.Add((lx, ly, s.Label.Length > room ? s.Label[..room] : s.Label, col));
            }
        }

        // A sector border draws as a clean straight run: a horizontal edge stays
        // ─ and a vertical edge stays │ the whole way, taking a corner or tee glyph
        // only where the sector geometry itself turns or branches. Where the border
        // crosses the dim background lattice it simply overwrites it — a continuous
        // coloured boundary reads better than one studded with ┼ at every crossing.
        foreach (var (pos, cell) in cells)
            map.WriteAt(RulerW + pos.X, RulerH + pos.Y, LineGlyph(cell.Mask), cell.Col);

        foreach (var (x, y, text, col) in labels)
            map.WriteAt(RulerW + x, RulerH + y, text, col);
    }

    // Direction bits and the box-drawing glyph each combination resolves to. A
    // "line layer" (the rover's trail, the sector outlines) ORs a bit into a cell
    // for every neighbour the line continues toward; LineGlyph then turns that
    // mask into the matching ─ │ ┌ ┐ └ ┘ ├ ┤ ┬ ┴ ┼, so straight runs, corners,
    // tees and crossings all merge instead of stamping a bare ┼. Index by the
    // bitmask N=1 E=2 S=4 W=8; mask 0 (no neighbours) reads blank.
    private const int N = 1, E = 2, S = 4, W = 8;
    private const string LineGlyphs = " │─└││┌├─┘─┴┐┤┬┼";
    private static char LineGlyph(int mask) => LineGlyphs[mask & 15];

    private static void DrawMarker(IRenderBuffer map, int wx, int wy, int camX, int camY,
                                   int plotW, int plotH, int cellW, int cellH, string marker, string color)
    {
        int sx = wx * cellW - camX;
        int sy = -wy * cellH - camY;
        if (sx < 0 || sx >= plotW || sy < 0 || sy >= plotH) return;
        map.WriteAt(RulerW + sx - marker.Length / 2, RulerH + sy, marker, color);
    }

    private static double Lerp(int a, int b, double t) => a + (b - a) * t;

    // The display ceiling rides the terrain's own ~p95 land level
    // (Terrain.DisplayCeiling), so the populated band fills the ramp on any
    // re-authored heightmap without a hand-set constant. Gamma < 1 lifts the
    // low-mids so the common mid-terrain gridlines stay visible across the
    // ICE/VOID ramp (matching the palette lab). Display-only: raw levels still
    // drive cost and impassability.
    private const double PaletteGamma = 0.82;

    // Maps a (possibly fractional) height level onto the elevation ramp, given
    // the per-terrain display ceiling. Band-0 reads as the near-black sea.
    private static string TerrainColor(double level, double ceiling)
    {
        if (level <= 0) return ExoColors.TerrainSea;
        var fade = ExoColors.FadeTerrain;
        if (fade.Length == 0) return ExoColors.ProksDark;
        double t = Math.Clamp(level / ceiling, 0, 1);
        t = Math.Pow(t, PaletteGamma);
        int idx = Math.Clamp((int)Math.Round(t * (fade.Length - 1)), 0, fade.Length - 1);
        return fade[idx];
    }

    // Like %, but always returns a value in [0, m) — C# % is negative for
    // negative operands, which would put gridlines off by a cell west of 0.
    private static int Mod(int v, int m) => ((v % m) + m) % m;
}
