using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Models;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Controllers;

// GET  /server/uptimechecker              → liste des checks
// GET  /server/uptimechecker/create       → formulaire création
// POST /server/uptimechecker/create       → sauvegarde (URL + intervalle + emails + enabled)
// GET  /server/uptimechecker/{id}/edit    → formulaire édition
// POST /server/uptimechecker/{id}/edit    → met à jour
// POST /server/uptimechecker/{id}/delete  → supprime

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
            Url                = vm.Url,
            IntervalMinutes    = vm.IntervalMinutes,
            IsEnabled          = vm.IsEnabled,
            NotificationEmails = ParseEmails(vm.NotificationEmailsRaw),
            LastResult         = initialResult,
            LastKnownIsUp      = initialResult.IsUp,
            NextCheckAt        = DateTimeOffset.UtcNow.AddMinutes(vm.IntervalMinutes)
        };

        await uptimeCheckerService.AddOrUpdateCheckAsync(check);

        TempData[WellKnownTempData.SuccessMessage] = $"Check for {check.Url} created successfully.";
        return RedirectToAction(nameof(Index));
    }

    private static System.Collections.Generic.List<string> ParseEmails(string raw) =>
        (raw ?? string.Empty)
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();
}
