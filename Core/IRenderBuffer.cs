namespace ExoProxy.Core;

public interface IRenderBuffer
{
    int Width { get; }
    int Height { get; }
    void WriteAt(int x, int y, string text);
    void WriteAt(int x, int y, string text, string ansiColor);
    void WriteRaw(int x, int y, string rawAnsiString);
    void Clear();
    string Flush();
}