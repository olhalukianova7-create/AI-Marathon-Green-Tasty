using Restaurant.Aqa.Infrastructure;
using Restaurant.Aqa.Pages;
using Xunit;
using Xunit.Abstractions;

namespace Restaurant.Aqa.Tests;

[Trait("Category", "Level2")]
[Trait("Layer", "UI")]
public sealed class Level2UiTests(ITestOutputHelper output)
{
    [Fact]
    public async Task OwnerSubmitsCombinedPreOrderAndAssignedWaiterSeesIdenticalDishes()
    {
        await using var group = await GroupScenario.CreateAsync(output, assignConfiguredWaiter: true);
        await group.ImportOwnerBasketAsync(confirmed: true);
        await group.ConfirmGuestAsync("First", 2);
        await group.ConfirmGuestAsync("Second", 3);
        var draft = await group.JoinAsync("Draft");
        await group.Api.AddDishAsync(draft, group.Dish.Id, 7);
        var snapshot = await group.WaitForSnapshotAsync(s => s.Participants.Length == 4 && s.Total == group.Dish.Price * 13);

        await BrowserScenario.RunAsync("Level2OwnerSubmission", output, async page =>
        {
            await new LoginPage(page).SignInAsync(group.Environment.Account("OWNER"));
            var review = new GroupOrderPage(page);
            await review.OpenAsync(group.ReservationId);
            await review.ExpectParticipantCountAsync(4);
            await review.ExpectBasketAsync(snapshot.Participants.Single(p => p.Type == "Owner"));
            await review.ExpectTotalAsync(group.Dish.Price * 13);
            await review.SubmitAsync(group.Dish.Price * 6);
            await review.ExpectSubmittedAsync();
        });
        await group.WaitForOrderAsync(o => o.Status == 3 && o.OrderItems.Length == 1 && o.OrderItems[0].Quantity == 6, group.WaiterToken);
        await BrowserScenario.RunAsync("Level2AssignedWaiterOrder", output, async page =>
        {
            await new LoginPage(page).SignInAsync(group.Environment.Account("WAITER"));
            await new WaiterOrderPage(page).ExpectOrderAsync(group.ReservationId, group.Dish, 6);
        });
    }

    [Fact]
    public async Task SubmitStaysDisabledWhenNoBasketIsConfirmed()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        await group.ImportOwnerBasketAsync(confirmed: false);
        var draft = await group.JoinAsync("Draft");
        await group.Api.AddDishAsync(draft, group.Dish.Id, 1);
        await group.WaitForSnapshotAsync(s => s.Participants.Length == 2 && s.Total == group.Dish.Price * 2);
        await BrowserScenario.RunAsync(nameof(SubmitStaysDisabledWhenNoBasketIsConfirmed), output, async page =>
        {
            await new LoginPage(page).SignInAsync(group.Environment.Account("OWNER"));
            var review = new GroupOrderPage(page);
            await review.OpenAsync(group.ReservationId);
            await review.ExpectSubmitDisabledAsync();
        });
    }

    [Fact]
    public async Task OwnerSeesErrorWhenUnderlyingOrderDoesNotExist()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        await group.ConfirmGuestAsync("Confirmed", 2);
        await BrowserScenario.RunAsync(nameof(OwnerSeesErrorWhenUnderlyingOrderDoesNotExist), output, async page =>
        {
            await new LoginPage(page).SignInAsync(group.Environment.Account("OWNER"));
            var review = new GroupOrderPage(page);
            await review.OpenAsync(group.ReservationId);
            await review.SubmitAsync(group.Dish.Price * 2, expectedStatus: 400);
            await review.ExpectSubmissionErrorAsync();
        });
    }
}
