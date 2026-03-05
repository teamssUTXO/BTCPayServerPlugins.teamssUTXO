using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Controllers;

[Route("server/stores/uptimechecker")]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UptimeCheckerController(
    StoreRepository storeRepository,
    SettingsRepository settingsRepository,
    UserManager<ApplicationUser> userManager) : Controller
{

    [HttpGet]
    [Route("DefaultHtmlTemplate")]
    public IActionResult DefaultHtmlTemplate()
    {
        return Content(HtmlTemplates.Default);
    }

    [HttpGet]
    public async Task<IActionResult> Config()
    {
        var stores = await storeRepository.GetStores();
        stores = stores.Where(c => !c.Archived).ToArray();
        var model = await settingsRepository.GetSettingAsync<UptimeCheckerSettings>() ?? new();
        var vm = new UptimeCheckerConfigViewModel
        {
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Password = model.Password,
            Enabled = model.Enabled,
            IncludeArchived = model.IncludeArchived,
            IncludeTransactionVolume = model.IncludeTransactionVolume,
            HtmlTemplate = string.IsNullOrWhiteSpace(model.HtmlTemplate) ? HtmlTemplates.Default : model.HtmlTemplate,
            ExtraTransactions = model.ExtraTransactions,
            Stores = stores,
            ExcludedStoreIds = model.ExcludedStoreIds
        };
        return View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Config(UptimeCheckerConfigViewModel viewModel)
    {
        if (string.IsNullOrEmpty(viewModel.HtmlTemplate))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "HTML Template cannot be empty.",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(Config));
        }

        var settings = new UptimeCheckerSettings
        {
            HtmlTemplate = viewModel.HtmlTemplate,
            StartDate = viewModel.StartDate,
            EndDate = viewModel.EndDate,
            Enabled = viewModel.Enabled,
            IncludeArchived = viewModel.IncludeArchived,
            IncludeTransactionVolume = viewModel.IncludeTransactionVolume,
            ExtraTransactions = viewModel.ExtraTransactions,
            Password = viewModel.Password,
            AdminUserId = GetUserId(),
            ExcludedStoreIds = viewModel.ExcludedStoreIds
        };
        await settingsRepository.UpdateSetting(settings);
        TempData[WellKnownTempData.SuccessMessage] = "Uptime Checker configuration updated successfully";
        return RedirectToAction(nameof(Config));
    }

    private string GetUserId() => userManager.GetUserId(User);
}
