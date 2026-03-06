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
}
