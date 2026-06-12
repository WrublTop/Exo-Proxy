namespace ExoProxy.Data;

public class MemoryStorageState
{
    // Each element: file ID or "" (free block)
    public List<string> RoverBlocks { get; set; } = [];
    public List<string> LocalBlocks { get; set; } = [];
    // Append-only list of file IDs synced to SUIRDC
    public List<string> SuirdcFiles { get; set; } = [];
}
