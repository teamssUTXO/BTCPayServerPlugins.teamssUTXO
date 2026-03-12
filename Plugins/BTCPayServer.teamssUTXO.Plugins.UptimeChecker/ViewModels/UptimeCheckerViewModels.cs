using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Models;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.ViewModels;

public class UptimeCheckListViewModel
{
    public IReadOnlyList<UptimeCheck> Checks { get; set; } = new List<UptimeCheck>();
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
}
