namespace ExoProxy.Core;

// Runtime palette. Property names are stable — call sites reference
// ExoColors.PhosphorText etc. directly and never need to change.
// Apply(theme, brightness) rebuilds every color in place at runtime.
public static class ExoColors
{
    private readonly record struct Rgb(int R, int G, int B);

    // ── Base definitions (RGB), brightness-independent ───────────────────────

    // Phosphor — Operator voice. The ONLY family the THEME changes.
    // GREEN is a soft chartreuse phosphor, deliberately yellower than
    // SignalText (#33FF33) so the SR-74/planet voice stays visually
    // distinct from the operator voice.
    private static readonly Dictionary<string, (Rgb Dim, Rgb Text, Rgb Bright)> _phosphorThemes = new()
    {
        ["AMBER"] = (new Rgb(107, 51, 0),  new Rgb(224, 128, 32), new Rgb(255, 184, 64)),
        ["GREEN"] = (new Rgb(42, 74, 0),   new Rgb(127, 191, 0),  new Rgb(184, 255, 64)),
    };

    // Proks / Signal / Fault are semantic world-colors — they do not change
    // with theme (only with brightness).
    private static readonly Rgb _proksDark    = new(13, 27, 38);    // #0D1B26
    private static readonly Rgb _proksBorder  = new(30, 61, 82);    // #1E3D52
    private static readonly Rgb _proksText    = new(52, 95, 117);   // #345F75
    private static readonly Rgb _proksPale    = new(76, 125, 144);  // #4C7D90
    private static readonly Rgb _signalDim    = new(26, 80, 48);    // #1A5030
    private static readonly Rgb _signalText   = new(51, 255, 51);   // #33FF33
    private static readonly Rgb _signalBright = new(102, 255, 136); // #66FF88
    private static readonly Rgb _faultText    = new(204, 34, 0);    // #CC2200
    private static readonly Rgb _faultBright  = new(255, 68, 34);   // #FF4422

    // ICE/VOID elevation ramp (low → high) for the MASS overlay, plus the
    // sea/void colour for band-0. Cool blue-white, chosen to sit with the UI's
    // blue borders rather than fight them; deep near-black voids keep the map's
    // negative space so it doesn't wash to one flat blue.
    private static readonly Rgb[] _terrainRamp =
    [
        new(18, 26, 44), new(36, 78, 120), new(90, 150, 200),
        new(180, 215, 245), new(245, 250, 255),
    ];
    private static readonly Rgb _terrainSea   = new(10, 10, 14);    // #0A0A0E

    // ── Active palette (rebuilt by Apply) ────────────────────────────────────

    public static string PhosphorDim    { get; private set; } = "";
    public static string PhosphorText   { get; private set; } = "";
    public static string PhosphorBright { get; private set; } = "";

    public static string ProksDark      { get; private set; } = "";
    public static string ProksBorder    { get; private set; } = "";
    public static string ProksText      { get; private set; } = "";
    public static string ProksPale      { get; private set; } = "";

    public static string SignalDim      { get; private set; } = "";
    public static string SignalText     { get; private set; } = "";
    public static string SignalBright   { get; private set; } = "";

    public static string FaultText      { get; private set; } = "";
    public static string FaultBright    { get; private set; } = "";

    public static string[] FadePhosphor { get; private set; } = [];
    public static string[] FadeSignal   { get; private set; } = [];

    // ICE/VOID elevation ramp for the MASS topographic overlay: deep cool low
    // ground → bright cyan-white peaks, interpolated across _terrainRamp. A
    // scanner layer, distinct from the amber/green UI.
    public static string[] FadeTerrain  { get; private set; } = [];

    // The same ramp as RGB tuples, so the MASS overlay can emit it as a
    // background colour (a smooth filled field, not just tinted gridlines).
    public static (int R, int G, int B)[] FadeTerrainRgb { get; private set; } = [];

    // Sea / band-0 void colour under the MASS overlay — a near-black sitting
    // below the lowest land stop so coastlines read.
    public static string TerrainSea     { get; private set; } = "";

    static ExoColors() => Apply("AMBER", "NORMAL");

