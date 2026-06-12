using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data;

public class CommsRepository
{
    private static readonly string _contentPath =
        Path.Combine(AppContext.BaseDirectory, "Content", "messages.yaml");

    private string _statePath = "";

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private List<CommsMessage> _all   = [];
    private CommsState         _state = new();

    // Set when a file existed but could not be parsed.
    public string? LoadWarning { get; private set; }

    public void Load(string operatorLogin)
    {
        _statePath = Path.Combine(AppContext.BaseDirectory, "SaveData",
            $"comms_state_{operatorLogin.ToUpper()}.yaml");

        if (File.Exists(_contentPath))
        {
            try
            {
                _all = _deserializer.Deserialize<List<CommsMessage>>(
                    File.ReadAllText(_contentPath)) ?? [];
            }
            catch
            {
                // Content file is shipped data, not a save — never quarantined.
                _all = [];
                LoadWarning = "COMMS ARCHIVE UNREADABLE — CONTACT SUIRDC MAINTENANCE";
            }
        }

        if (File.Exists(_statePath))
        {
            try
            {
                _state = _deserializer.Deserialize<CommsState>(
                    File.ReadAllText(_statePath)) ?? new();
            }
            catch
            {
                SaveGuard.Quarantine(_statePath);
                _state = new();
                LoadWarning = "COMMS CHECKSUM FAILURE — STATE RESTORED FROM DEFAULTS";
            }
        }
    }

    // Returns root messages visible in the inbox for the given SOL.
    public List<CommsMessage> GetInbox(int currentSol) =>
        _all.Where(m => !m.Locked && ParseSol(m.Sol) <= currentSol).ToList();

    private static int ParseSol(string sol) =>
        int.TryParse(sol.Replace("SOL", "").Trim(), out int n) ? n : 0;

    public bool IsRead(string id) =>
        _state.ReadMessages.Contains(id);

    // A root message shows as unread if any message in its thread chain is unread.
    public bool HasUnreadInThread(string rootId)
    {
        var (chain, _, _) = BuildThread(rootId);
        return chain.Any(m => !_state.ReadMessages.Contains(m.Id));
    }

    public void MarkThreadRead(string rootId)
    {
        var (chain, _, _) = BuildThread(rootId);
        bool changed = false;
        foreach (var m in chain)
        {
            if (!_state.ReadMessages.Contains(m.Id))
            {
                _state.ReadMessages.Add(m.Id);
                changed = true;
            }
        }
        if (changed) Save();
    }

    public string? GetChosenReply(string messageId) =>
        _state.ChosenReplies.TryGetValue(messageId, out var r) ? r : null;

    public void CommitReply(string messageId, string replyOptionId, string unlocks)
    {
        _state.ChosenReplies[messageId] = replyOptionId;
        if (!string.IsNullOrEmpty(unlocks) && !_state.UnlockedMessages.Contains(unlocks))
            _state.UnlockedMessages.Add(unlocks);
        Save();
    }

    public CommsMessage? GetMessage(string id) =>
        _all.FirstOrDefault(m => m.Id == id);

    // Follows the reply chain from rootId.
    // Returns: the ordered chain of messages, any pending reply options, and
    // the ID of the message those options belong to.
    public (List<CommsMessage> Chain,
            List<ReplyOption>? PendingOptions,
            string? PendingMessageId) BuildThread(string rootId)
    {
        var chain   = new List<CommsMessage>();
        var current = GetMessage(rootId);

        while (current != null)
        {
            chain.Add(current);

            if (!_state.ChosenReplies.TryGetValue(current.Id, out var chosenId))
            {
                var opts = current.CanReply && current.ReplyOptions.Count > 0
                    ? current.ReplyOptions
                    : null;
                return (chain, opts, opts != null ? current.Id : null);
            }

            var chosen = current.ReplyOptions.FirstOrDefault(r => r.Id == chosenId);
            if (chosen == null || string.IsNullOrEmpty(chosen.Unlocks)) break;

            current = GetMessage(chosen.Unlocks);
        }

        return (chain, null, null);
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, _serializer.Serialize(_state));
    }
}
