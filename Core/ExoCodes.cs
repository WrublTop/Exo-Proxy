namespace ExoProxy.Core;

public static class ExoCodes
{
    private const string Esc = "\e[";

    public static string Reset => "\e[0m";
    public static string ClearScreen => "\e[2J";
    public static string HideCursor => "\e[?25l";
    public static string ShowCursor => "\e[?25h";
    public static string MoveTo(int x, int y) => $"\e[{y};{x}H";
    public static string Fg(int r, int g, int b) => $"\e[38;2;{r};{g};{b}m";
    public static string Bg(int r, int g, int b) => $"\e[48;2;{r};{g};{b}m";
}
