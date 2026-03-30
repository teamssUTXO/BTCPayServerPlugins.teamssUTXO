using System;
using System.Linq;
using System.Threading;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.HostedServices;
using BTCPayServer.Tests;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Plugins.Tests.UptimeCheckerPluginTests;

// This test class runs in the "Standalone Tests" collection with a separate database
[Collection("Standalone Tests")]
[Trait("Category", "PlaywrightUITest")]
public class UptimeCheckerPluginUITestStandalone : PlaywrightBaseTest
{
    private readonly StandalonePluginTestFixture _fixture;

    public UptimeCheckerPluginUITestStandalone(StandalonePluginTestFixture fixture, ITestOutputHelper helper) : base(helper)
    {
        _fixture = fixture;
        if (_fixture.ServerTester == null) _fixture.Initialize(this);
        ServerTester = _fixture.ServerTester;
    }

    public ServerTester ServerTester { get; }


    [Fact]
    public async Task UptimeCheckerIndexPageLoadsTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        await GoToUrl("/server/uptimechecker");
        var heading = await Page.Locator("h2").First.TextContentAsync();
        Assert.Contains("Uptime Checker", heading);
        var addButton = Page.Locator("a.btn-primary", new PageLocatorOptions { HasText = "Add check" });
        await addButton.WaitForAsync();
        Assert.True(await addButton.IsVisibleAsync());
    }

    [Fact]
    public async Task UptimeCheckerCreateCheckTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        Page.SetDefaultNavigationTimeout(60000);
        Page.SetDefaultTimeout(60000);
        await GoToUrl("/server/uptimechecker/create");
        await Page.Locator("#Url").FillAsync("https://example.com");
        await Page.Locator("#IntervalMinutes").FillAsync("10");
        await Page.Locator("#NotificationEmailsRaw").FillAsync("test@test.com");
        await Page.Locator("button[type='submit']").ClickAsync();
        await AssertSuccessMessage("Check for https://example.com created successfully.");
        var urlCell = Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "https://example.com", Exact = true });
        await urlCell.WaitForAsync();
        Assert.True(await urlCell.IsVisibleAsync());
    }

    [Fact]
    public async Task UptimeCheckerCreateCheckSSRFSecurityTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        //IPv4
        await GoToUrl("/server/uptimechecker/create");
        await Page.Locator("#Url").FillAsync("https://localhost:0001/");
        await Page.Locator("#IntervalMinutes").FillAsync("1");
        await Page.Locator("#NotificationEmailsRaw").FillAsync("test@test.com");
        await Page.Locator("button[type='submit']").ClickAsync();
        await AssertSuccessMessage("Check for https://localhost:0001/ created successfully.");
        var errorCell = Page.Locator("table tbody tr td", new PageLocatorOptions { HasText = "Url Rejected." }).First;
        await errorCell.WaitForAsync();
        Assert.True(await errorCell.IsVisibleAsync());
        //IPv6
        await GoToUrl("/server/uptimechecker/create");
        await Page.Locator("#Url").FillAsync("http://[::1]/");
        await Page.Locator("#IntervalMinutes").FillAsync("1");
        await Page.Locator("#NotificationEmailsRaw").FillAsync("test@test.com");
        await Page.Locator("button[type='submit']").ClickAsync();
        await AssertSuccessMessage("Check for http://[::1]/ created successfully.");
        errorCell = Page.Locator("table tbody tr td", new PageLocatorOptions { HasText = "Url Rejected." }).First;
        await errorCell.WaitForAsync();
        Assert.True(await errorCell.IsVisibleAsync());
    }

    [Fact]
    public async Task UptimeCheckerCreateCheckEmailSecurityTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        Page.SetDefaultNavigationTimeout(60000);
        Page.SetDefaultTimeout(60000);
        await GoToUrl("/server/uptimechecker/create");
        await Page.Locator("#Url").FillAsync("https://google.com/");
        await Page.Locator("#IntervalMinutes").FillAsync("1");
        await Page.Locator("#NotificationEmailsRaw").FillAsync("no-mail");
        await Page.Locator("button[type='submit']").ClickAsync();
        await FindAlertMessageAsync(StatusMessageModel.StatusSeverity.Error);
        await Page.Locator("#NotificationEmailsRaw").FillAsync("no-mail, test@test.com");
        await Page.Locator("button[type='submit']").ClickAsync();
        await AssertSuccessMessage($"Check for https://google.com/ created successfully. Some email addresses were invalid and have been ignored.");
    }

    [Fact]
    public async Task UptimeCheckerEditCheckTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        Page.SetDefaultNavigationTimeout(60000);
        Page.SetDefaultTimeout(60000);
        await GoToUrl("/server/uptimechecker/create");
        await Page.Locator("#Url").FillAsync("https://httpbin.org/get");
        await Page.Locator("#IntervalMinutes").FillAsync("5");
        await Page.Locator("#NotificationEmailsRaw").FillAsync("test@test.com");
        await Page.Locator("button[type='submit']").ClickAsync();
        await FindAlertMessageAsync();
        var editButton =
            Page.Locator("table tbody tr", new PageLocatorOptions { HasText = "https://httpbin.org/get" })
                .Locator("a.btn-secondary", new LocatorLocatorOptions { HasText = "Edit" });
        await editButton.First.ClickAsync();
        var intervalInput = Page.Locator("#IntervalMinutes");
        await intervalInput.ClearAsync();
        await intervalInput.FillAsync("15");
        var emailInput = Page.Locator("#NotificationEmailsRaw");
        await emailInput.ClearAsync();
        await emailInput.FillAsync("edittest@test.com");
        await Page.Locator("button[type='submit']").ClickAsync();
        await AssertSuccessMessage("Check for https://httpbin.org/get updated successfully.");
        var updatedRow = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = "https://httpbin.org/get" });
        var rowText = await updatedRow.First.TextContentAsync();
        Assert.Contains("15", rowText);
    }

    [Fact]
    public async Task UptimeCheckerDeleteCheckTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        await GoToUrl("/server/uptimechecker/create");
        await Page.Locator("#Url").FillAsync("https://delete-me.example.com");
        await Page.Locator("#IntervalMinutes").FillAsync("5");
        await Page.Locator("#NotificationEmailsRaw").FillAsync("test@test.com");
        await Page.Locator("button[type='submit']").ClickAsync();
        await FindAlertMessageAsync();
        var deleteButton =
            Page.Locator("table tbody tr", new PageLocatorOptions { HasText = "https://delete-me.example.com" })
                .Locator("a.btn-danger");
        await deleteButton.First.ClickAsync();
        // Wait for the Bootstrap confirm modal and click the confirm button
        var confirmButton = Page.Locator("#ConfirmContinue");
        await confirmButton.WaitForAsync();
        await confirmButton.ClickAsync();
        await AssertSuccessMessage("Check deleted.");
        var rows = Page.Locator("table tbody tr", new PageLocatorOptions { HasText = "https://delete-me.example.com" });
        Assert.Equal(0, await rows.CountAsync());
    }


    [Fact]
    public async Task UptimeCheckerHistoryDisabledByDefaultTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        await GoToUrl("/server/uptimechecker/history");

        // Reset to known state first
        var toggle = Page.Locator("#enableHistoryToggle");
        await toggle.WaitForAsync();
        if (await toggle.IsCheckedAsync())
            await toggle.UncheckAsync();
        await Page.Locator("button[type='submit']").First.ClickAsync();
        await AssertSuccessMessage("History settings saved successfully.");

        await toggle.WaitForAsync();
        Assert.False(await toggle.IsCheckedAsync());

        var retentionSection = Page.Locator("#retentionSection");
        Assert.False(await retentionSection.IsVisibleAsync());

        var disabledMsg = Page.Locator("p.text-muted", new PageLocatorOptions { HasText = "History recording is disabled" });
        Assert.True(await disabledMsg.IsVisibleAsync());
    }

    [Fact]
    public async Task UptimeCheckerHistoryEnableAndSaveTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        await GoToUrl("/server/uptimechecker/history");

        // Enable history
        var toggle = Page.Locator("#enableHistoryToggle");
        await toggle.WaitForAsync();
        await toggle.CheckAsync();

        // Retention section should now be visible
        var retentionSection = Page.Locator("#retentionSection");
        await retentionSection.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.True(await retentionSection.IsVisibleAsync());

        // Save settings
        await Page.Locator("button[type='submit']").First.ClickAsync();
        await AssertSuccessMessage("History settings saved successfully.");

        // After save, toggle should remain checked and retention section visible
        var toggleAfter = Page.Locator("#enableHistoryToggle");
        await toggleAfter.WaitForAsync();
        Assert.True(await toggleAfter.IsCheckedAsync());

        var retentionAfter = Page.Locator("#retentionSection");
        Assert.True(await retentionAfter.IsVisibleAsync());
    }

    [Fact]
    [Obsolete("Obsolete")]
    public async Task UptimeCheckerHistoryRetentionDaysValidationTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        await GoToUrl("/server/uptimechecker/history");

        // Enable history first so the retention field is visible
        var toggle = Page.Locator("#enableHistoryToggle");
        await toggle.WaitForAsync();
        await toggle.CheckAsync();

        // Set an out-of-range retention value
        var retentionInput = Page.Locator("#RetentionDays");
        await retentionInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await retentionInput.ClearAsync();
        await retentionInput.FillAsync("366");

        await Page.Locator("button[type='submit']").First.ClickAsync();

        // HTML validation
        var navigationTask = Page.WaitForNavigationAsync();
        await Page.Locator("button[type='submit']").First.ClickAsync();
        var navigated = await Task.WhenAny(navigationTask, Task.Delay(2000)) == navigationTask;
        Assert.False(navigated);

        // await Page.ScreenshotAsync(new PageScreenshotOptions { Path = "C:\\Users\\timmf\\Documents\\GitHub\\BTCPayServerPlugins.teamssUTXO\\debug.png", FullPage = true});

        // Backend validation
        await retentionInput.EvaluateAsync("el => el.removeAttribute('max')");
        await retentionInput.FillAsync("366");
        await Page.RunAndWaitForNavigationAsync(async () =>
        {
            await Page.Locator("button[type='submit']").First.ClickAsync();
        });
        var validationError = Page.Locator(".alert-danger", new PageLocatorOptions { HasText = "Invalid retention period. Must be between 1 and 365 days." });
        await validationError.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.True(await validationError.IsVisibleAsync());
    }

    [Fact]
    public async Task UptimeCheckerHistoryDisableAfterEnableTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        await GoToUrl("/server/uptimechecker/history");

        // Enable and save
        var toggle = Page.Locator("#enableHistoryToggle");
        await toggle.WaitForAsync();
        await toggle.CheckAsync();
        await Page.Locator("button[type='submit']").First.ClickAsync();
        await FindAlertMessageAsync();

        // Now disable and save again
        var toggle2 = Page.Locator("#enableHistoryToggle");
        await toggle2.WaitForAsync();
        await toggle2.UncheckAsync();
        await Page.Locator("button[type='submit']").First.ClickAsync();
        await AssertSuccessMessage("History settings saved successfully.");

        // Toggle should be unchecked, disabled message should be back
        var toggleAfter = Page.Locator("#enableHistoryToggle");
        await toggleAfter.WaitForAsync();
        Assert.False(await toggleAfter.IsCheckedAsync());

        var disabledMsg = Page.Locator("p.text-muted", new PageLocatorOptions { HasText = "History recording is disabled" });
        Assert.True(await disabledMsg.IsVisibleAsync());
    }

    [Fact]
    public async Task UptimeCheckerHistoryRetentionDaysPersistsTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        await GoToUrl("/server/uptimechecker/history");

        // Enable history and set a specific retention period
        var toggle = Page.Locator("#enableHistoryToggle");
        await toggle.WaitForAsync();
        await toggle.CheckAsync();

        var retentionInput = Page.Locator("#RetentionDays");
        await retentionInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await retentionInput.ClearAsync();
        await retentionInput.FillAsync("30");

        await Page.Locator("button[type='submit']").ClickAsync();
        await AssertSuccessMessage("History settings saved successfully.");

        // Reload the page and confirm the value persisted
        await GoToUrl("/server/uptimechecker/history");
        var retentionAfterReload = Page.Locator("#RetentionDays");
        await retentionAfterReload.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var persistedValue = await retentionAfterReload.InputValueAsync();
        Assert.Equal("30", persistedValue);
    }

    [Fact]
    public async Task UptimeCheckerSyncAlertDisabledByDefaultTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        await GoToUrl("/server/uptimechecker/sync");

        var warning = Page.Locator(".alert.alert-warning", new PageLocatorOptions { HasText = "Only the node owner's email" });
        await warning.WaitForAsync();
        Assert.True(await warning.IsVisibleAsync());

        var enableButton = Page.Locator("button", new PageLocatorOptions { HasText = "Enable Sync Alerts" });
        await enableButton.WaitForAsync();
        Assert.True(await enableButton.IsVisibleAsync());
    }

    [Fact]
    public async Task UptimeCheckerSyncAlertEnableAndDisableTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        await GoToUrl("/server/uptimechecker/sync");

        await EnsureSyncAlertsDisabled();

        var enableButton = Page.Locator("button", new PageLocatorOptions { HasText = "Enable Sync Alerts" });
        await enableButton.WaitForAsync();
        await enableButton.ClickAsync();
        await AssertSuccessMessage("Node sync alerts enabled.");

        var disableButton = Page.Locator("button", new PageLocatorOptions { HasText = "Disable Sync Alerts" });
        await disableButton.WaitForAsync();
        Assert.True(await disableButton.IsVisibleAsync());

        await disableButton.ClickAsync();
        await AssertSuccessMessage("Node sync alerts disabled.");

        var enableButtonAfterDisable = Page.Locator("button", new PageLocatorOptions { HasText = "Enable Sync Alerts" });
        await enableButtonAfterDisable.WaitForAsync();
        Assert.True(await enableButtonAfterDisable.IsVisibleAsync());
    }

    [Fact]
    public async Task UptimeCheckerSyncAlertSettingPersistsTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
        await GoToUrl("/server/uptimechecker/sync");

        await EnsureSyncAlertsDisabled();

        var enableButton = Page.Locator("button", new PageLocatorOptions { HasText = "Enable Sync Alerts" });
        await enableButton.WaitForAsync();
        await enableButton.ClickAsync();
        await AssertSuccessMessage("Node sync alerts enabled.");

        await GoToUrl("/server/uptimechecker/sync");
        var disableButton = Page.Locator("button", new PageLocatorOptions { HasText = "Disable Sync Alerts" });
        await disableButton.WaitForAsync();
        Assert.True(await disableButton.IsVisibleAsync());

        await disableButton.ClickAsync();
        await AssertSuccessMessage("Node sync alerts disabled.");
    }

    [Fact]
    public async Task UptimeCheckerHistoryEntryCreatedAfterOneMinuteIntervalTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();

        await EnsureHistoryEnabled();

        var uniqueUrl = $"https://example.com/?uptime-test={Guid.NewGuid():N}";
        await GoToUrl("/server/uptimechecker/create");
        await Page.Locator("#Url").FillAsync(uniqueUrl);
        await Page.Locator("#IntervalMinutes").FillAsync("1");
        await Page.Locator("#NotificationEmailsRaw").FillAsync("test@test.com");
        await Page.Locator("button[type='submit']").ClickAsync();
        await AssertSuccessMessage($"Check for {uniqueUrl} created successfully.");

        var historyEntryFound = false;
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await GoToUrl($"/server/uptimechecker/history?count=10&urlFilter={Uri.EscapeDataString(uniqueUrl)}");
            var row = Page.Locator("table tbody tr td a", new PageLocatorOptions { HasText = uniqueUrl });
            if (await row.CountAsync() > 0)
            {
                historyEntryFound = true;
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        Assert.True(historyEntryFound, "Expected at least one history entry for the check after one minute interval.");
    }

    [Fact]
    public async Task UptimeCheckerSyncAlertSendsEmailWhenNodeBecomesUnsyncedTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();

        await ConfigureServerEmailSettingsAsync();
        await EnsureSyncAlertsEnabled();

        var syncAlertServiceType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .First(t => t.FullName == "BTCPayServer.teamssUTXO.Plugins.UptimeChecker.Services.SyncAlertService");
        var syncAlertService = ServerTester.PayTester.ServiceProvider.GetService(syncAlertServiceType)
            ?? throw new InvalidOperationException("SyncAlertService could not be resolved from service provider.");

        var runSyncCheck = syncAlertServiceType.GetMethod("RunSyncCheckIfDueAsync")
            ?? throw new InvalidOperationException("RunSyncCheckIfDueAsync method not found on SyncAlertService.");

        var dashboard = ServerTester.PayTester.GetService<NBXplorerDashboard>();
        var btcNetwork = ServerTester.NetworkProvider.GetNetwork<BTCPayNetwork>("BTC");

        var currentSummary = dashboard.Get("BTC");

        dashboard.Publish(
            btcNetwork,
            currentSummary?.State ?? NBXplorerState.Ready,
            currentSummary?.Status,
            currentSummary?.MempoolInfo,
            currentSummary?.Error);

        await (Task)runSyncCheck.Invoke(syncAlertService, new object[] { CancellationToken.None })!;

        var email = await ServerTester.AssertHasEmail(async () =>
        {
            dashboard.Publish(
                btcNetwork,
                NBXplorerState.Synching,
                null,
                currentSummary?.MempoolInfo,
                "forced unsynced state for test");

            await (Task)runSyncCheck.Invoke(syncAlertService, new object[] { CancellationToken.None })!;
        });

        Assert.Contains("[NODE UNSYNCED]", email.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BTC", email.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OUT OF SYNC", email.Html ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoginAsAdmin()
    {
        var user = ServerTester.NewAccount();
        await user.GrantAccessAsync();
        await user.MakeAdmin();
        await GoToUrl("/login");
        await LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
    }

    private async Task AssertSuccessMessage(string expected)
    {
        var locator = await FindAlertMessageAsync();
        var text = await locator.TextContentAsync();
        Assert.Equal(expected, text?.Trim());
    }

    private async Task EnsureSyncAlertsDisabled()
    {
        var disableButton = Page.Locator("button", new PageLocatorOptions { HasText = "Disable Sync Alerts" });
        if (await disableButton.CountAsync() > 0 && await disableButton.First.IsVisibleAsync())
        {
            await disableButton.First.ClickAsync();
            await AssertSuccessMessage("Node sync alerts disabled.");
        }
    }

    private async Task EnsureSyncAlertsEnabled()
    {
        await GoToUrl("/server/uptimechecker/sync");
        var enableButton = Page.Locator("button", new PageLocatorOptions { HasText = "Enable Sync Alerts" });
        if (await enableButton.CountAsync() > 0 && await enableButton.First.IsVisibleAsync())
        {
            await enableButton.First.ClickAsync();
            await AssertSuccessMessage("Node sync alerts enabled.");
        }
    }

    private async Task EnsureHistoryEnabled()
    {
        await GoToUrl("/server/uptimechecker/history");
        var toggle = Page.Locator("#enableHistoryToggle");
        await toggle.WaitForAsync();

        if (!await toggle.IsCheckedAsync())
        {
            await toggle.CheckAsync();
            await Page.Locator("button[type='submit']").First.ClickAsync();
            await AssertSuccessMessage("History settings saved successfully.");
        }
    }

    private async Task ConfigureServerEmailSettingsAsync()
    {
        var admin = ServerTester.NewAccount();
        await admin.GrantAccessAsync();
        await admin.MakeAdmin();
        var adminClient = await admin.CreateClient(Policies.Unrestricted);

        await adminClient.UpdateServerEmailSettings(new ServerEmailSettingsData
        {
            Server = ServerTester.MailPitSettings.Hostname,
            Port = ServerTester.MailPitSettings.SmtpPort,
            From = "from@example.com",
            Login = "login@example.com",
            Password = "password",
            DisableCertificateCheck = true
        });
    }
}
