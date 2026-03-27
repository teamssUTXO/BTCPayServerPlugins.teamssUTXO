using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Models;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.ViewModels;

public record HistoryFilter(string? UrlFilter, bool? IsUp, bool TransitionsOnly, DateTimeOffset? From, DateTimeOffset? To )
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(UrlFilter) && IsUp is null && !TransitionsOnly && From is null && To is null;
}

public class UptimeCheckListViewModel
{
    public IReadOnlyList<UptimeCheck> Checks { get; set; } = new List<UptimeCheck>();
}

public class SyncAlertSettingsViewModel
{
    public bool EnableSyncAlerts { get; set; }
}

public class UptimeCheckFormViewModel
{
    public string? Id { get; set; }

    [Required]
    [Display(Name = "URL to monitor")]
    [Url(ErrorMessage = "Please enter a valid http:// or https:// URL.")]
    public string Url { get; set; } = string.Empty;

    [Required]
    [Range(1, 1440)]
    [Display(Name = "Check interval (minutes)")]
    public int IntervalMinutes { get; set; } = 5;

    [Display(Name = "Active")]
    public bool IsEnabled { get; set; } = true;

    [Display(Name = "Notification emails")]
    public string NotificationEmailsRaw { get; set; } = string.Empty;
}

public class UptimeCheckHistoryViewModel : BasePagingViewModel
{
    public bool EnableHistory { get; set; }

    [Range(1, 365)]
    [Display(Name = "Retention (days)")]
    public int RetentionDays { get; set; } = 30;

    public IReadOnlyList<UptimeCheckResult> Entries { get; set; } = new List<UptimeCheckResult>();

    public override int CurrentPageCount => Entries.Count;

    // Filter
    [Display(Name = "URL contains")]
    public string? UrlFilter { get; set; }

    [Display(Name = "Status")]
    public string? StatusFilter { get; set; }

    [Display(Name = "Transitions only")]
    public bool TransitionsOnly { get; set; }

    [Display(Name = "From")]
    public DateTimeOffset? DateFrom { get; set; }

    [Display(Name = "To")]
    public DateTimeOffset? DateTo { get; set; }

    public HistoryFilter ToFilter() => new(
        UrlFilter: string.IsNullOrWhiteSpace(UrlFilter) ? null : UrlFilter.Trim(),
        IsUp: StatusFilter switch { "UP" => true, "DOWN" => false, _ => null },
        TransitionsOnly: TransitionsOnly,
        From: DateFrom,
        To: DateTo
    );
}
