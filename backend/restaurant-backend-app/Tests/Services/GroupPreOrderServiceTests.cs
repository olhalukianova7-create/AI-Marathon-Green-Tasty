using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Services.GroupReservations;
using Moq;

namespace APIHandler.Tests.Services;

public class GroupPreOrderServiceTests
{
    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly Mock<IGroupReservationRepository> _groups = new();
    private readonly Mock<IGuestSessionRepository> _sessions = new();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly GroupPreOrderService _service;
    private readonly GroupReservation _group = new()
    {
        GroupReservationId = "group", ReservationId = "reservation", ReservationToken = "token", Status = GroupReservationStatus.Open
    };
    private readonly Order _order = new()
    {
        OrderId = "order", ReservationId = "reservation", CustomerId = "owner",
        ReservationAddress = "Address", ReservationTime = "Time", Dishes = [], Status = OrderStatus.Created
    };

    public GroupPreOrderServiceTests()
    {
        _service = new(_reservations.Object, _groups.Object, _sessions.Object, _orders.Object);
        _reservations.Setup(r => r.GetReservationByIdAsync("reservation")).ReturnsAsync(new Reservation
        {
            Id = "reservation", CustomerId = "owner", TableId = "table", WaiterId = "waiter"
        });
        _groups.Setup(r => r.GetByReservationIdAsync("reservation")).ReturnsAsync(_group);
        _groups.Setup(r => r.GetGroupReservationByIdAsync("group")).ReturnsAsync(_group);
        _orders.Setup(r => r.GetOrderByReservationIdAsync("reservation")).ReturnsAsync(_order);
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group"))
            .ReturnsAsync([Session("guest", true, new OrderItem { DishId = "dish1", Quantity = 1 })]);
    }

    [Fact]
    public async Task OwnerIdLookupSelectsOnlyCallersOwnerSession()
    {
        var guest = Session("guest", false);
        var otherOwner = Session("other", false);
        otherOwner.IsOwnerSession = true;
        otherOwner.OwnerUserId = "other-user";
        var owner = Session("owner-session", false);
        owner.IsOwnerSession = true;
        owner.OwnerUserId = "owner";
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group")).ReturnsAsync([guest, otherOwner, owner]);

        var result = await _service.GetOwnerSessionIdAsync("owner", "reservation");

        Assert.Equal("owner-session", result.GuestSessionId);
    }

    [Theory]
    [InlineData("lookup")]
    [InlineData("submit")]
    public async Task BothOperationsRejectNonOwnerBeforeReadingSessions(string operation)
    {
        await Assert.ThrowsAsync<ActionForbiddenException>(() => Invoke(operation, "other-user", "reservation"));
        _sessions.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
        AssertNoWrites();
    }

    [Theory]
    [InlineData("lookup")]
    [InlineData("submit")]
    public async Task BothOperationsReturnNotFoundForMissingReservation(string operation)
    {
        await Assert.ThrowsAsync<ReservationNotFoundException>(() => Invoke(operation, "owner", "missing"));
        AssertNoWrites();
    }

