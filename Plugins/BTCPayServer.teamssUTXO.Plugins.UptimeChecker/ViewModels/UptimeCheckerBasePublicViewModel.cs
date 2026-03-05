using BTCPayServer.Models;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.ViewModels;

public class UptimeCheckerBasePublicViewModel
{
    public string StoreId { get; set; }
    public string StoreName { get; set; }
    public StoreBrandingViewModel StoreBranding { get; set; }
}
