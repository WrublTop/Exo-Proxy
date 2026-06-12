namespace ExoProxy.Core;

public interface IRenderBuffer
{
    int Width { get; }
    int Height { get; }
    void WriteAt(int x, int y, string text);
    void WriteAt(int x, int y, string text, string ansiColor);
    void WriteAt(int x, int y, char c);
    void WriteAt(int x, int y, char c, string ansiColor);
    void Clear();
    string Flush();
}
