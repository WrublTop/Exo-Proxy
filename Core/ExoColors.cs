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
}
