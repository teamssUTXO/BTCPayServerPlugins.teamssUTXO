using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models.Migrations;

public class CreateSyncAlertTableMigration() : MigrationBase<ApplicationDbContext>("uptimechecker_sync_alert_settings_v1.2.0")
{
    public override async Task MigrateAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.GetDbConnection().ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS "uptimechecker_sync_alert_settings" (
                "id" INTEGER NOT NULL PRIMARY KEY DEFAULT 1,
                "enable_sync_alerts" BOOLEAN NOT NULL DEFAULT FALSE
            );
            """);
    }
}
