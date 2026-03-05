# BTCPay Server Uptime Checker Plugin

## Setup initial (une seule fois)
````bash
git submodule update --init --recursive
````

## Builder la solution
````bash
dotnet build -maxcpucount:1
````

## Lancer BTCPayServer avec le plugin (dev local)

### Démarrer l'infrastructure Docker (Bitcoin regtest + Postgres + NBXplorer)

````bash
docker compose -f "submodules/btcpayserver/BTCPayServer.Tests/docker-compose.yml" up -d dev --build
````

### Générer appsettings.dev.json (indique à BTCPay où trouver la DLL du plugin)

````bash
dotnet run --project ConfigBuilder
````

### Lancer BTCPayServer

````bash
dotnet run --project submodules/btcpayserver/BTCPayServer/BTCPayServer.csproj --launch-profile Bitcoin
````

Le serveur est accessible sur http://localhost:14142.

### Tests

````bash
dotnet tool install --global Microsoft.Playwright.CLI
playwright install --with-deps
````

## S'assurer que Docker tourne (même commande que 3a)

### Lancer les tests

````bash
dotnet test BTCPayServer.Plugins.Tests --verbosity normal --filter "Category=PlaywrightUITest"
````
