namespace ExoProxy.Data;

public class OperatorAccount
{
    public string Login { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string RegisteredDate { get; set; } = "";
    public OperatorStatus Status { get; set; } = OperatorStatus.Active;
}

public enum OperatorStatus
{
    Active,
    Terminated,
    Redacted
}
