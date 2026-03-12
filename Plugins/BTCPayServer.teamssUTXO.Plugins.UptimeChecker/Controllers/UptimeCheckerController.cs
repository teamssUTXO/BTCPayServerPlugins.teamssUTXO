using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Controllers;


[Route("server/uptimechecker")]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UptimeCheckerController(UptimeCheckerService uptimeCheckerService, ChecksHistoryService checksHistoryService) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var checks = await uptimeCheckerService.GetChecksAsync();
        return View(new UptimeCheckListViewModel { Checks = checks });
    }


    [HttpGet("create")]
    public IActionResult Create()
    {
        return View("Edit", new UptimeCheckFormViewModel());
    }


    [HttpPost("create")]
    public async Task<IActionResult> Create(UptimeCheckFormViewModel vm)
    {
        if (!ModelState.IsValid)
            return View("Edit", vm);

        // Initialize the state of the checked URL
        var initialResult = await uptimeCheckerService.CheckUrlAsync(vm.Url);

        var check = new UptimeCheck
        {
            Url = vm.Url,
            IntervalMinutes = vm.IntervalMinutes,
            IsEnabled = vm.IsEnabled,
            NotificationEmails = ParseEmails(vm.NotificationEmailsRaw),
            LastResult = initialResult,
            LastKnownIsUp = initialResult.IsUp,
            NextCheckAt = DateTimeOffset.UtcNow.AddMinutes(vm.IntervalMinutes)
        };

        await uptimeCheckerService.AddOrUpdateCheckAsync(check);

        TempData[WellKnownTempData.SuccessMessage] = $"Check for {check.Url} created successfully.";
        return RedirectToAction(nameof(Index));
    }


    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(string id)
    {
        var checks = await uptimeCheckerService.GetChecksAsync();
        var check  = checks.FirstOrDefault(c => c.Id == id);

        if (check is null)
            return NotFound();

        var vm = new UptimeCheckFormViewModel
        {
            Id = check.Id,
            Url = check.Url,
            IntervalMinutes = check.IntervalMinutes,
            IsEnabled = check.IsEnabled,
            NotificationEmailsRaw = string.Join(", ", check.NotificationEmails)
        };

        return View(vm);
    }


    [HttpPost("{id}/edit")]
    public async Task<IActionResult> Edit(string id, UptimeCheckFormViewModel vm)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var checks = await uptimeCheckerService.GetChecksAsync();
        var existingCheck = checks.FirstOrDefault(c => c.Id == id);

        if (existingCheck is null)
            return NotFound();

        var lastResult = existingCheck.LastResult;
        var lastKnownIsUp = existingCheck.LastKnownIsUp;
        var isEnabled = vm.IsEnabled;

        if (existingCheck.Url != vm.Url && vm.IsEnabled)
        {
            var initialResult = await uptimeCheckerService.CheckUrlAsync(vm.Url);
            lastResult = initialResult;
            lastKnownIsUp = initialResult.IsUp;
        }

        var updatedCheck = new UptimeCheck
        {
            Id = existingCheck.Id,
            Url = vm.Url,
            IntervalMinutes = vm.IntervalMinutes,
            IsEnabled = isEnabled,
            NotificationEmails = ParseEmails(vm.NotificationEmailsRaw),
            LastResult = lastResult,
            LastKnownIsUp = lastKnownIsUp,
            NextCheckAt = existingCheck.NextCheckAt
        };

        await uptimeCheckerService.AddOrUpdateCheckAsync(updatedCheck);

        TempData[WellKnownTempData.SuccessMessage] = $"Check for {updatedCheck.Url} updated successfully.";
        return RedirectToAction(nameof(Index));
    }


    [HttpPost("{id}/delete")]
    public async Task<IActionResult> Delete(string id)
    {
        await uptimeCheckerService.RemoveCheckAsync(id);
        TempData[WellKnownTempData.SuccessMessage] = "Check deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(int skip = 0, int count = 25)
    {
        count = Math.Clamp(count, 10, 1000);
        skip  = Math.Max(skip, 0);

        var settings = await checksHistoryService.GetHistorySettingsAsync();

        var vm = new UptimeCheckHistoryViewModel
        {
            EnableHistory = settings.enable_history,
            RetentionDays = settings.retention_days,
            Skip = skip,
            Count = count
        };

        if (!settings.enable_history) return View("History", vm);

        vm.Total = await checksHistoryService.CountHistoryEntriesAsync();
        vm.Entries = await checksHistoryService.GetHistoryEntriesAsync(skip, count);

        return View("History", vm);
    }

    [HttpPost("history")]
    public async Task<IActionResult> History(UptimeCheckHistoryViewModel vm)
    {
        if (vm.RetentionDays is < 1 or > 365)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Invalid retention period. Must be between 1 and 365 days.";
            return RedirectToAction(nameof(History));
        }
        await checksHistoryService.SaveHistorySettingsAsync(vm.EnableHistory, vm.RetentionDays);
        TempData[WellKnownTempData.SuccessMessage] = "History settings saved.";
        return RedirectToAction(nameof(History));
    }

    private static List<string> ParseEmails(string? raw) =>
        (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();
}
