namespace ExoProxy.Data.Mission;

public enum Direction { North, South, East, West }

public static class DirectionExtensions
{
    // Map-style axes: north increases Y, east increases X — like latitude
    // and longitude. The renderer alone knows that screen rows grow downward.
    public static (int Dx, int Dy) Delta(this Direction d) => d switch
    {
        Direction.North => (0, 1),
        Direction.South => (0, -1),
        Direction.East  => (1, 0),
        _               => (-1, 0),
    };

    public static char Letter(this Direction d) => d switch
    {
        Direction.North => 'N',
        Direction.South => 'S',
        Direction.East  => 'E',
        _               => 'W',
    };
}
