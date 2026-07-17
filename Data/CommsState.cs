namespace ExoProxy.Data;

public class CommsState
{
    public List<string>               ReadMessages     { get; set; } = [];
    public Dictionary<string, string> ChosenReplies    { get; set; } = [];
    public List<string>               UnlockedMessages { get; set; } = [];

    // Gated messages whose gate passed once — sticky, stays even if the file leaves.
    public List<string>               RevealedMessages { get; set; } = [];
}
