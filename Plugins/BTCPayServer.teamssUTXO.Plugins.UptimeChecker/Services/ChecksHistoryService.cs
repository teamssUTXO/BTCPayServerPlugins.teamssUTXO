using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.ViewModels;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;

public class ChecksHistoryService : IDisposable
{
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly ILogger<ChecksHistoryService> _logger;

    private ChecksHistorySettings _settings = new();
    private readonly SemaphoreSlim _settingsLock = new(1, 1);

    public ChecksHistoryService(ApplicationDbContextFactory dbContextFactory, ILogger<ChecksHistoryService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task LoadSettingsFromDatabaseAsync(CancellationToken ct = default)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            var row = await conn.QueryFirstOrDefaultAsync<ChecksHistorySettings>("""
                SELECT "id", "enable_history", "retention_days"
                FROM "uptimechecker_history_settings"
                LIMIT 1;
                """);
            await _settingsLock.WaitAsync(ct);
            try
            {
                _settings = row?.ToDomain() ?? new ChecksHistorySettings();
            }
            finally
            {
                _settingsLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to load history settings.");
        }
    }

    public async Task<ChecksHistorySettings> GetHistorySettingsAsync(CancellationToken ct = default)
    {
        await _settingsLock.WaitAsync(ct);
        try
        {
            return _settings;
        }
        finally
        {
            _settingsLock.Release();
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
                VALUES (1, @enable, @retentionDays)
                ON CONFLICT ("id") DO UPDATE
                SET "enable_history" = EXCLUDED."enable_history",
                    "retention_days" = EXCLUDED."retention_days";
                """, new { enable, retentionDays });

            await _settingsLock.WaitAsync(ct);
            try
            {
                _settings = new ChecksHistorySettings { enable_history = enable, retention_days = retentionDays };
            }
            finally
            {
                _settingsLock.Release();
            }
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
                    ("id", "check_id", "url", "is_up", "is_state_change", "http_status_code", "error_message", "checked_at", "check_duration_ms")
                VALUES
                    (@id, @checkId, @url, @isUp, @isStateChange, @httpStatusCode, @errorMessage, @checkedAt, @checkDurationMs);
                """, new
            {
                id = Guid.NewGuid().ToString(),
                checkId = check.Id,
                url = result.Url,
                isUp = result.IsUp,
                isStateChange = result.IsStateChange,
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

    public async Task<int> CountHistoryEntriesAsync(HistoryFilter? filter = null, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();

            var (sql, parameters) = BuildFilteredQuery(
                selectClause: "SELECT COUNT(*)",
                filter: filter,
                skip: null,
                count: null);

            return await conn.ExecuteScalarAsync<int>(sql, parameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to count history entries.");
            return 0;
        }
    }

    public async Task<IReadOnlyList<UptimeCheckResult>> GetHistoryEntriesAsync(int skip = 0, int count = 50, HistoryFilter? filter = null, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();

            var (sql, parameters) = BuildFilteredQuery(
                selectClause: """
                    SELECT "check_id" AS "CheckId", "url" AS "Url", "is_up" AS "IsUp",
                           "is_state_change" AS "IsStateChange", "http_status_code" AS "HttpStatusCode",
                           "error_message" AS "ErrorMessage", "checked_at" AS "CheckedAt",
                           "check_duration_ms" AS "CheckDurationMs"
                    """,
                filter: filter,
                skip: skip,
                count: count);

            var rows = await conn.QueryAsync<UptimeCheckResult>(sql, parameters);
            return rows.ToList().AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to load history entries.");
            return new List<UptimeCheckResult>();
        }
    }

    // Query builder
    private static (string Sql, DynamicParameters Parameters) BuildFilteredQuery(string selectClause, HistoryFilter? filter, int? skip, int? count)
    {
        var p = new DynamicParameters(); // Dapper container for SQL parameters to prevent SQL injection

        var sb = new StringBuilder();

        sb.Append(selectClause);
        sb.AppendLine();
        sb.AppendLine("FROM \"uptimechecker_history\"");

        var conditions = new List<string>();

        if (filter?.TransitionsOnly == true)
        {
            conditions.Add("\"is_state_change\" = TRUE");
        }

        if (!string.IsNullOrWhiteSpace(filter?.UrlFilter))
        {
            conditions.Add("LOWER(\"url\") LIKE LOWER(@urlFilter)");
            p.Add("urlFilter", $"%{filter.UrlFilter}%");
        }

        if (filter?.IsUp is bool isUp)
        {
            conditions.Add("\"is_up\" = @isUp");
            p.Add("isUp", isUp);
        }

        if (filter?.From is DateTimeOffset from)
        {
            conditions.Add("\"checked_at\" >= @dateFrom");
            p.Add("dateFrom", from.ToUniversalTime());
        }

        if (filter?.To is DateTimeOffset to)
        {
            conditions.Add("\"checked_at\" <= @dateTo");
            p.Add("dateTo", to.ToUniversalTime());
        }

        if (conditions.Count > 0)
        {
            sb.AppendLine("WHERE " + string.Join("\n  AND ", conditions));
        }

        if (!count.HasValue) return (sb.ToString(), p);

        sb.AppendLine("""ORDER BY "checked_at" DESC""");
        sb.AppendLine("LIMIT @count OFFSET @skip");
        p.Add("count", count.Value);
        p.Add("skip", skip ?? 0);

        return (sb.ToString(), p);
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

    public void Dispose()
    {
        _settingsLock.Dispose();
    }
}
