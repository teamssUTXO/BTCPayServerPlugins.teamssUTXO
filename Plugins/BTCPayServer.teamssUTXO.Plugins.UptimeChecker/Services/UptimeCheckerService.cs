using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net;
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
    private readonly ChecksHistoryService _checksHistoryService;

    // checks restored from DB on startup
    private readonly List<UptimeCheck> _checks = new();
    private readonly SemaphoreSlim _checksLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15); // adjust this to control how frequently the loop evaluates which checks are due to run

    private DateTimeOffset _lastPurge = DateTimeOffset.MinValue; // MinValue ensures the first purge runs immediately on startup
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);  // adjust this to control how frequently the loop purge old entries - lower values keep history cleaner but increase DB load

    public UptimeCheckerService(SendEmailService sendEmailService, IHttpClientFactory httpClientFactory, ApplicationDbContextFactory dbContextFactory, ILogger<UptimeCheckerService> logger, ChecksHistoryService checksHistoryService)
    {
        _sendEmailService = sendEmailService;
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _checksHistoryService = checksHistoryService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadChecksFromDatabaseAsync(cancellationToken);
        await _checksHistoryService.LoadSettingsFromDatabaseAsync(cancellationToken);

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
    public async Task<UptimeCheckResult> CheckUrlAsync(string url, CancellationToken ct = default)
    {
        var (isSafe, resolvedIp) = await IsUrlSafeAsync(url, ct);
        if (!isSafe || resolvedIp is null)
        {
            return new UptimeCheckResult
            {
                IsUp = false,
                ErrorMessage = "URL rejected.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        return await CheckUrlAsync(new UptimeCheck { Url = url }, ct);
    }

    /// <summary>
    /// Performs the actual HTTP GET request and returns the result.
    /// </summary>
    private async Task<UptimeCheckResult> CheckUrlAsync(UptimeCheck check, CancellationToken ct = default)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var client = _httpClientFactory.CreateClient("UptimeChecker");
        var stw = Stopwatch.StartNew();

        var (isSafe, resolvedIp) = await IsUrlSafeAsync(check.Url, ct);
        if (!isSafe || resolvedIp is null)
        {
            return new UptimeCheckResult
            {
                CheckId = check.Id,
                Url = check.Url,
                IsUp = false,
                HttpStatusCode = null,
                ErrorMessage = "URL rejected.",
                CheckedAt = checkedAt,
                CheckDurationMs = stw.ElapsedMilliseconds
            };
        }

        try
        {
            var uri = new Uri(check.Url);
            var requestUri = new UriBuilder(uri) { Host = resolvedIp.ToString() }.Uri;
            var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Host = uri.Host;

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            stw.Stop();

            var code = (int)response.StatusCode;

            return new UptimeCheckResult
            {
                CheckId = check.Id,
                Url = check.Url,
                IsUp = code is >= 200 and < 400,
                HttpStatusCode = code,
                CheckedAt = checkedAt,
                CheckDurationMs = stw.ElapsedMilliseconds,
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
                CheckId = check.Id,
                Url = check.Url,
                IsUp = false,
                HttpStatusCode = null,
                ErrorMessage = ex.Message,
                CheckedAt = checkedAt,
                CheckDurationMs = stw.ElapsedMilliseconds
            };
        }
    }

    private static async Task<(bool, IPAddress?)> IsUrlSafeAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (false, null);

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return (false, null);

        var host = uri.Host.ToLowerInvariant();
        if (host == "localhost") return (false, null);

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            foreach (var ip in addresses)
            {
                if (IsPrivateOrLoopback(ip))
                    return (false, null);
            }

            return (true, addresses.FirstOrDefault());
        }
        catch
        {
            return (false, null);
        }
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            return IsPrivateOrLoopback(ip.MapToIPv4());

        if (IPAddress.IsLoopback(ip)) return true;

        var bytes = ip.GetAddressBytes();

        // IPv4
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 0 || // 0.0.0.0
                   bytes[0] == 10 || // 10.x.x.x
                   bytes[0] == 127 ||  // 127.x.x.x
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || // 172.16-31.x.x
                   (bytes[0] == 192 && bytes[1] == 168) || // 192.168.x.x
                   (bytes[0] == 169 && bytes[1] == 254) || // 169.254.x.x
                   (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) || // 100.64.0.0/10
                   (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19)); // 198.18.0.0/15
        }

        // IPv6
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.IPv6Any))
                return true;

            var v6 = ip.GetAddressBytes();
            var isUniqueLocal = (v6[0] & 0xFE) == 0xFC; // fc00::/7
            var isMulticast = v6[0] == 0xFF; // ff00::/8

            return ip.IsIPv6LinkLocal ||
                   ip.IsIPv6SiteLocal ||
                   ip.Equals(IPAddress.IPv6Loopback) ||
                   isUniqueLocal ||
                   isMulticast;
        }

        return true;
    }

    private async Task LoadChecksFromDatabaseAsync(CancellationToken ct)
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
                await RunPurgeIfDueAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UptimeChecker: unexpected error in main loop, monitoring continues.");
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
        _logger.LogInformation("UptimeChecker: monitoring loop stopped.");
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
        var dueChecks = snapshot
            .Where(c => c.IsEnabled && c.NextCheckAt <= now)
            .ToList();

        foreach (var check in dueChecks)
            check.NextCheckAt = DateTimeOffset.UtcNow.AddMinutes(check.IntervalMinutes);

        // Limit to 10 simultaneous checks to avoid overloading the network
        var semaphore = new SemaphoreSlim(10);
        var tasks = dueChecks.Select(async check =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await RunSingleCheckAsync(check, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task RunSingleCheckAsync(UptimeCheck check, CancellationToken ct)
    {
        var result = await CheckUrlAsync(check, ct);

        var isTransition = check.LastKnownIsUp.HasValue && check.LastKnownIsUp.Value != result.IsUp;
        var isFirstRun = !check.LastKnownIsUp.HasValue;

        result.IsStateChange = isTransition;

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
        {
            await PersistCheckResultAsync(updatedCheck, ct);
            var settings = await _checksHistoryService.GetHistorySettingsAsync(ct);
            if (settings.enable_history)
            {
                await _checksHistoryService.AppendHistoryEntryAsync(updatedCheck, result, ct);
            }
        }

        if (!isFirstRun && isTransition)
        {
            try
            {
                if (result.IsUp)
                {
                    await _sendEmailService.SendMailUpAsync(check, result);
                    _logger.LogInformation("UptimeChecker: {Url} has been restored. Status: {StatusCode}", result.Url, result.HttpStatusCode);
                }
                else
                {
                    await _sendEmailService.SendMailDownAsync(check, result);
                    _logger.LogError("Uptime Checker: {Url} is down. Message: {StatusCode}", result.Url, result.HttpStatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UptimeChecker: failed to send email for {Url}.", result.Url);
            }

        }
    }

    private async Task RunPurgeIfDueAsync(CancellationToken ct)
    {
        var now =  DateTimeOffset.UtcNow;

        if (now - _lastPurge < PurgeInterval)
            return;

        try
        {
            var settings = await _checksHistoryService.GetHistorySettingsAsync(ct);
            if (!settings.enable_history)
            {
                _lastPurge = now;
                return;
            }

            await _checksHistoryService.PurgeOldEntriesAsync(settings.retention_days, ct);
            _lastPurge = now;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to purge history entries.");
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
                SET "last_result" = @last_result::jsonb,
                    "last_known_is_up"= @last_known_is_up,
                    "next_check_at"  = @next_check_at
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
