namespace ExoProxy.Data;

// Exact transfer outcome — the UI must never guess which error occurred.
public enum TransferResult
{
    Ok,
    SourceMissing,
    AlreadyStored,
    InsufficientSpace
}
