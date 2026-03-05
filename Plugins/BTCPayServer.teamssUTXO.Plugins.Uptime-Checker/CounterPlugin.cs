using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker;

public class CounterPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.1.0" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("store-integrations-nav", "PluginCounterNav");
        services.AddSingleton<TxCounterService>();
        base.Execute(services);
    }
}
