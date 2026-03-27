using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using BTCPayServer.HostedServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;

public class SyncAlertService : IDisposable
{
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SendEmailService _sendEmailService;
    private readonly ILogger<SyncAlertService> _logger;
    private readonly NBXplorerDashboard _nbXplorerDashboard;

    private SyncAlertSettings _settings = new();
    private readonly System.Collections.Generic.Dictionary<string, bool> _lastKnownNetworkSynced = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _settingsLock = new(1, 1);

    public SyncAlertService(
        ApplicationDbContextFactory dbContextFactory,
        IServiceScopeFactory scopeFactory,
        SendEmailService sendEmailService,
        ILogger<SyncAlertService> logger,
        NBXplorerDashboard nbXplorerDashboard)
    {
        _dbContextFactory = dbContextFactory;
        _scopeFactory = scopeFactory;
        _sendEmailService = sendEmailService;
        _logger = logger;
        _nbXplorerDashboard = nbXplorerDashboard;
    }

    public async Task LoadSettingsFromDatabaseAsync(CancellationToken ct = default)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            var row = await conn.QueryFirstOrDefaultAsync<SyncAlertSettings>("""
                SELECT "id", "enable_sync_alerts"
                FROM "uptimechecker_sync_alert_settings"
                LIMIT 1;
                """);

            await _settingsLock.WaitAsync(ct);
            try
            {
                _settings = row?.ToDomain() ?? new SyncAlertSettings();
            }
            finally
            {
                _settingsLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to load sync alert settings.");
        }
    }

    public async Task<SyncAlertSettings> GetSyncAlertSettingsAsync(CancellationToken ct = default)
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

    public async Task SaveSyncAlertSettingsAsync(bool enableSyncAlerts, CancellationToken ct = default)
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            await conn.ExecuteAsync("""
                INSERT INTO "uptimechecker_sync_alert_settings" ("id", "enable_sync_alerts")
                VALUES (1, @enableSyncAlerts)
                ON CONFLICT ("id") DO UPDATE
                SET "enable_sync_alerts" = EXCLUDED."enable_sync_alerts";
                """, new { enableSyncAlerts });

            await _settingsLock.WaitAsync(ct);
            try
            {
                _settings = new SyncAlertSettings { enable_sync_alerts = enableSyncAlerts };
            }
            finally
            {
                _settingsLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to save sync alert settings.");
            throw;
        }
    }

    private async Task<string?> ResolveOwnerEmailAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admins = await userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
        var owner = admins
            .Where(u => !string.IsNullOrWhiteSpace(u.Email))
            .OrderBy(u => u.Created ?? DateTimeOffset.MaxValue)
            .ThenBy(u => u.Id)
            .FirstOrDefault();

        return owner?.Email;
    }

    public async Task RunSyncCheckIfDueAsync(CancellationToken ct)
    {
        var settings = await GetSyncSettingsAsync(ct);
        if (!settings.enable_sync_alerts)
            return;

        var summaries = _nbXplorerDashboard.GetAll()?.ToList();
        if (summaries is null || summaries.Count == 0)
            return;

        var transitions = new System.Collections.Generic.List<(bool IsSynced, string Network, string? Details)>();

        foreach (var summary in summaries)
        {
            if (summary?.Network is null)
                continue;

            var network = summary.Network.CryptoCode;
            var isSynced = summary.Status?.IsFullySynched is true;

            if (!_lastKnownNetworkSynced.TryGetValue(network, out var previous))
            {
                _lastKnownNetworkSynced[network] = isSynced;
                continue;
            }

            if (previous == isSynced)
                continue;

            transitions.Add((isSynced, network, BuildSyncDetails(summary)));
            _lastKnownNetworkSynced[network] = isSynced;
        }

        if (transitions.Count == 0)
            return;

        var ownerEmail = await ResolveOwnerEmailAsync(ct);
        if (string.IsNullOrWhiteSpace(ownerEmail))
        {
            _logger.LogWarning("UptimeChecker: sync alert state changed but no owner email is configured.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var unsyncedTransitions = transitions.Where(t => !t.IsSynced).ToList();
        var syncedTransitions = transitions.Where(t => t.IsSynced).ToList();

        if (unsyncedTransitions.Count > 0)
        {
            try
            {
                var networks = string.Join(", ", unsyncedTransitions.Select(t => t.Network).Distinct(StringComparer.OrdinalIgnoreCase));
                var details = string.Join(" | ", unsyncedTransitions
                    .Where(t => !string.IsNullOrWhiteSpace(t.Details))
                    .Select(t => $"{t.Network}: {t.Details}"));

                await _sendEmailService.SendSyncDownMailAsync(networks, now, string.IsNullOrWhiteSpace(details) ? null : details, ownerEmail);
                _logger.LogWarning("UptimeChecker: node out of sync for network(s): {Networks}", networks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UptimeChecker: failed to send grouped node sync down email.");
            }

            if (syncedTransitions.Count > 0)
            {
                var skippedNetworks = string.Join(", ", syncedTransitions.Select(t => t.Network).Distinct(StringComparer.OrdinalIgnoreCase));
                _logger.LogInformation("UptimeChecker: skipping sync recovery email because down transition(s) exist in the same tick. Skipped networks: {Networks}", skippedNetworks);
            }

            return;
        }

        if (syncedTransitions.Count > 0)
        {
            try
            {
                var networks = string.Join(", ", syncedTransitions.Select(t => t.Network).Distinct(StringComparer.OrdinalIgnoreCase));
                var details = string.Join(" | ", syncedTransitions
                    .Where(t => !string.IsNullOrWhiteSpace(t.Details))
                    .Select(t => $"{t.Network}: {t.Details}"));

                await _sendEmailService.SendSyncUpMailAsync(networks, now, string.IsNullOrWhiteSpace(details) ? null : details, ownerEmail);
                _logger.LogInformation("UptimeChecker: node sync recovered for network(s): {Networks}", networks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UptimeChecker: failed to send grouped node sync recovery email.");
            }
        }
    }

    private async Task<SyncAlertSettings> GetSyncSettingsAsync(CancellationToken ct)
    {
        await _settingsLock.WaitAsync(ct);
        try
        {
            return _settings.ToDomain();
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private static string? BuildSyncDetails(NBXplorerDashboard.NBXplorerSummary summary)
    {
        var status = summary.Status;
        if (status?.BitcoinStatus is null)
            return summary.State.ToString();

        var bitcoinStatus = status.BitcoinStatus;
        return $"State={summary.State}, Blocks={bitcoinStatus.Blocks}, Headers={bitcoinStatus.Headers}, VerificationProgress={bitcoinStatus.VerificationProgress:P2}";
    }

    public void Dispose()
    {
        _settingsLock.Dispose();
    }
}
