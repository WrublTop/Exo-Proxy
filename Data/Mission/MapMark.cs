namespace ExoProxy.Data.Mission;

// An operator-placed map annotation — the player's own breadcrumbs, not data
// from the planet. Drawn only while no sensor is active (sensor overlays
// would interfere with the readout). Persisted per-operator once mission
// saves exist.
public sealed record MapMark(string Name, int X, int Y);
