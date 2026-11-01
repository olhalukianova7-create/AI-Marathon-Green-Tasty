using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.GuestSessions;
using Moq;

namespace APIHandler.Tests.Services;

public class DeleteGuestSessionServiceTests
{
    private readonly Mock<IGroupReservationRepository> _groups = new();
    private readonly Mock<IGuestSessionRepository> _sessions = new();
    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly Mock<IDishRepository> _dishes = new();
    private readonly GuestSessionService _service;
    private readonly GuestSession _deleted = new()
    {
        Id = "session1", GroupReservationId = "group", GuestName = "Andrii", IsSubmitted = true,
        Dishes = [new OrderItem { DishId = "dish1", Quantity = 2 }]
    };

    public DeleteGuestSessionServiceTests()
    {
        _service = new(_groups.Object, _sessions.Object, _dishes.Object, _reservations.Object,
            Mock.Of<IUserRepository>(), Mock.Of<ITableRepository>());
        _groups.Setup(r => r.GetGroupReservationByIdAsync("group")).ReturnsAsync(new GroupReservation
        {
            GroupReservationId = "group", ReservationId = "reservation", ReservationToken = "token"
        });
        _reservations.Setup(r => r.GetReservationByIdAsync("reservation")).ReturnsAsync(new Reservation
        {
            Id = "reservation", CustomerId = "owner", TableId = "table", WaiterId = "waiter"
        });
        _sessions.Setup(r => r.GetByGuestSessionId("session1")).ReturnsAsync(_deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteReturnsOnlyRemainingParticipantsAndRecalculatesTotals(bool staleIndex)
    {
        var remaining = new GuestSession
        {
            Id = "session2", GroupReservationId = "group", GuestName = "Olena", IsSubmitted = false,
            Dishes = [new OrderItem { DishId = "dish2", Quantity = 1 }]
        };
        bool deleted = false;
        _sessions.Setup(r => r.DeleteAsync(_deleted)).Callback(() => deleted = true).Returns(Task.CompletedTask);
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group")).ReturnsAsync(() =>
        {
            Assert.True(deleted, "Snapshot must be loaded after deletion succeeds.");
            return staleIndex ? [_deleted, remaining] : [remaining];
        });
        _dishes.Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([new Dish { Id = "dish2", Name = "Tom Yum", Price = 220m }]);

        var result = await _service.DeleteGuestSessionAsync("owner", "group", "session1");

        var participant = Assert.Single(result.Participants);
        Assert.Equal("session2", participant.Id);
        Assert.Equal("Olena", participant.Name);
        Assert.False(participant.IsSubmitted);
        Assert.Equal(220m, participant.Total);
        Assert.Equal(220m, result.Total);
        var item = Assert.Single(participant.Items);
        Assert.Equal("dish2", item.Dish.Id);
        Assert.Equal(1, item.Quantity);
        _sessions.Verify(r => r.DeleteAsync(_deleted), Times.Once);
        _dishes.Verify(r => r.GetDishesByIdsAsync(It.Is<IEnumerable<string>>(ids => ids.SequenceEqual(new[] { "dish2" }))), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletingLastParticipantReturnsEmptySnapshotEvenWithStaleIndex(bool staleIndex)
    {
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync("group")).ReturnsAsync(staleIndex ? [_deleted] : []);

        var result = await _service.DeleteGuestSessionAsync("owner", "group", "session1");

        Assert.Empty(result.Participants);
        Assert.Equal(0m, result.Total);
        _dishes.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Theory]
    [InlineData("other-user")]
    [InlineData("")]
    [InlineData(" ")]
    public async Task OnlyOwnerCanDelete(string userId)
    {
        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.DeleteGuestSessionAsync(userId, "group", "session1"));
        _sessions.Verify(r => r.GetByGuestSessionId(It.IsAny<string>()), Times.Never);
        AssertNoDeleteOrSnapshot();
    }

    [Fact]
    public async Task SessionInAnotherGroupReturnsValidationErrorAndIsNotDeleted()
    {
        _deleted.GroupReservationId = "other-group";
        await Assert.ThrowsAsync<ValidationException>(() => _service.DeleteGuestSessionAsync("owner", "group", "session1"));
        AssertNoDeleteOrSnapshot();
    }

    [Fact]
    public async Task MissingSessionThrowsNotFoundWithoutDeleting()
    {
        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _service.DeleteGuestSessionAsync("owner", "group", "missing"));
        AssertNoDeleteOrSnapshot();
    }

    [Fact]
    public async Task MissingGroupThrowsNotFoundWithoutDeleting()
    {
        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.DeleteGuestSessionAsync("owner", "missing", "session1"));
        AssertNoDeleteOrSnapshot();
    }

    [Fact]
    public async Task MissingReservationThrowsNotFoundWithoutDeleting()
    {
        _reservations.Setup(r => r.GetReservationByIdAsync("reservation")).ReturnsAsync((Reservation?)null);
        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.DeleteGuestSessionAsync("owner", "group", "session1"));
        AssertNoDeleteOrSnapshot();
    }

    [Fact]
    public async Task FailedDeleteDoesNotReturnSnapshotAsSuccess()
    {
        _sessions.Setup(r => r.DeleteAsync(_deleted)).ThrowsAsync(new InvalidOperationException("Delete failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteGuestSessionAsync("owner", "group", "session1"));
        _sessions.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    private void AssertNoDeleteOrSnapshot()
    {
        _sessions.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
        _sessions.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }
}
