using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Restaurant.Aqa.Api;
using Restaurant.Aqa.Infrastructure;
using Restaurant.Aqa.Pages;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;

namespace Restaurant.Aqa.Tests;

[Trait("Category", "Smoke")]
public sealed class MenuSmokeTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Layer", "UI")]
    public Task AnonymousVisitorCanBrowseMenuAndOpenDish() =>
        BrowserScenario.RunAsync(nameof(AnonymousVisitorCanBrowseMenuAndOpenDish), output, async page =>
        {
            var menu = new MenuPage(page);
            await menu.OpenAsync();
            await menu.OpenFirstDishAsync();
        });
}

[Trait("Category", "Smoke")]
public sealed class GroupAccessSmokeTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Layer", "UI")]
    public Task AnonymousVisitorIsRedirectedFromGroupOrderToLogin() =>
        BrowserScenario.RunAsync(nameof(AnonymousVisitorIsRedirectedFromGroupOrderToLogin), output, async page =>
        {
            await page.GotoAsync($"reservations/{Guid.NewGuid():N}/group-order");
            await Expect(page).ToHaveURLAsync(new Regex(@"/login(?:[?#].*)?$"));
            await Expect(page.GetByRole(Microsoft.Playwright.AriaRole.Heading,
                new() { Name = "Group Pre-Order", Exact = true })).Not.ToBeVisibleAsync();
        });
}

[Trait("Category", "Smoke")]
public sealed class ApiSmokeTests
{
    [Fact]
    [Trait("Layer", "API")]
    public async Task PublicDishesEndpointReturnsNonemptyMenu()
    {
        using var api = new RestaurantApi();
        using var response = await api.GetDishesAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.NotEmpty(body.RootElement.EnumerateArray());
    }

    [Fact]
    [Trait("Layer", "API")]
    public async Task GroupParticipantsEndpointRejectsAnonymousRequest()
    {
        using var api = new RestaurantApi();
        // Authentication must run before looking up the group; no data is created.
        using var response = await api.GetParticipantsAnonymouslyAsync(Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
