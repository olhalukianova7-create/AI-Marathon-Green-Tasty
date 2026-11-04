using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Entities;
using APIHandler.Handlers.GroupReservations;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Services.GroupReservations;
using Moq;
using Xunit;

namespace Restaurant.Aqa.Tests;

// Public APIs reject confirming an empty basket, so this defensive state cannot be
// prepared through normal HTTP requests. Exercise the real handler and service
// with isolated repositories rather than adding a production data-corruption API.
[Trait("Category", "Level2")]
[Trait("Layer", "APIContract")]
public sealed class Level2ApiContractTests
{
    [Fact]
    public async Task ConfirmedButEmptyBasketsReturn400WithSpecificMessageAndNoWrites()
    {
        var reservations = new Mock<IReservationRepository>();
        var groups = new Mock<IGroupReservationRepository>();
        var sessions = new Mock<IGuestSessionRepository>();
        var orders = new Mock<IOrderRepository>();
        reservations.Setup(r => r.GetReservationByIdAsync("reservation")).ReturnsAsync(new Reservation
        {
            Id = "reservation", CustomerId = "owner", TableId = "table", WaiterId = "waiter"
        });
        groups.Setup(g => g.GetGroupReservationByIdAsync("group")).ReturnsAsync(new GroupReservation
        {
            GroupReservationId = "group", ReservationId = "reservation", ReservationToken = "invite", Status = GroupReservationStatus.Open
        });
        sessions.Setup(s => s.GetAllByGroupReservationIdAsync("group")).ReturnsAsync([
            new GuestSession { Id = "empty", GroupReservationId = "group", GuestName = "Empty", IsSubmitted = true, Dishes = [] }
        ]);
        var service = new GroupPreOrderService(reservations.Object, groups.Object, sessions.Object, orders.Object);
        var handler = new SubmitGroupPreOrderHandler(service);
        var request = new APIGatewayProxyRequest
        {
            Body = "{\"reservationId\":\"reservation\"}",
            PathParameters = new Dictionary<string, string> { ["groupReservationId"] = "group" },
            RequestContext = new()
            {
                Authorizer = new APIGatewayCustomAuthorizerContext { Claims = new() { ["sub"] = "owner" } }
            }
        };
        var context = Mock.Of<ILambdaContext>(c => c.Logger == Mock.Of<ILambdaLogger>());

        var response = await handler.HandleRequest(request, context);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("Submitted guest sessions contain no dishes.", response.Body);
        orders.Verify(o => o.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        groups.Verify(g => g.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }
}
