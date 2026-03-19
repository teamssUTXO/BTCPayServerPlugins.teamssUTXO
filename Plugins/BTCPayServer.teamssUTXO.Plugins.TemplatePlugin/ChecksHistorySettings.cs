namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker;

public class ChecksHistorySettings
{
    public int id { get; set; } = 1;
    public bool enable_history { get; set; }
    public int retention_days { get; set; } = 7;

    public ChecksHistorySettings ToDomain() => new()
    {
        id = id,
        enable_history = enable_history,
        retention_days = retention_days
    };
}
