using System;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker;

public class CounterPluginSettings
{
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool Enabled { get; set; }
    public string? Password { get; set; }
    public string? HtmlTemplate { get; set; }
    public string AdminUserId { get; set; }
    public bool IncludeArchived { get; set; }
    public bool IncludeTransactionVolume { get; set; }
    public string ExcludedStoreIds { get; set; }
    public string ExtraTransactions { get; set; }
}
