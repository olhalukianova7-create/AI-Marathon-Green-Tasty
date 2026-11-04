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
            Has = page.GetByRole(AriaRole.Heading, new() { Name = participant.Type == "Owner" ? "My Pre-Order" : $"Guest: {participant.Name}", Exact = true })
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
        // Background synchronization disables the button while requests are pending.
        // Cloud response time can exceed the default five-second assertion timeout.
        await Expect(refresh).ToBeEnabledAsync(new() { Timeout = 30000 });
        var response = await page.RunAndWaitForResponseAsync(
            () => refresh.ClickAsync(new() { Timeout = 30000 }),
            response => response.Request.Method == "GET"
                && new Uri(response.Url).AbsolutePath.Contains("/guest/groups/")
                && new Uri(response.Url).AbsolutePath.EndsWith("/sessions"),
            new() { Timeout = 30000 });
        if (response.Status != 200)
            throw new InvalidOperationException($"UI refresh expected HTTP 200, received {response.Status}.");
        await Expect(refresh).ToBeEnabledAsync(new() { Timeout = 30000 });
    }

    public Task ExpectSubmitDisabledAsync() =>
        Expect(page.GetByRole(AriaRole.Button, new() { Name = "Submit Pre-Order", Exact = true })).ToBeDisabledAsync();

    public async Task SubmitAsync(decimal confirmedTotal, int expectedStatus = 200)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit Pre-Order", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.Locator(".confirmation-total")).ToContainTextAsync(Money(confirmedTotal));
        var response = await page.RunAndWaitForResponseAsync(
            () => dialog.GetByRole(AriaRole.Button, new() { Name = "Submit", Exact = true }).ClickAsync(),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath.Contains("/group-reservations/")
                && new Uri(response.Url).AbsolutePath.EndsWith("/submit"));
        if (response.Status != expectedStatus)
            throw new InvalidOperationException($"UI submission expected HTTP {expectedStatus}, received {response.Status}.");
    }

    public async Task ExpectSubmittedAsync()
    {
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Sent to Restaurant" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sent to Restaurant", Exact = true })).ToBeDisabledAsync();
    }

    public Task ExpectSubmissionErrorAsync() =>
        Expect(page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "Couldn't send your pre-order" })).ToBeVisibleAsync();

    private static string Money(decimal amount) => amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
