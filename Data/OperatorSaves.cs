namespace ExoProxy.Data;

// Per-operator save files all live in SaveData/ as {kind}_{LOGIN}.yaml. This
// wipes them as a set — used on permadeath (the operator is gone, its run with
// it) and, for the transient DEV login, as a plain reset back to a fresh start.
//
// The filenames are built explicitly rather than globbed on "*_{LOGIN}.yaml" so
// one login can never sweep up another operator's files by coincidence.
public static class OperatorSaves
{
    private static readonly string[] Kinds =
        ["progress", "comms_state", "memory_state", "mission_state"];

    public static void Wipe(string login)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "SaveData");
        if (!Directory.Exists(dir)) return;

        foreach (var kind in Kinds)
        {
            string path = Path.Combine(dir, $"{kind}_{login.ToUpper()}.yaml");
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
