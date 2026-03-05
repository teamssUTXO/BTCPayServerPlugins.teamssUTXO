using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;
using BTCPayServer.teamssUTXO.Plugins.UptimeChecker.ViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Controllers;

[AllowAnonymous]
[Route("uptimechecker/")]
public class UptimeCheckerPublicController(
    UriResolver uriResolver,
    StoreRepository storeRepo,
    SettingsRepository settingsRepository,
    UptimeCheckerService uptimeCheckerService) : Controller
{
    [HttpGet("html")]
    public async Task<IActionResult> Counter([FromQuery] string password)
    {
        var model = await settingsRepository.GetSettingAsync<UptimeCheckerSettings>() ?? new UptimeCheckerSettings();
        if (!model.Enabled)
            return NotFound();

        if (!string.IsNullOrEmpty(model.Password))
        {
            var validationResult = await ValidatePassword(model, password);
            if (validationResult != null)
                return validationResult;
        }

        if (string.IsNullOrEmpty(model.HtmlTemplate) ||
            !model.HtmlTemplate.Contains("{COUNTER}") ||
            !model.HtmlTemplate.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            !model.HtmlTemplate.Contains("<body", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Invalid HTML template, or missing {COUNTER} placeholder. If you updated plugin version, make sure to update the HTML template as well.");

        var invoiceTransactions = await InvoiceTransactionQuery(model);
        var viewModel = new UptimeCheckerPublicViewModel
        {
            HtmlTemplate = model.HtmlTemplate,
            InitialCount = invoiceTransactions.TransactionCount,
            InitialVolumeByCurrency = invoiceTransactions.VolumeByCurrency
        };
        return View(viewModel);
    }

    [HttpGet("api")]
    public async Task<IActionResult> ApiCounter([FromQuery] string password)
    {
        // Add CORS headers directly to the response
        Response.Headers.Append("Access-Control-Allow-Origin", "*");
        Response.Headers.Append("Access-Control-Allow-Headers", "*");
        Response.Headers.Append("Access-Control-Allow-Methods", "GET");

        var model = await settingsRepository.GetSettingAsync<UptimeCheckerSettings>() ?? new UptimeCheckerSettings();
        if (!model.Enabled)
            return NotFound();

        if (!string.IsNullOrEmpty(model.Password))
        {
            var validationResult = await ValidatePassword(model, password);
            if (validationResult != null)
                return validationResult;
        }

        var transactionResult = await InvoiceTransactionQuery(model);
        return Json(new
        {
            count = transactionResult.TransactionCount,
            volumeByCurrency = transactionResult.VolumeByCurrency
        });
    }

    private Task<InvoiceTransactionResult> InvoiceTransactionQuery(UptimeCheckerSettings model)
    {
        return uptimeCheckerService.GetTransactionCountAsync(model);
    }

    private async Task<IActionResult> ValidatePassword(UptimeCheckerSettings model, string password)
    {
        if (string.IsNullOrEmpty(password) || password != model.Password)
        {
            var adminStores = await storeRepo.GetStoresByUserId(model.AdminUserId);
            var storeData = adminStores[0];
            var publicModel = new UptimeCheckerBasePublicViewModel
            {
                StoreId = storeData.Id,
                StoreName = storeData?.StoreName,
                StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, storeData.GetStoreBlob())
            };
            return View("PasswordRequired", publicModel);
        }

        return null;
    }
}
