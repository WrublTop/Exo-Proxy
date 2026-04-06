namespace ExoProxy.Core;

public readonly record struct InputEvent(ConsoleKeyInfo Key, DateTimeOffset Timestamp);
