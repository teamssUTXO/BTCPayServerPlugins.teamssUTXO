using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using NBXplorer.Models;
using Newtonsoft.Json;
using InvoiceData = BTCPayServer.Data.InvoiceData;

namespace BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services;

public class UptimeCheckerService
{
    public UptimeCheckerService() {}

    public async Task<bool> CheckURL()
    {
        // méthode qui check une URL en async, ça renvoie un modele de donnée avec le status, le temps de réponse, etc
        return true;
    }
}

public class HTTPResponse
{
    // modèle de réponse HTTP, pour après parser et renvoyer un bool ou autre
}
