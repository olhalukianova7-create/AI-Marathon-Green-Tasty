using Restaurant.Aqa.Infrastructure;
using Restaurant.Aqa.Pages;
using Xunit;
using Xunit.Abstractions;

namespace Restaurant.Aqa.Tests;

[Trait("Category", "Level1")]
[Trait("Layer", "UI")]
public sealed class Level1UiTests(ITestOutputHelper output)
{
    [Fact]
    public async Task OwnerSeesGuestDishesQuantitiesAndBasketStatuses()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        var confirmed = await group.JoinAsync("Confirmed");
        var pending = await group.JoinAsync("Pending");
        await group.Api.AddDishAsync(confirmed, group.Dish.Id, 2);
        await group.WaitForSnapshotAsync(s => s.Total == group.Dish.Price * 2);
        await group.Api.SubmitGuestAsync(confirmed);
        await group.Api.AddDishAsync(pending, group.Dish.Id, 1);
        var snapshot = await group.WaitForSnapshotAsync(s => s.Total == group.Dish.Price * 3
            && s.Participants.Any(p => p.Id == confirmed.SessionId && p.IsSubmitted));

        await BrowserScenario.RunAsync(nameof(OwnerSeesGuestDishesQuantitiesAndBasketStatuses), output, async page =>
        {
            await new LoginPage(page).SignInAsync(group.Environment.Account("OWNER"));
            var review = new GroupOrderPage(page);
            await review.OpenAsync(group.ReservationId);
            await review.ExpectParticipantCountAsync(2);
            foreach (var participant in snapshot.Participants) await review.ExpectBasketAsync(participant);
        });
    }

    [Fact]
    public async Task OwnerSeesGuestWithoutDishesAndPendingStatus()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        await group.JoinAsync("Empty");
        var snapshot = await group.WaitForSnapshotAsync(s => s.Participants.Length == 1);
        await BrowserScenario.RunAsync(nameof(OwnerSeesGuestWithoutDishesAndPendingStatus), output, async page =>
        {
            await new LoginPage(page).SignInAsync(group.Environment.Account("OWNER"));
            var review = new GroupOrderPage(page);
            await review.OpenAsync(group.ReservationId);
            await review.ExpectParticipantCountAsync(1);
            await review.ExpectBasketAsync(snapshot.Participants[0]);
            await review.ExpectTotalAsync(0m);
        });
    }

    [Fact]
    public async Task OwnerSeesEmptyGroupWithZeroTotal()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        await BrowserScenario.RunAsync(nameof(OwnerSeesEmptyGroupWithZeroTotal), output, async page =>
        {
            await new LoginPage(page).SignInAsync(group.Environment.Account("OWNER"));
            var review = new GroupOrderPage(page);
            await review.OpenAsync(group.ReservationId);
            await review.ExpectParticipantCountAsync(0);
            await review.ExpectTotalAsync(0m);
        });
    }

    [Fact]
    public async Task GrandTotalIncludesAllBasketsAndChangesWhenGuestAddsDishes()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        var guest = await group.JoinAsync("Updating");
        await group.Api.AddDishAsync(guest, group.Dish.Id, 1);
        await group.WaitForSnapshotAsync(s => s.Total == group.Dish.Price);
        await BrowserScenario.RunAsync(nameof(GrandTotalIncludesAllBasketsAndChangesWhenGuestAddsDishes), output, async page =>
        {
            await new LoginPage(page).SignInAsync(group.Environment.Account("OWNER"));
            var review = new GroupOrderPage(page);
            await review.OpenAsync(group.ReservationId);
            // Homework Level 1 requires all participant baskets in the total.
            await review.ExpectTotalAsync(group.Dish.Price);
            await group.Api.AddDishAsync(guest, group.Dish.Id, 2);
            var snapshot = await group.WaitForSnapshotAsync(s => s.Total == group.Dish.Price * 3);
            await review.RefreshAsync();
            await review.ExpectBasketAsync(snapshot.Participants.Single());
            await review.ExpectTotalAsync(group.Dish.Price * 3);
        });
    }
}
