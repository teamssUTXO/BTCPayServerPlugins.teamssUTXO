using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        services.AddSingleton<SendEmailService>();
        // Register as singleton so controllers can inject it, and as hosted service so it runs in the background
        services.AddSingleton<UptimeCheckerService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<UptimeCheckerService>());
        base.Execute(services);
    }
}
