using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Data;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.ViewModels;

public class UptimeCheckerConfigViewModel
{
    [Display(Name = "Start Date")]
    public DateTime StartDate { get; set; }

    [Display(Name = "End Date")]
    public DateTime? EndDate { get; set; }

    [Display(Name = "Enable uptime checker")]
    public bool Enabled { get; set; }

    [Display(Name = "Include archived invoices")]
    public bool IncludeArchived { get; set; }

    [Display(Name = "Include Transaction Volume Data (more expensive queries)")]
    public bool IncludeTransactionVolume { get; set; }

    public string? Password { get; set; }
    public StoreData[] Stores { get; set; }

    [Display(Name = "HTML Template")]
    public string? HtmlTemplate { get; set; }

    [Display(Name = "Custom Transactions")]
    public string ExtraTransactions { get; set; }

    public string ExcludedStoreIds { get; set; }
}

public class UptimeStatusViewModel
{
    public IReadOnlyList<UptimeCheck> Checks { get; set; } = new List<UptimeCheck>();
}
