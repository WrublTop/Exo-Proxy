namespace ExoProxy.Core;

public static class ExoColors
{
    // Phosphor — Operator, ciepły amber-orange
    public static readonly string PhosphorDim    = ExoCodes.Fg(107, 51, 0);    // #6B3300
    public static readonly string PhosphorText   = ExoCodes.Fg(224, 128, 32);  // #E08020
    public static readonly string PhosphorBright = ExoCodes.Fg(255, 184, 64);  // #FFB840

    // Proks — Maszyna, SUIRDC, zimny metal
    public static readonly string ProksDark      = ExoCodes.Fg(13, 27, 38);    // #0D1B26
    public static readonly string ProksBorder    = ExoCodes.Fg(30, 61, 82);    // #1E3D52
    public static readonly string ProksText      = ExoCodes.Fg(52, 95, 117);   // #345F75
    public static readonly string ProksPale      = ExoCodes.Fg(76, 125, 144);  // #4C7D90

    // Signal — SR-74, planeta, transmisje
    public static readonly string SignalDim      = ExoCodes.Fg(26, 80, 48);    // #1A5030
    public static readonly string SignalText     = ExoCodes.Fg(51, 255, 51);   // #33FF33
    public static readonly string SignalBright   = ExoCodes.Fg(102, 255, 136); // #66FF88

    // Fault — błędy, niebezpieczeństwo
    public static readonly string FaultText      = ExoCodes.Fg(204, 34, 0);    // #CC2200
    public static readonly string FaultBright    = ExoCodes.Fg(255, 68, 34);   // #FF4422

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

    public static readonly string[] FadePhosphor = GenerateFade((8, 4, 0), (224, 128, 32), 24);
    public static readonly string[] FadeSignal   = GenerateFade((0, 0, 0), (51, 255, 51), 12);
}
