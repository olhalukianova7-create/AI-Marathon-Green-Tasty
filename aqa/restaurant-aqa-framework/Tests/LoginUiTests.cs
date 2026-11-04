using System.Text.RegularExpressions;
using Restaurant.Aqa.Configuration;
using Restaurant.Aqa.Infrastructure;
using Restaurant.Aqa.Pages;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace Restaurant.Aqa.Tests;

[Trait("Category", "AccountSetup")]
[Trait("Layer", "UI")]
public sealed class LoginUiTests(ITestOutputHelper output)
{
    [Fact]
    public Task OwnerCanSignIn() => BrowserScenario.RunAsync(nameof(OwnerCanSignIn), output, async page =>
    {
        await new LoginPage(page).SignInAsync(new TestEnvironment().Account("OWNER"));
        await page.GotoAsync("reservations");
        await Expect(page).ToHaveURLAsync(new Regex(@"/reservations(?:[?#].*)?$"));
    });
}
