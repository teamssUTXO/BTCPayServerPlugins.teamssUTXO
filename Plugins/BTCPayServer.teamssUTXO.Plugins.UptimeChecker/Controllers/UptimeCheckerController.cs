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
using MimeKit;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Controllers;


[Route("server/uptimechecker")]
[AutoValidateAntiforgeryToken]
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

        var parsedEmails = ParseEmails(vm.NotificationEmailsRaw);
        var rawCount = vm.NotificationEmailsRaw?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Count(e => !string.IsNullOrWhiteSpace(e)) ?? 0;
        if (parsedEmails.Count == 0)
        {
            TempData[WellKnownTempData.ErrorMessage] = "No valid email addresses were found. Please check your input.";
            return View("Edit", vm);
        }

        // Initialize the state of the checked URL
        var initialResult = await uptimeCheckerService.CheckUrlAsync(vm.Url);

        var check = new UptimeCheck
        {
            Url = vm.Url,
            IntervalMinutes = vm.IntervalMinutes,
            IsEnabled = vm.IsEnabled,
            NotificationEmails = parsedEmails,
            LastResult = initialResult,
            LastKnownIsUp = initialResult.IsUp,
            NextCheckAt = DateTimeOffset.UtcNow.AddMinutes(vm.IntervalMinutes)
        };

        await uptimeCheckerService.AddOrUpdateCheckAsync(check);

        var message = parsedEmails.Count < rawCount
            ? $"Check for {check.Url} created successfully. Some email addresses were invalid and have been ignored."
            : $"Check for {check.Url} created successfully.";
        TempData[WellKnownTempData.SuccessMessage] = message;

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

        var parsedEmails = ParseEmails(vm.NotificationEmailsRaw);
        var rawCount = vm.NotificationEmailsRaw?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Count(e => !string.IsNullOrWhiteSpace(e)) ?? 0;
        if (parsedEmails.Count == 0)
        {
            TempData[WellKnownTempData.ErrorMessage] = "No valid email addresses were found. Please check your input.";
            return View("Edit", vm);
        }

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
            NotificationEmails = parsedEmails,
            LastResult = lastResult,
            LastKnownIsUp = lastKnownIsUp,
            NextCheckAt = existingCheck.NextCheckAt
        };

        await uptimeCheckerService.AddOrUpdateCheckAsync(updatedCheck);

        var message = parsedEmails.Count < rawCount
            ? $"Check for {updatedCheck.Url} updated successfully. Some email addresses were invalid and have been ignored."
            : $"Check for {updatedCheck.Url} updated successfully.";
        TempData[WellKnownTempData.SuccessMessage] = message;

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
    public async Task<IActionResult> History(int skip = 0, int count = 25, string? urlFilter = null, string? statusFilter = null, bool transitionsOnly = false, DateTimeOffset? dateFrom = null, DateTimeOffset? dateTo = null)
    {
        count = Math.Clamp(count, 10, 1000);
        skip  = Math.Max(skip, 0);

        var settings = await checksHistoryService.GetHistorySettingsAsync();

        var vm = new UptimeCheckHistoryViewModel
        {
            EnableHistory = settings.enable_history,
            RetentionDays = settings.retention_days,
            Skip = skip,
            Count = count,
            UrlFilter = urlFilter,
            StatusFilter = statusFilter,
            TransitionsOnly = transitionsOnly,
            DateFrom = dateFrom,
            DateTo = dateTo
        };

        if (!settings.enable_history) return View("History", vm);

        var filter = vm.ToFilter();

        vm.Total = await checksHistoryService.CountHistoryEntriesAsync(filter);
        vm.Entries = await checksHistoryService.GetHistoryEntriesAsync(skip, count, filter);

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
            .Where(IsValidEmail)
            .ToList();

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new System.Net.Mail.MailAddress(email);
            return email.Contains('@') && email.Split('@')[1].Contains('.');
        }
        catch (Exception)
        {
            return false;
        }
    }
}
