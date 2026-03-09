using System;
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
public class UptimeCheckerController(UptimeCheckerService uptimeCheckerService) : Controller
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

    private static System.Collections.Generic.List<string> ParseEmails(string? raw) =>
        (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();
}
