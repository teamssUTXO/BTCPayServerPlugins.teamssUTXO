using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models.Migrations;

/// <summary>
/// Creates the <c>uptime_history_settings</c> & <c>uptimechecker_history</c> tables at plugin start
/// </summary>
public class CreateChecksHistoryTableMigration() : MigrationBase<ApplicationDbContext>("uptimechecker_history_v1.0.1")
{
    public override async Task MigrateAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var conn = dbContext.Database.GetDbConnection();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS "uptimechecker_history_settings" (
                "id" INTEGER NOT NULL PRIMARY KEY DEFAULT 1,
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

            CREATE INDEX IF NOT EXISTS "idx_uptimechecker_history_checked_at"
                ON "uptimechecker_history" ("checked_at" DESC);

            CREATE INDEX IF NOT EXISTS "idx_uptimechecker_history_check_id"
                ON "uptimechecker_history" ("check_id");
            """);
    }
}
