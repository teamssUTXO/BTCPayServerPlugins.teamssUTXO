using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker;

public class UptimeCheckSettings
{
    /// <summary>
    /// BDD representation
    /// </summary>
    public string id { get; set; } = string.Empty;
    public string url { get; set; } = string.Empty;
    public int interval_minutes { get; set; } = 5;
    public bool is_enabled { get; set; } = true;
    public string notification_emails { get; set; } = "[]";
    public string? last_result { get; set; }
    public bool? last_known_is_up { get; set; }
    public DateTimeOffset next_check_at { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// App Representation
    /// </summary>
    public UptimeCheck ToDomain() => new()
    {
        Id = id,
        Url = url,
        IntervalMinutes = interval_minutes,
        IsEnabled = is_enabled,
        NotificationEmails = JsonConvert.DeserializeObject<List<string>>(notification_emails ?? "[]") ?? new List<string>(),
        LastResult = last_result is null ? null : JsonConvert.DeserializeObject<UptimeCheckResult>(last_result),
        LastKnownIsUp = last_known_is_up,
        NextCheckAt = next_check_at
    };
}
