using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;

using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;

public class ChecksHistoryService
{
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly ILogger<ChecksHistoryService> _logger;

    public ChecksHistoryService(ApplicationDbContextFactory dbContextFactory, ILogger<ChecksHistoryService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<ChecksHistorySettings> GetHistorySettingsAsync(CancellationToken ct = default)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            var row = await conn.QueryFirstOrDefaultAsync<ChecksHistorySettings>("""
                SELECT "enable_history", "retention_days"
                FROM "uptimechecker_history_settings"
                WHERE "id" = 'global';
                """);
            return row?.ToDomain() ?? new ChecksHistorySettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to load history settings.");
            return new ChecksHistorySettings();
        }
    }

    public async Task SaveHistorySettingsAsync(bool enable, int retentionDays, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            await conn.ExecuteAsync("""
                INSERT INTO "uptimechecker_history_settings" ("id", "enable_history", "retention_days")
                VALUES ('global', @enable, @retentionDays)
                ON CONFLICT ("id") DO UPDATE SET
                    "enable_history" = EXCLUDED."enable_history",
                    "retention_days" = EXCLUDED."retention_days";
                """, new { enable, retentionDays });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to save history settings.");
        }
    }

    public async Task AppendHistoryEntryAsync(UptimeCheck check, UptimeCheckResult result, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            await conn.ExecuteAsync("""
                INSERT INTO "uptimechecker_history"
                    ("id", "check_id", "url", "is_up", "http_status_code", "error_message", "checked_at", "check_duration_ms")
                VALUES
                    (@id, @checkId, @url, @isUp, @httpStatusCode, @errorMessage, @checkedAt, @checkDurationMs);
                """, new
            {
                checkId = check.Id,
                url = result.Url,
                isUp = result.IsUp,
                httpStatusCode = result.HttpStatusCode,
                errorMessage = result.ErrorMessage,
                checkedAt = result.CheckedAt,
                checkDurationMs = result.CheckDurationMs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to append history entry for {CheckId}.", check.Id);
        }
    }

    public async Task<IReadOnlyList<UptimeCheckResult>> GetHistoryEntriesAsync(CancellationToken ct = default)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            var rows = await conn.QueryAsync<UptimeCheckResult>("""
                SELECT "check_id", "url", "is_up", "http_status_code", "error_message", "checked_at", "check_duration_ms"
                FROM "uptimechecker_history"
                ORDER BY "checked_at" DESC;
                """);
            return rows.Select(r => new UptimeCheckResult
            {
                CheckId = r.CheckId,
                Url = r.Url,
                IsUp = r.IsUp,
                HttpStatusCode = r.HttpStatusCode,
                ErrorMessage = r.ErrorMessage,
                CheckedAt = r.CheckedAt,
                CheckDurationMs = r.CheckDurationMs
            }).ToList().AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to load history entries.");
            return new List<UptimeCheckResult>();
        }
    }

    public async Task PurgeOldEntriesAsync(int retentionDays, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            await conn.ExecuteAsync("""
                DELETE FROM "uptimechecker_history"
                WHERE "checked_at" < NOW() - (@days || ' days')::interval;
                """, new { days = retentionDays });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to purge old history entries.");
        }
    }
}
