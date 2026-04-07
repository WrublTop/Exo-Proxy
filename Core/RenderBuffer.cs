using System.Text;

namespace ExoProxy.Core;

public sealed class RenderBuffer : IRenderBuffer
{
    private readonly char[,] _chars;
    private readonly string?[,] _colors;
    private readonly StringBuilder _sb = new();

    public int Width { get; }
    public int Height { get; }

    public RenderBuffer(int width, int height)
    {
        Width = width;
        Height = height;
        _chars = new char[width, height];
        _colors = new string?[width, height];
        Clear();
    }

    public void WriteAt(int x, int y, string text)
    {
        for (int i = 0; i < text.Length && x + i < Width; i++)
            _chars[x + i, y] = text[i];
    }

    public void WriteAt(int x, int y, string text, string ansiColor)
    {
        for (int i = 0; i < text.Length && x + i < Width; i++)
        {
            _chars[x + i, y] = text[i];
            _colors[x + i, y] = ansiColor;
        }
    }

    public void WriteRaw(int x, int y, string rawAnsiString)
    {
        _chars[x, y] = '\0';
        _colors[x, y] = rawAnsiString;
    }

    public void Clear()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                _chars[x, y] = ' ';
                _colors[x, y] = null;
            }
    }

    public string Flush()
    {
        _sb.Clear();
        _sb.Append(ExoCodes.MoveTo(1, 1));

        string? lastColor = null;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                string? color = _colors[x, y];
                if (color != lastColor)
                {
                    _sb.Append(color ?? ExoCodes.Reset);
                    lastColor = color;
                }
                _sb.Append(_chars[x, y] == '\0' ? "" : _chars[x, y]);
            }
        }

        return _sb.ToString();
    }
}