    [Theory]
    [InlineData("lookup")]
    [InlineData("submit")]
    public async Task BothOperationsReturnNotFoundForMissingGroup(string operation)
    {
        _groups.Setup(r => r.GetByReservationIdAsync("reservation")).ReturnsAsync((GroupReservation?)null);
        _groups.Setup(r => r.GetGroupReservationByIdAsync("group")).ReturnsAsync((GroupReservation?)null);
        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => Invoke(operation, "owner", "reservation"));
        AssertNoWrites();
    }

    [Fact]
    public async Task LookupDoesNotReturnGuestSessionWhenOwnerSessionIsMissing()
    {
        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _service.GetOwnerSessionIdAsync("owner", "reservation"));
    }

    [Fact]
    public async Task SubmitCombinesOnlySubmittedSessionsAndSetsBothStatuses()
    {
        var owner = Session("owner-session", true, new OrderItem { DishId = "dish1", Quantity = 1 });
        owner.IsOwnerSession = true;
        owner.OwnerUserId = "owner";
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group")).ReturnsAsync([
            owner,
            Session("guest1", true, new OrderItem { DishId = "dish1", Quantity = 2 }, new OrderItem { DishId = "dish2", Quantity = 2 }),
            Session("draft", false, new OrderItem { DishId = "ignored", Quantity = 99 })
        ]);
        _order.Dishes = [new OrderItem { DishId = "stale", Quantity = 10 }];

        var result = await _service.SubmitAsync("owner", "group", "reservation");

        Assert.Collection(result,
            dish => { Assert.Equal("dish1", dish.DishId); Assert.Equal(3, dish.Quantity); },
            dish => { Assert.Equal("dish2", dish.DishId); Assert.Equal(2, dish.Quantity); });
        Assert.Equal(OrderStatus.SubmittedByCustomer, _order.Status);
        Assert.Equal(GroupReservationStatus.Submitted, _group.Status);
        Assert.Equal(2, _order.Dishes.Count);
        Assert.Equal(3, _order.Dishes.Single(d => d.DishId == "dish1").Quantity);
        Assert.All(_order.Dishes, d => Assert.Equal(OrderItemState.Pending, d.State));
        _orders.Verify(r => r.SaveOrderAsync(_order), Times.Once);
        _groups.Verify(r => r.SaveGroupReservationAsync(_group), Times.Once);
    }

    [Theory]
    [InlineData("no-sessions")]
    [InlineData("draft-only")]
    [InlineData("empty-submitted")]
    [InlineData("missing-order")]
    [InlineData("wrong-reservation")]
    [InlineData("invalid-quantity")]
    [InlineData("empty-dish-id")]
    [InlineData("wrong-order-owner")]
    [InlineData("wrong-order-reservation")]
    public async Task InvalidSubmitReturnsValidationErrorWithoutWrites(string scenario)
    {
        switch (scenario)
        {
            case "no-sessions":
                _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group")).ReturnsAsync([]); break;
            case "draft-only":
                _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group")).ReturnsAsync([Session("draft", false, new OrderItem { DishId = "dish", Quantity = 1 })]); break;
            case "empty-submitted":
                _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group")).ReturnsAsync([Session("empty", true)]); break;
            case "missing-order":
                _orders.Setup(r => r.GetOrderByReservationIdAsync("reservation")).ReturnsAsync((Order?)null); break;
            case "wrong-reservation": _group.ReservationId = "other-reservation"; break;
            case "invalid-quantity":
                _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group")).ReturnsAsync([Session("guest", true, new OrderItem { DishId = "dish", Quantity = 0 })]); break;
            case "empty-dish-id":
                _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group")).ReturnsAsync([Session("guest", true, new OrderItem { DishId = " ", Quantity = 1 })]); break;
            case "wrong-order-owner": _order.CustomerId = "other-user"; break;
            case "wrong-order-reservation": _order.ReservationId = "other-reservation"; break;
        }

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync("owner", "group", "reservation"));
        AssertNoWrites();
    }

    [Theory]
    [InlineData(GroupReservationStatus.Closed)]
    [InlineData(GroupReservationStatus.Submitted)]
    public async Task InactiveGroupCannotBeSubmitted(GroupReservationStatus status)
    {
        _group.Status = status;
        await Assert.ThrowsAnyAsync<ValidationException>(() => _service.SubmitAsync("owner", "group", "reservation"));
        AssertNoWrites();
    }

    [Theory]
    [InlineData(OrderStatus.Submitted)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task ProcessedOrCancelledOrderCannotBeSubmitted(OrderStatus status)
    {
        _order.Status = status;
        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitAsync("owner", "group", "reservation"));
        AssertNoWrites();
    }

    [Fact]
    public async Task FailedOrderSaveDoesNotMarkGroupSubmitted()
    {
        _orders.Setup(r => r.SaveOrderAsync(It.IsAny<Order>())).ThrowsAsync(new InvalidOperationException("Save failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SubmitAsync("owner", "group", "reservation"));
        Assert.Equal(GroupReservationStatus.Open, _group.Status);
        _groups.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    [Fact]
    public async Task RetryAfterGroupSaveFailureDoesNotDoubleOrderQuantities()
    {
        _groups.SetupSequence(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .ThrowsAsync(new InvalidOperationException("Save failed")).Returns(Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SubmitAsync("owner", "group", "reservation"));
        _group.Status = GroupReservationStatus.Open; // Stored group was not changed by the failed save.

        await _service.SubmitAsync("owner", "group", "reservation");

        Assert.Equal(1, Assert.Single(_order.Dishes).Quantity);
        Assert.Equal(GroupReservationStatus.Submitted, _group.Status);
    }

    private async Task Invoke(string operation, string userId, string reservationId)
    {
        if (operation == "lookup") await _service.GetOwnerSessionIdAsync(userId, reservationId);
        else await _service.SubmitAsync(userId, "group", reservationId);
    }

    private void AssertNoWrites()
    {
        _orders.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groups.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    private static GuestSession Session(string id, bool submitted, params OrderItem[] items) => new()
    {
        Id = id, GuestName = id, GroupReservationId = "group", IsSubmitted = submitted, Dishes = items.ToList()
    };
}
