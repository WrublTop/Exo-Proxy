namespace ExoProxy.Data;

public class MemoryFile
{
    public string Id          { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Type        { get; set; } = "LOG";
    public int    Blocks      { get; set; } = 2;
    public string Sol         { get; set; } = "SOL 001";
    public string Description { get; set; } = "";
    public string Content     { get; set; } = "";

    public int SyncValue { get; set; }   // credits for syncing to SUIRDC (demo-scale); not spent yet
}
