using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models.Migrations;

/// <summary>
/// Creates the <c>uptime_history_settings</c> & <c>uptimechecker_history</c> tables at plugin start
/// </summary>
public class CreateChecksHistoryTableMigration() : MigrationBase<ApplicationDbContext>("20260311_uptimechecker_history_v2")
{
    public override async Task MigrateAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var conn = dbContext.Database.GetDbConnection();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS "uptimechecker_history_settings" (
                "enable_history" BOOLEAN NOT NULL DEFAULT FALSE,
                "retention_days" INTEGER NOT NULL DEFAULT 7
            );

            CREATE TABLE IF NOT EXISTS "uptimechecker_history" (
                "id" TEXT NOT NULL PRIMARY KEY,
                "check_id" TEXT NOT NULL REFERENCES "uptimechecker_checks"("id") ON DELETE CASCADE,
                "url" TEXT NOT NULL,
                "is_up" BOOLEAN NOT NULL,
                "http_status_code" INTEGER,
                "error_message" TEXT,
                "checked_at" TIMESTAMPTZ NOT NULL,
                "check_duration_ms" BIGINT NOT NULL,
                "is_state_change" BOOLEAN NOT NULL DEFAULT FALSE
            );
            """);
    }
}
