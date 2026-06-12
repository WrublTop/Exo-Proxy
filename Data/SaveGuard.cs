namespace ExoProxy.Data;

// When a save file fails to parse, it is renamed to *.corrupt instead of
// being left in place — otherwise the next Save() would silently overwrite
// the player's progress with a fresh default state.
internal static class SaveGuard
{
    public static void Quarantine(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch
        {
            // Quarantine is best-effort: if the rename fails we still reset
            // state, we just lose the forensic copy.
        }
    }
}
