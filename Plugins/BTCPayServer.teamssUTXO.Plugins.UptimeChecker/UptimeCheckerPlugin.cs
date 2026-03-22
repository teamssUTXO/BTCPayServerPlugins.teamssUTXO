using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models.Migrations;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;
using System.Net.Http;
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
        services.AddHttpClient("UptimeChecker", client => { client.Timeout = TimeSpan.FromSeconds(45); })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<SendEmailService>();
        services.AddSingleton<ChecksHistoryService>();
        services.AddSingleton<UptimeCheckerService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<UptimeCheckerService>());
        services.AddMigration<ApplicationDbContext, CreateUptimeCheckerTableMigration>();
        services.AddMigration<ApplicationDbContext, CreateChecksHistoryTableMigration>();
        base.Execute(services);
    }
}
