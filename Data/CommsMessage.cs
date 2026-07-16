namespace ExoProxy.Data;

public class CommsMessage
{
    public string Id            { get; set; } = "";
    public string Sender        { get; set; } = "";
    public string SenderDisplay { get; set; } = "";
    public string Subject       { get; set; } = "";
    public string Body          { get; set; } = "";
    public string Sol           { get; set; } = "";
    public bool   CanReply      { get; set; }
    public bool   Locked        { get; set; }

    // Optional gates beyond SOL: file held anywhere / synced to SUIRDC. Sticky
    // once shown (see CommsRepository.GetInbox).
    public string RequiresFile   { get; set; } = "";
    public string RequiresSynced { get; set; } = "";

    public List<ReplyOption> ReplyOptions { get; set; } = [];
}
