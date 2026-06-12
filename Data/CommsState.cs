namespace ExoProxy.Data;

public class CommsState
{
    public List<string>               ReadMessages     { get; set; } = [];
    public Dictionary<string, string> ChosenReplies    { get; set; } = [];
    public List<string>               UnlockedMessages { get; set; } = [];
}
