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
    private string? _ownerEmail;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);

    private static readonly TimeSpan SyncCheckInterval = TimeSpan.FromMinutes(1);
    private DateTimeOffset _lastSyncCheck = DateTimeOffset.MinValue;
    private bool? _lastKnownNodeSynced;

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

            var ownerEmail = await ResolveOwnerEmailAsync(ct);

            await _settingsLock.WaitAsync(ct);
            try
            {
                _settings = row?.ToDomain() ?? new SyncAlertSettings();
                _ownerEmail = ownerEmail;
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

    public async Task<string?> GetOwnerEmailAsync(CancellationToken ct = default)
    {
        await _settingsLock.WaitAsync(ct);
        try
        {
            return _ownerEmail;
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

            var ownerEmail = await ResolveOwnerEmailAsync(ct);

            await _settingsLock.WaitAsync(ct);
            try
            {
                _settings = new SyncAlertSettings { enable_sync_alerts = enableSyncAlerts };
                _ownerEmail = ownerEmail;
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

    // TODO : est appelé deux fois. faut le mettre en cache
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

    //TODO : est hardcodé à 1 minute Pas configurable par l'admin — c'est un choix acceptable pour une v1 mais à noter
    public async Task RunSyncCheckIfDueAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastSyncCheck < SyncCheckInterval)
            return;

        _lastSyncCheck = now;

        var settings = await GetSyncAlertSettingsAsync(ct);
        if (!settings.enable_sync_alerts)
            return;

        var status = GetNodeSyncStatus();
        if (status is null)
            return;

        if (!_lastKnownNodeSynced.HasValue)
        {
            _lastKnownNodeSynced = status.Value.IsSynced;
            return;
        }

        if (_lastKnownNodeSynced.Value == status.Value.IsSynced)
            return;

        var ownerEmail = await GetOwnerEmailAsync(ct);
        if (string.IsNullOrWhiteSpace(ownerEmail))
        {
            _logger.LogWarning("UptimeChecker: sync alert state changed but no owner email is configured.");
            _lastKnownNodeSynced = status.Value.IsSynced;
            return;
        }

        try
        {
            if (status.Value.IsSynced)
            {
                await _sendEmailService.SendSyncUpMailAsync(status.Value.Network, now, status.Value.Details, ownerEmail);
                _logger.LogInformation("UptimeChecker: node sync recovered for {Network}.", status.Value.Network);
            }
            else
            {
                await _sendEmailService.SendSyncDownMailAsync(status.Value.Network, now, status.Value.Details, ownerEmail);
                _logger.LogWarning("UptimeChecker: node out of sync for {Network}. Details: {Details}", status.Value.Network, status.Value.Details);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UptimeChecker: failed to send node sync alert email.");
        }
        finally
        {
            _lastKnownNodeSynced = status.Value.IsSynced;
        }
    }

    // TODO : retourne le premier réseau non synchronisé. Si tu as BTC et LTC et que les deux sont désynchronisés, tu n'alertes que sur le premier.
    private (bool IsSynced, string Network, string? Details)? GetNodeSyncStatus()
    {
        var summaries = _nbXplorerDashboard.GetAll()?.ToList();
        if (summaries is null || summaries.Count == 0)
            return null;

        var unsynced = summaries.FirstOrDefault(s => s?.Status?.IsFullySynched is not true);
        if (unsynced is not null)
        {
            var details = BuildSyncDetails(unsynced);
            return (false, unsynced.Network.CryptoCode, details);
        }

        var synced = summaries.FirstOrDefault(s => s?.Status is not null);
        if (synced is null)
            return null;

        return (true, synced.Network.CryptoCode, BuildSyncDetails(synced));
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
