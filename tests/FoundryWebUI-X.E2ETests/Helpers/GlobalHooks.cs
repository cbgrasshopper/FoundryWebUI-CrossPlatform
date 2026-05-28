using Microsoft.Playwright;

namespace FoundryWebUI.E2ETests.Helpers;

/// <summary>
/// Assembly-level lifecycle: spin up the app + Playwright once, tear down at the end.
/// </summary>
public static class GlobalHooks
{
    public static AppHostFixture App { get; private set; } = null!;
    public static IPlaywright Playwright { get; private set; } = null!;
    public static IBrowser Browser { get; private set; } = null!;

    [Before(Assembly)]
    public static async Task SetupAsync()
    {
        App = await AppHostFixture.StartAsync();

        // Ensures Chromium is downloaded; on CI we run `playwright install chromium`
        // explicitly so this is a no-op there.
        var exit = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"`playwright install chromium` failed with exit code {exit}.");
        }

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    [After(Assembly)]
    public static async Task TeardownAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        Playwright?.Dispose();
        if (App is not null) await App.DisposeAsync();
    }
}
