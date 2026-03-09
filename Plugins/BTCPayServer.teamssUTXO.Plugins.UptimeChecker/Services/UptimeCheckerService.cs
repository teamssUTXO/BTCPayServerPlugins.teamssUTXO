using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;

public class UptimeCheckerService : IHostedService, IDisposable
{
    private readonly SendEmailService _sendEmailService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly ILogger<UptimeCheckerService> _logger;

    // checks restored from DB on startup
    private readonly List<UptimeCheck> _checks = new();
    private readonly SemaphoreSlim _checksLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(20);

    public UptimeCheckerService(SendEmailService sendEmailService, IHttpClientFactory httpClientFactory, ApplicationDbContextFactory dbContextFactory, ILogger<UptimeCheckerService> logger)
    {
        _sendEmailService = sendEmailService;
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadFromDatabaseAsync(cancellationToken);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts != null)
        {
            _cts.Cancel();
            if (_loopTask != null)
                await _loopTask.ConfigureAwait(false);
        }
    }

    public async Task AddOrUpdateCheckAsync(UptimeCheck check)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        await conn.ExecuteAsync("""
            INSERT INTO "uptimechecker_checks"
                ("id", "url", "interval_minutes", "is_enabled", "notification_emails",
                 "last_result", "last_known_is_up", "next_check_at")
            VALUES
                (@id, @url, @interval_minutes, @is_enabled, @notification_emails::jsonb,
                 @last_result::jsonb, @last_known_is_up, @next_check_at)
            ON CONFLICT ("id") DO UPDATE SET
                "url" = EXCLUDED."url",
                "interval_minutes" = EXCLUDED."interval_minutes",
                "is_enabled" = EXCLUDED."is_enabled",
                "notification_emails" = EXCLUDED."notification_emails",
                "last_result" = EXCLUDED."last_result",
                "last_known_is_up" = EXCLUDED."last_known_is_up",
                "next_check_at" = EXCLUDED."next_check_at";
            """,
            new
            {
                id = check.Id,
                url = check.Url,
                interval_minutes = check.IntervalMinutes,
                is_enabled = check.IsEnabled,
                notification_emails = JsonConvert.SerializeObject(check.NotificationEmails),
                last_result = check.LastResult is null ? null : JsonConvert.SerializeObject(check.LastResult),
                last_known_is_up = check.LastKnownIsUp,
                next_check_at = check.NextCheckAt
            });

        await _checksLock.WaitAsync();
        try
        {
            var existing = _checks.FindIndex(c => c.Id == check.Id);
            if (existing >= 0)
                _checks[existing] = check;
            else
                _checks.Add(check);
        }
        finally
        {
            _checksLock.Release();
        }
    }

    public async Task RemoveCheckAsync(string checkId)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        await conn.ExecuteAsync("""DELETE FROM "uptimechecker_checks" WHERE "id" = @id;""", new { id = checkId });

        await _checksLock.WaitAsync();
        try
        {
            _checks.RemoveAll(c => c.Id == checkId);
        }
        finally
        {
            _checksLock.Release();
        }
    }

    public async Task<IReadOnlyList<UptimeCheck>> GetChecksAsync()
    {
        await _checksLock.WaitAsync();
        try
        {
            return _checks.AsReadOnly();
        }
        finally
        {
            _checksLock.Release();
        }
    }

    /// <summary>
    /// Performs HTTP check against a raw URL string (to initialize the state of a check – UP/DOWN).
    /// </summary>
    public Task<UptimeCheckResult> CheckUrlAsync(string url, CancellationToken ct = default) =>
        CheckUrlAsync(new UptimeCheck { Url = url }, ct);

    /// <summary>
    /// Performs the actual HTTP GET request and returns the result.
    /// </summary>
    private async Task<UptimeCheckResult> CheckUrlAsync(UptimeCheck check, CancellationToken ct = default)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var client = _httpClientFactory.CreateClient("UptimeChecker");

        try
        {
            using var response = await client.GetAsync(check.Url, ct);
            var code = (int)response.StatusCode;

            return new UptimeCheckResult
            {
                IsUp = code is >= 200 and < 400,
                HttpStatusCode = code,
                CheckedAt = checkedAt
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UptimeCheckResult
            {
                IsUp = false,
                HttpStatusCode = null,
                ErrorMessage = ex.Message,
                CheckedAt = checkedAt
            };
        }
    }

    private async Task LoadFromDatabaseAsync(CancellationToken ct)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();

            var rows = (await conn.QueryAsync<UptimeCheckSettings>("""
                SELECT "id", "url", "interval_minutes", "is_enabled",
                       "notification_emails"::text, "last_result"::text,
                       "last_known_is_up", "next_check_at"
                FROM "uptimechecker_checks";
                """)).ToList();

            await _checksLock.WaitAsync(ct);
            try
            {
                _checks.Clear();
                _checks.AddRange(rows.Select(r => r.ToDomain()));
                _logger.LogInformation("UptimeChecker: restored {Count} check(s) from database.", _checks.Count);
            }
            finally
            {
                _checksLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to load checks from database.");
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunDueChecksAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(TickInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Determines which checks are currently due based on their scheduled date.
    /// </summary>
    private async Task RunDueChecksAsync(CancellationToken ct)
    {
        List<UptimeCheck> snapshot;

        await _checksLock.WaitAsync(ct);
        try
        {
            snapshot = new List<UptimeCheck>(_checks);
        }
        finally
        {
            _checksLock.Release();
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var check in snapshot)
        {
            if (ct.IsCancellationRequested)
                break;

            if (!check.IsEnabled)
                continue;

            if (check.NextCheckAt > now)
                continue;

            await RunSingleCheckAsync(check, ct);
        }
    }

    private async Task RunSingleCheckAsync(UptimeCheck check, CancellationToken ct)
    {
        var result = await CheckUrlAsync(check, ct);

        var isTransition = check.LastKnownIsUp.HasValue && check.LastKnownIsUp.Value != result.IsUp;
        var isFirstRun = !check.LastKnownIsUp.HasValue;

        UptimeCheck? updatedCheck = null;

        await _checksLock.WaitAsync(ct);
        try
        {
            var live = _checks.Find(c => c.Id == check.Id);
            if (live is null) return; // check if it was removed while running

            live.LastResult = result;
            live.LastKnownIsUp = result.IsUp;
            live.NextCheckAt = DateTimeOffset.UtcNow.AddMinutes(live.IntervalMinutes);

            updatedCheck = live;
        }
        finally
        {
            _checksLock.Release();
        }

        if (updatedCheck is not null)
            await PersistCheckResultAsync(updatedCheck, ct);

        if (!isFirstRun && isTransition)
        {
            if (result.IsUp)
                await _sendEmailService.SendMailUpAsync(check, result);
            else
                await _sendEmailService.SendMailDownAsync(check, result);
        }
    }

    private async Task PersistCheckResultAsync(UptimeCheck check, CancellationToken ct)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();

            await conn.ExecuteAsync("""
                UPDATE "uptimechecker_checks"
                SET "last_result"     = @last_result::jsonb,
                    "last_known_is_up"= @last_known_is_up,
                    "next_check_at"   = @next_check_at
                WHERE "id" = @id;
                """,
                new
                {
                    id = check.Id,
                    last_result = check.LastResult is null ? (string?)null : JsonConvert.SerializeObject(check.LastResult),
                    last_known_is_up = check.LastKnownIsUp,
                    next_check_at = check.NextCheckAt
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to persist check result for check {CheckId}.", check.Id);
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _checksLock.Dispose();
    }
}
