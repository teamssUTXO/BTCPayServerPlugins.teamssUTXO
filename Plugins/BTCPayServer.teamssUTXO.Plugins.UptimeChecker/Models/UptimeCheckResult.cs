using System;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;

/// <summary>
/// Result of a single HTTP check run
/// </summary>
public class UptimeCheckResult
{
    public bool IsUp { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
}
