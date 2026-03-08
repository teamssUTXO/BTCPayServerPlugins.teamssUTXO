using System;
using System.Collections.Generic;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;

/// <summary>
/// Represents a configured uptime check
/// </summary>
public class UptimeCheck
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Url { get; set; } = string.Empty;

    public int IntervalMinutes { get; set; } = 5;

    public bool IsEnabled { get; set; } = true;

    public List<string> NotificationEmails { get; set; } = new();

    public UptimeCheckResult? LastResult { get; set; }

    public bool? LastKnownIsUp { get; set; }

    public DateTimeOffset NextCheckAt { get; set; } = DateTimeOffset.UtcNow;
}
