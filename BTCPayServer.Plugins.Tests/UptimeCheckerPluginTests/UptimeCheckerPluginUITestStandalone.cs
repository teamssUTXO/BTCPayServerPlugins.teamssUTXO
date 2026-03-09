using BTCPayServer.Abstractions.Models;
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
        await GoToUrl("/server/uptimechecker/create");
        await Page.Locator("#Url").FillAsync("https://example.com");
        await Page.Locator("#IntervalMinutes").FillAsync("10");
        await Page.Locator("#NotificationEmailsRaw").FillAsync("test@test.com");
        await Page.Locator("button[type='submit']").ClickAsync();
        await AssertSuccessMessage("Check for https://example.com created successfully.");
        var urlCell = Page.Locator("table tbody tr td a", new PageLocatorOptions { HasText = "https://example.com" });
        await urlCell.WaitForAsync();
        Assert.True(await urlCell.IsVisibleAsync());
    }

    [Fact]
    public async Task UptimeCheckerEditCheckTest()
    {
        await InitializePlaywright(ServerTester);
        await LoginAsAdmin();
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
}
