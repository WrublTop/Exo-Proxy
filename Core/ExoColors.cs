namespace ExoProxy.Core;

public static class ExoColors
{
    public static readonly string ColorAmber = ExoCodes.Fg(255, 176, 0);
    public static readonly string ColorGreen = ExoCodes.Fg(51, 255, 51);

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

    public static readonly string[] FadeAmber = GenerateFade((12, 12, 12), (255, 176, 0), 24);
    public static readonly string[] FadeGreen = GenerateFade((0, 0, 0), (51, 255, 51), 12);
}

