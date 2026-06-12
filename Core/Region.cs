namespace ExoProxy.Core;

// A clipping, translating viewport over another IRenderBuffer. Lets a panel
// renderer use local (0,0)-based coordinates without knowing where on the
// screen it lives — and without being able to scribble over its neighbours.
// Region never owns the framebuffer; Flush() delegates to the parent.
public sealed class Region : IRenderBuffer
{
    private readonly IRenderBuffer _parent;
    private readonly int _ox, _oy;

    public int Width  { get; }
    public int Height { get; }

    public Region(IRenderBuffer parent, int x, int y, int width, int height)
    {
        _parent = parent;
        _ox = x;
        _oy = y;
        Width  = width;
        Height = height;
    }

    public void WriteAt(int x, int y, string text)
    {
        if (!ClipLine(ref x, ref text, y)) return;
        _parent.WriteAt(_ox + x, _oy + y, text);
    }

    public void WriteAt(int x, int y, string text, string ansiColor)
    {
        if (!ClipLine(ref x, ref text, y)) return;
        _parent.WriteAt(_ox + x, _oy + y, text, ansiColor);
    }

    public void WriteAt(int x, int y, char c)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        _parent.WriteAt(_ox + x, _oy + y, c);
    }

    public void WriteAt(int x, int y, char c, string ansiColor)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        _parent.WriteAt(_ox + x, _oy + y, c, ansiColor);
    }

    public void Clear()
    {
        string blank = new(' ', Width);
        for (int row = 0; row < Height; row++)
            _parent.WriteAt(_ox, _oy + row, blank);
    }

    public string Flush() => _parent.Flush();

    // Trims text so nothing renders outside this region. Returns false when
    // the write is entirely out of bounds.
    private bool ClipLine(ref int x, ref string text, int y)
    {
        if (y < 0 || y >= Height || x >= Width) return false;
        if (x < 0)
        {
            if (-x >= text.Length) return false;
            text = text[(-x)..];
            x = 0;
        }
        if (x + text.Length > Width)
            text = text[..(Width - x)];
        return text.Length > 0;
    }
}
