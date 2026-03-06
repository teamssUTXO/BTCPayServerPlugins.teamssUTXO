using System.Threading.Tasks;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Controllers;

[AllowAnonymous]
[Route("uptimechecker")]
public class UptimeCheckerPublicController(UptimeCheckerService uptimeCheckerService) : Controller
{
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var checks = await uptimeCheckerService.GetChecksAsync();
        return View(new UptimeStatusViewModel { Checks = checks });
    }
}
