namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker;

public class SyncAlertSettings
{
    public int id { get; set; } = 1;
    public bool enable_sync_alerts { get; set; }

    public SyncAlertSettings ToDomain() => new()
    {
        id = id,
        enable_sync_alerts = enable_sync_alerts
    };
}
