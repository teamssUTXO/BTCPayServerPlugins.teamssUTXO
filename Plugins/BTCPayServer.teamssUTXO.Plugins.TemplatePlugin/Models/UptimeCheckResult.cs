using System;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;

/// <summary>
/// Result of a single HTTP check run
/// </summary>
public class UptimeCheckResult
{
    public string CheckId { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool IsUp { get; set; }

    public bool IsStateChange { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;

    public long CheckDurationMs  { get; set; }
}
