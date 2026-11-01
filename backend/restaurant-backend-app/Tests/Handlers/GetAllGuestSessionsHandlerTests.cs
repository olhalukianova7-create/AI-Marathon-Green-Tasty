using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Entities;
using APIHandler.Mappers;
using APIHandler.Services.GuestSessions;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers;

public class GetAllGuestSessionsHandlerTests
{
    private readonly Mock<IGuestSessionService> _service = new();
    private readonly ILambdaContext _context = Mock.Of<ILambdaContext>(c => c.Logger == Mock.Of<ILambdaLogger>());

    [Fact]
    public async Task SuccessUsesHomeworkJsonContract()
    {
        _service.Setup(s => s.GetAllSessionsAsync("owner", "group")).ReturnsAsync(
            new GetAllSessionsResponse([
                new Participant("session", ParticipantType.Guest, "Andrii", [
                    new OrderItemResponse { Dish = DishMapper.ToDishResponse(new Dish { Id = "dish1", Name = "Salad", Price = 180m }), Quantity = 2 }
                ], 360m, true)
            ], 360m));

        var response = await new GetAllGuestSessionsHandler(_service.Object).HandleRequest(Request(), _context);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(360m, json.RootElement.GetProperty("total").GetDecimal());
        var participant = json.RootElement.GetProperty("participants")[0];
        Assert.Equal("session", participant.GetProperty("id").GetString());
        Assert.Equal("Guest", participant.GetProperty("type").GetString());
        Assert.Equal("Andrii", participant.GetProperty("name").GetString());
        Assert.True(participant.GetProperty("isSubmitted").GetBoolean());
        Assert.Equal(360m, participant.GetProperty("total").GetDecimal());
        var item = participant.GetProperty("items")[0];
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        Assert.Equal(180m, item.GetProperty("dish").GetProperty("price").GetDecimal());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task MissingIdentityReturnsUnauthorized(string? userId)
    {
        var request = Request();
        request.RequestContext.Authorizer.Claims = userId == null ? [] : new() { ["sub"] = userId };
        var response = await new GetAllGuestSessionsHandler(_service.Object).HandleRequest(request, _context);
        Assert.Equal(401, response.StatusCode);
        _service.Verify(s => s.GetAllSessionsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MissingGroupIdReturnsBadRequest()
    {
        var request = Request();
        request.PathParameters = null;
        var response = await new GetAllGuestSessionsHandler(_service.Object).HandleRequest(request, _context);
        Assert.Equal(400, response.StatusCode);
    }

    [Theory]
    [InlineData("group", 404)]
    [InlineData("reservation", 404)]
    [InlineData("dish", 404)]
    [InlineData("owner", 403)]
    public async Task ServiceFailuresReturnExpectedStatus(string failure, int expectedStatus)
    {
        Exception error = failure switch
        {
            "group" => new GroupReservationNotFoundException(),
            "reservation" => new ReservationNotFoundException("Missing reservation"),
            "dish" => new DishNotFoundException("Missing dish"),
            _ => new ActionForbiddenException("Owner only")
        };
        _service.Setup(s => s.GetAllSessionsAsync("owner", "group")).ThrowsAsync(error);
        var response = await new GetAllGuestSessionsHandler(_service.Object).HandleRequest(Request(), _context);
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    private static APIGatewayProxyRequest Request() => new()
    {
        PathParameters = new Dictionary<string, string> { ["groupReservationId"] = "group" },
        RequestContext = new()
        {
            Authorizer = new() { Claims = new() { ["sub"] = "owner" } }
        }
    };
}
