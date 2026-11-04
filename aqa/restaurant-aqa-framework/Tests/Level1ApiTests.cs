using System.Net;
using Restaurant.Aqa.Api;
using Restaurant.Aqa.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Restaurant.Aqa.Tests;

[Trait("Category", "Level1")]
[Trait("Layer", "API")]
public sealed class Level1ApiTests(ITestOutputHelper output)
{
    [Fact]
    public async Task OwnerSeesConfirmedPendingAndEmptyBasketsWithCorrectTotals()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        var confirmed = await group.JoinAsync("Confirmed");
        var pending = await group.JoinAsync("Pending");
        var empty = await group.JoinAsync("Empty");
        await group.Api.AddDishAsync(confirmed, group.Dish.Id, 2);
        await group.WaitForSnapshotAsync(s => s.Participants.Any(p => p.Id == confirmed.SessionId && p.Total == group.Dish.Price * 2));
        await group.Api.SubmitGuestAsync(confirmed);
        await group.Api.AddDishAsync(pending, group.Dish.Id, 1);
        var snapshot = await group.WaitForSnapshotAsync(s => s.Participants.Length == 3
            && s.Total == group.Dish.Price * 3 && s.Participants.Any(p => p.Id == confirmed.SessionId && p.IsSubmitted));

        AssertBasket(snapshot, confirmed.SessionId, group.Dish, 2, true);
        AssertBasket(snapshot, pending.SessionId, group.Dish, 1, false);
        var emptyBasket = Assert.Single(snapshot.Participants, p => p.Id == empty.SessionId);
        Assert.Empty(emptyBasket.Items);
        Assert.Equal(0m, emptyBasket.Total);
        Assert.False(emptyBasket.IsSubmitted);
        Assert.Equal(snapshot.Participants.Sum(p => p.Total), snapshot.Total);
    }

    [Fact]
    public async Task GuestBasketChangeUpdatesParticipantAndGroupTotals()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        var guest = await group.JoinAsync("Changing");
        await group.Api.AddDishAsync(guest, group.Dish.Id, 1);
        await group.WaitForSnapshotAsync(s => s.Total == group.Dish.Price);
        await group.Api.AddDishAsync(guest, group.Dish.Id, 2);
        var snapshot = await group.WaitForSnapshotAsync(s => s.Total == group.Dish.Price * 3);
        AssertBasket(snapshot, guest.SessionId, group.Dish, 3, false);
        Assert.Equal(snapshot.Participants.Sum(p => p.Total), snapshot.Total);
    }

    [Fact]
    public async Task NewlyCreatedGroupHasNoParticipantsAndZeroTotal()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        var snapshot = await group.Api.ParticipantsAsync(group.GroupId, group.OwnerToken);
        Assert.Empty(snapshot.Participants);
        Assert.Equal(0m, snapshot.Total);
    }

    [Theory]
    [InlineData("other-customer", HttpStatusCode.Forbidden)]
    [InlineData("waiter", HttpStatusCode.Forbidden)]
    [InlineData("guest", HttpStatusCode.Unauthorized)]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    public async Task ParticipantsAreRestrictedToReservationOwner(string caller, HttpStatusCode expected)
    {
        await using var group = await GroupScenario.CreateAsync(output);
        var guest = await group.JoinAsync("Restricted");
        await group.Api.AddDishAsync(guest, group.Dish.Id, 1);
        await group.WaitForSnapshotAsync(s => s.Total == group.Dish.Price);
        var token = caller switch
        {
            "other-customer" => group.OtherToken,
            "waiter" => group.WaiterToken,
            "guest" => guest.SessionToken,
            _ => null
        };
        using var response = await group.Api.ParticipantsResponseAsync(group.GroupId, token);
        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(guest.SessionId, body);
        Assert.DoesNotContain(group.Dish.Name, body);
        Assert.DoesNotContain("\"participants\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OwnerReceivesNotFoundForMissingGroup()
    {
        using var api = new GroupApi();
        var token = await api.SignInAsync(new Configuration.TestEnvironment().Account("OWNER"));
        using var response = await api.ParticipantsResponseAsync(Guid.NewGuid().ToString(), token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("not found", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertBasket(ParticipantsSnapshot snapshot, string guestId, TestDish dish, int quantity, bool submitted)
    {
        var participant = Assert.Single(snapshot.Participants, p => p.Id == guestId);
        Assert.Equal("Guest", participant.Type);
        Assert.StartsWith("Aqa ", participant.Name);
        Assert.Equal(submitted, participant.IsSubmitted);
        var item = Assert.Single(participant.Items);
        Assert.Equal(dish.Id, item.Dish.Id);
        Assert.Equal(dish.Name, item.Dish.Name);
        Assert.Equal(dish.Price, item.Dish.Price);
        Assert.Equal(quantity, item.Quantity);
        Assert.Equal(dish.Price * quantity, participant.Total);
    }
}
