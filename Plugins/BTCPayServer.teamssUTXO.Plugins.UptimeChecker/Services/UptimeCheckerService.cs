using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;

public class UptimeCheckerService : IHostedService, IDisposable
{
    private readonly SendEmailService _sendEmailService;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly List<UptimeCheck> _checks = new(); // TODO : à remplacer par une intégration à la BDD de BTCPay Server
    private readonly SemaphoreSlim _checksLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(20);

    public UptimeCheckerService(SendEmailService sendEmailService, IHttpClientFactory httpClientFactory, ILogger<UptimeCheckerService> logger)
    {
        _sendEmailService = sendEmailService;
        _httpClientFactory = httpClientFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
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
    /// Performs HTTP request
    /// </summary>
    public async Task<UptimeCheckResult> CheckUrlAsync(UptimeCheck check, CancellationToken ct = default)
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

    // Background loop
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
    /// <param name="ct"></param>
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

        bool isTransition = check.LastKnownIsUp.HasValue && check.LastKnownIsUp.Value != result.IsUp;
        bool isFirstRun = !check.LastKnownIsUp.HasValue;

        await _checksLock.WaitAsync(ct);
        try
        {
            var live = _checks.Find(c => c.Id == check.Id);
            if (live is null) return; // check if it was removed while running

            live.LastResult = result;
            live.LastKnownIsUp = result.IsUp;
            live.NextCheckAt = DateTimeOffset.UtcNow.AddMinutes(live.IntervalMinutes);
        }
        finally
        {
            _checksLock.Release();
        }

        if (!isFirstRun && isTransition)
        {
            if (result.IsUp)
            {
                await _sendEmailService.SendMailUpAsync(check, result);
            }
            else
            {
                await _sendEmailService.SendMailDownAsync(check, result);
            }

        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _checksLock.Dispose();
    }
}