    // Rebuilds the whole palette. Theme switches the Phosphor family,
    // brightness scales every color. Unknown values fall back to defaults.
    public static void Apply(string theme, string brightness)
    {
        var ph = _phosphorThemes.TryGetValue(theme, out var t)
            ? t
            : _phosphorThemes["AMBER"];

        float k = brightness switch
        {
            "DIM"    => 0.75f,
            "BRIGHT" => 1.20f,
            _        => 1.00f,   // NORMAL or unknown
        };

        string C(Rgb c) => ExoCodes.Fg(Scale(c.R, k), Scale(c.G, k), Scale(c.B, k));
        (int r, int g, int b) S(Rgb c) => (Scale(c.R, k), Scale(c.G, k), Scale(c.B, k));

        PhosphorDim    = C(ph.Dim);
        PhosphorText   = C(ph.Text);
        PhosphorBright = C(ph.Bright);

        ProksDark      = C(_proksDark);
        ProksBorder    = C(_proksBorder);
        ProksText      = C(_proksText);
        ProksPale      = C(_proksPale);

        SignalDim      = C(_signalDim);
        SignalText     = C(_signalText);
        SignalBright   = C(_signalBright);

        FaultText      = C(_faultText);
        FaultBright    = C(_faultBright);

        FadePhosphor   = GenerateFade((8, 4, 0), S(ph.Text),     24);
        FadeSignal     = GenerateFade((0, 0, 0), S(_signalText), 12);
        var ramp = Array.ConvertAll(_terrainRamp, S);
        FadeTerrain    = GenerateFade(ramp, 24);
        FadeTerrainRgb = GenerateFadeRgb(ramp, 24);
        TerrainSea     = C(_terrainSea);
    }

    private static int Scale(int v, float k) => Math.Clamp((int)(v * k), 0, 255);

    public static string[] GenerateFade((int r, int g, int b) from, (int r, int g, int b) to, int steps)
    {
        var colors = new string[steps];
        for (int i = 0; i < steps; i++)
        {
            float t = steps == 1 ? 1f : (float)i / (steps - 1);
            int r = (int)(from.r + (to.r - from.r) * t);
            int g = (int)(from.g + (to.g - from.g) * t);
            int b = (int)(from.b + (to.b - from.b) * t);
            colors[i] = ExoCodes.Fg(r, g, b);
        }
        return colors;
    }

    public static (int R, int G, int B)[] GenerateFadeRgb((int r, int g, int b) from, (int r, int g, int b) to, int steps)
    {
        var c = new (int, int, int)[steps];
        for (int i = 0; i < steps; i++)
        {
            float t = steps == 1 ? 1f : (float)i / (steps - 1);
            c[i] = ((int)(from.r + (to.r - from.r) * t),
                    (int)(from.g + (to.g - from.g) * t),
                    (int)(from.b + (to.b - from.b) * t));
        }
        return c;
    }

    // Multi-stop variants: interpolate a smooth ramp across an ordered list of
    // colour stops (used for the ICE/VOID terrain ramp, which has 5 stops).
    public static string[] GenerateFade((int r, int g, int b)[] stops, int steps)
    {
        var rgb = GenerateFadeRgb(stops, steps);
        var colors = new string[steps];
        for (int i = 0; i < steps; i++)
            colors[i] = ExoCodes.Fg(rgb[i].R, rgb[i].G, rgb[i].B);
        return colors;
    }

    public static (int R, int G, int B)[] GenerateFadeRgb((int r, int g, int b)[] stops, int steps)
    {
        var c = new (int, int, int)[steps];
        if (stops.Length == 1)
        {
            for (int i = 0; i < steps; i++) c[i] = stops[0];
            return c;
        }
        for (int i = 0; i < steps; i++)
        {
            float t = steps == 1 ? 1f : (float)i / (steps - 1);
            float seg = t * (stops.Length - 1);
            int si = Math.Min((int)seg, stops.Length - 2);
            float f = seg - si;
            var a = stops[si];
            var b = stops[si + 1];
            c[i] = ((int)(a.r + (b.r - a.r) * f),
                    (int)(a.g + (b.g - a.g) * f),
                    (int)(a.b + (b.b - a.b) * f));
        }
        return c;
    }
}
