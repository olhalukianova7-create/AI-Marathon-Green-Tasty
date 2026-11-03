using Microsoft.Playwright;
using Restaurant.Aqa.Configuration;
using Xunit.Abstractions;

namespace Restaurant.Aqa.Infrastructure;

public static class BrowserScenario
{
    public static async Task RunAsync(string name, ITestOutputHelper output, Func<IPage, Task> scenario)
    {
        var environment = new TestEnvironment();
        var baseUrl = environment.UiBaseUrl;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = environment.Headless });
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = baseUrl.AbsoluteUri,
            ViewportSize = new() { Width = 1440, Height = 1000 }
        });
        context.SetDefaultTimeout(15000);
        context.SetDefaultNavigationTimeout(30000);
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true, Sources = true });
        var page = await context.NewPageAsync();
        try
        {
            await scenario(page);
        }
        catch
        {
            var directory = Path.Combine(environment.ResultsDirectory, "failures", $"{name}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            // A capture failure must not hide the original test failure.
            try { await page.ScreenshotAsync(new() { Path = Path.Combine(directory, "screenshot.png"), FullPage = true }); }
            catch (Exception ex) { output.WriteLine($"Screenshot capture failed: {ex.Message}"); }
            try { await context.Tracing.StopAsync(new() { Path = Path.Combine(directory, "trace.zip") }); }
            catch (Exception ex) { output.WriteLine($"Trace capture failed: {ex.Message}"); }
            output.WriteLine($"Failure artifacts: {directory}");
            throw;
        }
        await context.Tracing.StopAsync();
    }
}
