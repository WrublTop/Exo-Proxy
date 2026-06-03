using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data;

public class OperatorRegistry
{
    private static readonly string _savePath = Path.Combine(AppContext.BaseDirectory, "SaveData", "operators.yaml");

    private static readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static readonly List<OperatorAccount> _defaults =
    [
        new OperatorAccount { Login = "E.VOSS",     PasswordHash = Hash("XXXX"), RegisteredDate = "03.02.1297 AJD", Status = OperatorStatus.Terminated },
        new OperatorAccount { Login = "M.CALLOWAY", PasswordHash = Hash("XXXX"), RegisteredDate = "17.02.1297 AJD", Status = OperatorStatus.Terminated },
    ];

    public List<OperatorAccount> Accounts { get; private set; } = [];

    public void Load()
    {
        if (!File.Exists(_savePath))
        {
            Accounts = [.. _defaults];
            Sort();
            return;
        }

        try
        {
            var yaml = File.ReadAllText(_savePath);
            Accounts = _deserializer.Deserialize<List<OperatorAccount>>(yaml) ?? [.. _defaults];
            Sort();
        }
        catch
        {
            Accounts = [.. _defaults];
            Sort();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        File.WriteAllText(_savePath, _serializer.Serialize(Accounts));
    }

    public bool TryLogin(string login, string password, out OperatorAccount? account)
    {
        account = Accounts.FirstOrDefault(a =>
            a.Login == login.ToUpper() &&
            a.PasswordHash == Hash(password));
        return account is not null;
    }

    public bool LoginExists(string login) =>
        Accounts.Any(a => a.Login == login.ToUpper());

    public OperatorAccount Register(string login, string password)
    {
        var account = new OperatorAccount
        {
            Login          = login.ToUpper(),
            PasswordHash   = Hash(password),
            RegisteredDate = DateTime.Now.ToString("dd.MM.1297 AJD"),
            Status         = OperatorStatus.Active
        };
        Accounts.Add(account);
        Sort();
        Save();
        return account;
    }

    private void Sort()
    {
        Accounts.Sort((a, b) => StatusOrder(a.Status).CompareTo(StatusOrder(b.Status)));
    }

    private static int StatusOrder(OperatorStatus s) => s switch
    {
        OperatorStatus.Active     => 0,
        OperatorStatus.Terminated => 1,
        OperatorStatus.Redacted   => 2,
        _                         => 3
    };

    private static string Hash(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
