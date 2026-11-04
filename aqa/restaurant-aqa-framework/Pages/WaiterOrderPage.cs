using Microsoft.Playwright;
using Restaurant.Aqa.Api;
using static Microsoft.Playwright.Assertions;

namespace Restaurant.Aqa.Pages;

public sealed class WaiterOrderPage(IPage page)
{
    public async Task ExpectOrderAsync(string reservationId, TestDish dish, int quantity)
    {
        await page.GotoAsync($"waiter-pre-order?reservationId={Uri.EscapeDataString(reservationId)}");
        var cards = page.Locator("app-dish-card");
        await Expect(cards).ToHaveCountAsync(1, new() { Timeout = 30000 });
        await Expect(cards.GetByText(dish.Name, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(cards.GetByText($"Quantity: {quantity}", new() { Exact = true })).ToBeVisibleAsync();
    }
}
