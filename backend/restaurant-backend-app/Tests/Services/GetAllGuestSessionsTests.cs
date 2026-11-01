using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GuestSessions;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.GuestSessions;
using Moq;

namespace APIHandler.Tests.Services;

public class GetAllGuestSessionsTests
{
    private readonly Mock<IGroupReservationRepository> _groups = new();
    private readonly Mock<IGuestSessionRepository> _sessions = new();
    private readonly Mock<IDishRepository> _dishes = new();
    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly GuestSessionService _service;

    public GetAllGuestSessionsTests()
    {
        _service = new GuestSessionService(_groups.Object, _sessions.Object, _dishes.Object,
            _reservations.Object, Mock.Of<IUserRepository>(), Mock.Of<ITableRepository>());
        _groups.Setup(r => r.GetGroupReservationByIdAsync("group"))
            .ReturnsAsync(new GroupReservation { GroupReservationId = "group", ReservationId = "reservation", ReservationToken = "invite" });
        _reservations.Setup(r => r.GetReservationByIdAsync("reservation"))
            .ReturnsAsync(new Reservation { Id = "reservation", TableId = "table", WaiterId = "waiter", CustomerId = "owner" });
    }

    [Fact]
    public async Task ReturnsAllParticipantsWithDishesAndTotalsRegardlessOfSubmission()
    {
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group")).ReturnsAsync([
            Session("session1", "Andrii", true, [new OrderItem { DishId = "dish1", Quantity = 2 }]),
            Session("session2", "Olena", false, [new OrderItem { DishId = "dish2", Quantity = 1 }]),
            new GuestSession { Id = "owner-session", GuestName = "Owner", GroupReservationId = "group", IsOwnerSession = true, Dishes = [] }
        ]);
        _dishes.Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync([
            new Dish { Id = "dish1", Name = "Caesar Salad", Price = 180m },
            new Dish { Id = "dish2", Name = "Tom Yum", Price = 220m }
        ]);

        var result = await _service.GetAllSessionsAsync("owner", "group");
        var participants = result.Participants.ToArray();

        Assert.Equal(580m, result.Total);
        Assert.Equal(3, participants.Length);
        Assert.Equal("session1", participants[0].Id);
        Assert.Equal("Andrii", participants[0].Name);
        Assert.Equal(ParticipantType.Guest, participants[0].Type);
        Assert.True(participants[0].IsSubmitted);
        Assert.Equal(360m, participants[0].Total);
        var item = Assert.Single(participants[0].Items);
        Assert.Equal("dish1", item.Dish.Id);
        Assert.Equal("Caesar Salad", item.Dish.Name);
        Assert.Equal(180m, item.Dish.Price);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(220m, participants[1].Total);
        Assert.False(participants[1].IsSubmitted);
        Assert.Equal(ParticipantType.Owner, participants[2].Type);
        Assert.Empty(participants[2].Items);
        Assert.Equal(0m, participants[2].Total);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyGroupOrCartReturnsZeroWithoutFetchingDishes(bool includeEmptySession)
    {
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group"))
            .ReturnsAsync(includeEmptySession ? [Session("empty", "Guest", false, [])] : []);

        var result = await _service.GetAllSessionsAsync("owner", "group");

        Assert.Equal(0m, result.Total);
        Assert.Equal(includeEmptySession ? 1 : 0, result.Participants.Count());
        _dishes.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task NonOwnerCannotReadSessions()
    {
        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.GetAllSessionsAsync("other-user", "group"));
        _sessions.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MissingGroupThrowsNotFound()
    {
        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetAllSessionsAsync("owner", "missing"));
    }

    [Fact]
    public async Task MissingParentReservationThrowsNotFound()
    {
        _reservations.Setup(r => r.GetReservationByIdAsync("reservation")).ReturnsAsync((Reservation?)null);
        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetAllSessionsAsync("owner", "group"));
    }

    [Fact]
    public async Task MissingDishDoesNotSilentlyUnderstateTotal()
    {
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group"))
            .ReturnsAsync([Session("session", "Guest", false, [new OrderItem { DishId = "missing", Quantity = 1 }])]);
        _dishes.Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync([]);

        await Assert.ThrowsAsync<DishNotFoundException>(() => _service.GetAllSessionsAsync("owner", "group"));
    }

    private static GuestSession Session(string id, string name, bool submitted, List<OrderItem> items) => new()
    {
        Id = id, GuestName = name, GroupReservationId = "group", IsSubmitted = submitted, Dishes = items
    };
}
