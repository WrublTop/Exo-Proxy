namespace ExoProxy.Data;

public class OperatorAccount
{
    public string Login { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string RegisteredDate { get; set; } = "";
    public OperatorStatus Status { get; set; } = OperatorStatus.Active;
    public int Sol { get; set; }
    public int Funds { get; set; } = 1000;
}

public enum OperatorStatus
{
    Active,
    Terminated,
    Redacted
}
