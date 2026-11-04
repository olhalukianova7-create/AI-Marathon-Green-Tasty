using System.Globalization;
using Microsoft.Playwright;
using Restaurant.Aqa.Api;
using static Microsoft.Playwright.Assertions;

namespace Restaurant.Aqa.Pages;

public sealed class GroupOrderPage(IPage page)
{
    private ILocator Participants => page.GetByRole(AriaRole.List, new() { Name = "Group participants", Exact = true });

    public async Task OpenAsync(string reservationId)
    {
        await page.GotoAsync($"reservations/{Uri.EscapeDataString(reservationId)}/group-order");
        // An empty list has no rendered height. Attachment proves the loaded
        // review exists without incorrectly requiring participants to be present.
        await Expect(Participants).ToBeAttachedAsync(new() { Timeout = 30000 });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Group Pre-Order", Exact = true })).ToBeVisibleAsync();
    }

    public async Task ExpectBasketAsync(ParticipantSnapshot participant)
    {
        var card = Participants.GetByRole(AriaRole.Article).Filter(new()
        {
            Has = page.GetByRole(AriaRole.Heading, new() { Name = $"Guest: {participant.Name}", Exact = true })
        });
        await Expect(card).ToBeVisibleAsync();
        await Expect(card.Locator(".status")).ToContainTextAsync(participant.IsSubmitted ? "Submitted" : "Still choosing");
        await Expect(card.Locator(".subtotal")).ToContainTextAsync(Money(participant.Total));
        if (participant.Items.Length == 0)
            await Expect(card.GetByText("No dishes yet", new() { Exact = true })).ToBeVisibleAsync();
        foreach (var item in participant.Items)
        {
            var row = card.GetByRole(AriaRole.Listitem).Filter(new()
            {
                Has = page.GetByText(item.Dish.Name, new() { Exact = true })
            });
            await Expect(row.GetByLabel($"Quantity: {item.Quantity}", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(row).ToContainTextAsync(Money(item.Quantity * item.Dish.Price));
        }
    }

    public Task ExpectParticipantCountAsync(int count) =>
        Expect(Participants.GetByRole(AriaRole.Article)).ToHaveCountAsync(count);

    public Task ExpectTotalAsync(decimal amount) => Expect(page.Locator(".grand-total strong")).ToHaveTextAsync(Money(amount));

    public async Task RefreshAsync()
    {
        var refresh = page.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true });
        await Expect(refresh).ToBeEnabledAsync();
        await refresh.ClickAsync();
        await Expect(refresh).ToBeEnabledAsync();
    }

    private static string Money(decimal amount) => amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
