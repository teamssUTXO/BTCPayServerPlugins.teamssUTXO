using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker;

public class UptimeCheckerPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.0" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("store-integrations-nav", "UptimeCheckerNav");
        services.AddSingleton<UptimeCheckerService>();
        base.Execute(services);
    }
}
