using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Restaurant.Aqa.Pages;

public sealed class MenuPage(IPage page)
{
    // The application has no test IDs for these components. Scope the CSS fallback
    // to the Angular component, then use roles and accessible image names.
    private ILocator DishCards => page.Locator("app-dish-card").GetByRole(AriaRole.Button);
    private ILocator DishModal => page.Locator("app-dish-modal");

    public async Task OpenAsync()
    {
        var response = await page.GotoAsync("menu");
        if (response is null || !response.Ok)
            throw new InvalidOperationException($"Menu navigation failed with HTTP {response?.Status}.");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Menu", Exact = true })).ToBeVisibleAsync();
        await Expect(DishCards.First).ToBeVisibleAsync(new() { Timeout = 30000 });
    }

    public async Task OpenFirstDishAsync()
    {
        var name = await DishCards.First.GetByRole(AriaRole.Img).GetAttributeAsync("alt");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("First dish has no name.");
        await DishCards.First.ClickAsync();
        await Expect(DishModal.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true })).ToBeVisibleAsync();
        await Expect(DishModal.GetByRole(AriaRole.Img, new() { Name = name, Exact = true })).ToBeVisibleAsync();
    }
}
