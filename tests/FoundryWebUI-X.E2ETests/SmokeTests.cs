using FoundryWebUI.E2ETests.Helpers;

using Microsoft.Playwright;

namespace FoundryWebUI.E2ETests;

public class SmokeTests
{
    private async Task<IPage> NewPageAsync()
    {
        var context = await GlobalHooks.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = GlobalHooks.App.BaseUrl,
        });
        return await context.NewPageAsync();
    }

    [Test]
    public async Task HomePage_RendersTitle()
    {
        var page = await NewPageAsync();
        var resp = await page.GotoAsync("/");
        await Assert.That(resp!.Status).IsEqualTo(200);

        var title = await page.TitleAsync();
        await Assert.That(title).Contains("FoundryWebUI-X");
    }

    [Test]
    public async Task ModelsPage_Loads()
    {
        var page = await NewPageAsync();
        var resp = await page.GotoAsync("/Models");
        await Assert.That(resp!.Status).IsEqualTo(200);
    }

    [Test]
    public async Task SettingsPage_Loads()
    {
        var page = await NewPageAsync();
        var resp = await page.GotoAsync("/Settings");
        await Assert.That(resp!.Status).IsEqualTo(200);
    }

    [Test]
    public async Task LogsPage_HasExpectedTabsOnly()
    {
        var page = await NewPageAsync();
        var resp = await page.GotoAsync("/Logs");
        await Assert.That(resp!.Status).IsEqualTo(200);

        await Assert.That(await page.Locator("[data-source=\"foundry\"]").CountAsync()).IsEqualTo(1);
        await Assert.That(await page.Locator("[data-source=\"app\"]").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("[data-source=\"stdout\"]").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("[data-source=\"eventlog\"]").CountAsync()).IsEqualTo(0);
        await Assert.That(await page.Locator("[data-source=\"iis\"]").CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task HomePage_NoUnhandledJsExceptions()
    {
        var page = await NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, msg) => pageErrors.Add(msg);

        await page.GotoAsync("/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assert.That(pageErrors).IsEmpty();
    }
}
