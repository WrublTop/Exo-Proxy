using ExoProxy.Core;

namespace ExoProxy.Presentation;

// Shared chrome helpers used across sections and boot phases.
public static class Ui
{
    public static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..Math.Max(0, max - 1)] + "…";

    // Inline separator border: "├ LABEL ────┤". innerW excludes ├ ┤.
    public static void WriteTransmitBorder(IRenderBuffer buffer, int borderX, int y,
                                            int innerW, string label)
    {
        int dashCount = Math.Max(0, innerW - label.Length);
        buffer.WriteAt(borderX, y,
            "├" + label + new string('─', dashCount) + "┤",
            ExoColors.ProksBorder);
    }

    // Top border with left and right titles: "┌─ LEFT ──── RIGHT ─┐".
    public static void WriteDualTitleBorder(IRenderBuffer buffer, int x, int y, int w,
        string leftTitle, string rightTitle,
        string leftColor, string rightColor, string borderColor)
    {
        int inner    = w - 2;
        int leftDash = 1;
        int rightDash = rightTitle.Length > 0 ? 1 : 1;
        int midDash  = inner - leftDash - leftTitle.Length - rightTitle.Length - rightDash;
        if (midDash < 1) midDash = 1;

        int curX = x;
        buffer.WriteAt(curX, y, "┌" + new string('─', leftDash), borderColor);
        curX += 1 + leftDash;

        buffer.WriteAt(curX, y, leftTitle, leftColor);
        curX += leftTitle.Length;

        buffer.WriteAt(curX, y, new string('─', midDash), borderColor);
        curX += midDash;

        if (rightTitle.Length > 0)
        {
            buffer.WriteAt(curX, y, rightTitle, rightColor);
            curX += rightTitle.Length;
        }

        buffer.WriteAt(curX, y, new string('─', rightDash) + "┐", borderColor);
    }
}

// Reusable cursor/marker blink. Deliberately safe as a default struct:
// `private BlinkState _blink;` starts visible with a 500 ms interval —
// note that `new BlinkState()` skips any custom constructor, which is why
// visibility is stored inverted and the interval has a fallback.
public struct BlinkState
{
    private bool _hidden;            // default(false) → visible
    private TimeSpan? _timer;
    private readonly int _intervalMs;

    public BlinkState(int intervalMs) => _intervalMs = intervalMs;

    public readonly bool Visible => !_hidden;

    private readonly int IntervalMs => _intervalMs > 0 ? _intervalMs : 500;

    public void Update(TimeSpan now)
    {
        _timer ??= now;
        if (now - _timer.Value >= TimeSpan.FromMilliseconds(IntervalMs))
        {
            _hidden = !_hidden;
            _timer  = now;
        }
    }
}
