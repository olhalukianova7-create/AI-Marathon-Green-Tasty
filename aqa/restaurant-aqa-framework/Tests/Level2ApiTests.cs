using System.Net;
using Restaurant.Aqa.Api;
using Restaurant.Aqa.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Restaurant.Aqa.Tests;

[Trait("Category", "Level2")]
[Trait("Layer", "API")]
public sealed class Level2ApiTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ConfirmedOwnerAndGuestBasketsAreAggregatedAndDraftBasketExcluded()
    {
        await using var group = await GroupScenario.CreateAsync(output, assignConfiguredWaiter: true);
        await group.ImportOwnerBasketAsync(confirmed: true); // owner quantity 1
        await group.ConfirmGuestAsync("First", 2);
        await group.ConfirmGuestAsync("Second", 3);
        var draft = await group.JoinAsync("Draft");
        await group.Api.AddDishAsync(draft, group.Dish.Id, 7);
        await group.WaitForSnapshotAsync(s => s.Total == group.Dish.Price * 13);

        using var response = await group.Api.SubmitGroupAsync(group.GroupId, group.ReservationId, group.OwnerToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var submitted = Assert.Single(await GroupApi.ReadAsync<ConsolidatedDish[]>(response));
        Assert.Equal(group.Dish.Id, submitted.DishId);
        Assert.Equal(6, submitted.Quantity);
        var waiterOrder = await group.WaitForOrderAsync(o => o.Status == 3, group.WaiterToken);
        Assert.Equal(group.ReservationId, waiterOrder.ReservationId);
        var item = Assert.Single(waiterOrder.OrderItems);
        Assert.Equal(group.Dish.Id, item.Dish.Id);
        Assert.Equal(6, item.Quantity);
        var status = await group.Api.GetAsync<GroupStatusResult>($"guest/groups/{group.GroupId}/status", group.OwnerToken);
        Assert.Equal("Submitted", status.Status);
    }

    [Fact]
    public async Task NoConfirmedBasketReturnsSpecificValidationMessageWithoutSubmitting()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        await group.CreateOrderAsync();
        var draft = await group.JoinAsync("Draft");
        await group.Api.AddDishAsync(draft, group.Dish.Id, 1);
        await group.WaitForSnapshotAsync(s => s.Total == group.Dish.Price);
        using var response = await group.Api.SubmitGroupAsync(group.GroupId, group.ReservationId, group.OwnerToken);
        await ExpectErrorAsync(response, HttpStatusCode.BadRequest, "There are no submitted guest sessions.");
        Assert.Equal("Open", (await group.Api.GetAsync<GroupStatusResult>($"guest/groups/{group.GroupId}/status", group.OwnerToken)).Status);
        Assert.Equal(0, (await group.Api.GetAsync<OrderSnapshot>($"orders/{group.ReservationId}", group.OwnerToken)).Status);
    }

    [Fact]
    public async Task MissingOrderReturnsSpecificValidationMessage()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        await group.ConfirmGuestAsync("Confirmed", 1);
        using var response = await group.Api.SubmitGroupAsync(group.GroupId, group.ReservationId, group.OwnerToken);
        await ExpectErrorAsync(response, HttpStatusCode.BadRequest, "Order was not found for this reservation.");
    }

    [Theory]
    [InlineData("group")]
    [InlineData("reservation")]
    public async Task MissingResourceReturnsNotFound(string resource)
    {
        await using var group = await GroupScenario.CreateAsync(output);
        using var response = await group.Api.SubmitGroupAsync(
            resource == "group" ? Guid.NewGuid().ToString() : group.GroupId,
            resource == "reservation" ? Guid.NewGuid().ToString() : group.ReservationId,
            group.OwnerToken);
        await ExpectErrorAsync(response, HttpStatusCode.NotFound, "not found");
    }

    [Theory]
    [InlineData("other-customer", HttpStatusCode.Forbidden)]
    [InlineData("waiter", HttpStatusCode.Forbidden)]
    [InlineData("guest", HttpStatusCode.Unauthorized)]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    public async Task SubmissionIsRestrictedToOwner(string role, HttpStatusCode expected)
    {
        await using var group = await GroupScenario.CreateAsync(output);
        await group.CreateOrderAsync();
        var guest = await group.ConfirmGuestAsync("Confirmed", 2);
        var token = role switch
        {
            "other-customer" => group.OtherToken,
            "waiter" => group.WaiterToken,
            "guest" => guest.SessionToken,
            _ => null
        };
        using var response = await group.Api.SubmitGroupAsync(group.GroupId, group.ReservationId, token);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Forbidden)
            Assert.Contains("Only the reservation owner", await response.Content.ReadAsStringAsync());
        Assert.Equal("Open", (await group.Api.GetAsync<GroupStatusResult>($"guest/groups/{group.GroupId}/status", group.OwnerToken)).Status);
        Assert.Equal(0, (await group.Api.GetAsync<OrderSnapshot>($"orders/{group.ReservationId}", group.OwnerToken)).Status);
    }

    [Fact]
    public async Task RepeatedSubmissionIsRejectedAndDoesNotDuplicateOrderDishes()
    {
        await using var group = await GroupScenario.CreateAsync(output);
        await group.CreateOrderAsync();
        await group.ConfirmGuestAsync("Confirmed", 2);
        using var first = await group.Api.SubmitGroupAsync(group.GroupId, group.ReservationId, group.OwnerToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await group.WaitForOrderAsync(o => o.Status == 3);
        using var second = await group.Api.SubmitGroupAsync(group.GroupId, group.ReservationId, group.OwnerToken);
        await ExpectErrorAsync(second, HttpStatusCode.BadRequest, "Group reservation was submitted.");
        var order = await group.Api.GetAsync<OrderSnapshot>($"orders/{group.ReservationId}", group.OwnerToken);
        Assert.Equal(2, Assert.Single(order.OrderItems).Quantity);
    }

    private static async Task ExpectErrorAsync(HttpResponseMessage response, HttpStatusCode status, string message)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}
