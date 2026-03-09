using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models.Migrations;

/// <summary>
/// Creates the <c>uptimechecker_checks</c> table at plugin start
/// </summary>
public class CreateUptimeCheckerTableMigration() : MigrationBase<ApplicationDbContext>("20260309_uptimechecker_init")
{
    public override async Task MigrateAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.GetDbConnection().ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS "uptimechecker_checks" (
                "id" TEXT NOT NULL PRIMARY KEY,
                "url" TEXT NOT NULL,
                "interval_minutes" INTEGER NOT NULL DEFAULT 5,
                "is_enabled" BOOLEAN NOT NULL DEFAULT TRUE,
                "notification_emails" JSONB NOT NULL DEFAULT '[]'::JSONB,
                "last_result" JSONB,
                "last_known_is_up" BOOLEAN,
                "next_check_at" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
        """);
    }
}
