using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Mappers;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Services.GuestSessions;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers;

public class DeleteGuestSessionHandlerTests
{
    private readonly Mock<IGuestSessionService> _service = new();
    private readonly ILambdaContext _context = Mock.Of<ILambdaContext>(c => c.Logger == Mock.Of<ILambdaLogger>());

    [Fact]
    public async Task SuccessReturnsUpdatedSnapshotUsingHomeworkJsonNames()
    {
        _service.Setup(s => s.DeleteGuestSessionAsync("owner", "group", "session1")).ReturnsAsync(
            new GetAllSessionsResponse([
                new Participant("session2", ParticipantType.Guest, "Olena", [new OrderItemResponse
                {
                    Dish = DishMapper.ToDishResponse(new Dish { Id = "dish2", Name = "Tom Yum", Price = 220m }), Quantity = 1
                }], 220m, false)
            ], 220m));

        var response = await Handle(Request());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(220m, json.RootElement.GetProperty("total").GetDecimal());
        Assert.False(json.RootElement.TryGetProperty("Participants", out _));
        var participants = json.RootElement.GetProperty("participants");
        Assert.Equal(1, participants.GetArrayLength());
        var participant = participants[0];
        Assert.Equal("session2", participant.GetProperty("id").GetString());
        Assert.Equal("Guest", participant.GetProperty("type").GetString());
        Assert.Equal("Olena", participant.GetProperty("name").GetString());
        Assert.False(participant.GetProperty("isSubmitted").GetBoolean());
        Assert.Equal(220m, participant.GetProperty("total").GetDecimal());
        var item = participant.GetProperty("items")[0];
        Assert.Equal("dish2", item.GetProperty("dish").GetProperty("id").GetString());
        Assert.Equal(220m, item.GetProperty("dish").GetProperty("price").GetDecimal());
        Assert.Equal(1, item.GetProperty("quantity").GetInt32());
    }

    [Theory]
    [InlineData("groupReservationId", null)]
    [InlineData("groupReservationId", "")]
    [InlineData("groupReservationId", " ")]
    [InlineData("guestSessionId", null)]
    [InlineData("guestSessionId", "")]
    [InlineData("guestSessionId", " ")]
    public async Task InvalidPathReturns400WithoutDeleting(string parameter, string? value)
    {
        var request = Request();
        if (value == null) request.PathParameters.Remove(parameter);
        else request.PathParameters[parameter] = value;
        Assert.Equal(400, (await Handle(request)).StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("no-context")]
    [InlineData("no-claims")]
    [InlineData("no-sub")]
    [InlineData("blank-sub")]
    public async Task InvalidAuthenticationReturns401WithoutDeleting(string scenario)
    {
        var request = Request();
        switch (scenario)
        {
            case "no-context": request.RequestContext = null; break;
            case "no-claims": request.RequestContext.Authorizer.Claims = null; break;
            case "no-sub": request.RequestContext.Authorizer.Claims.Clear(); break;
            case "blank-sub": request.RequestContext.Authorizer.Claims["sub"] = " "; break;
        }
        Assert.Equal(401, (await Handle(request)).StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("non-owner", 403)]
    [InlineData("wrong-group", 400)]
    [InlineData("missing-session", 404)]
    [InlineData("missing-group", 404)]
    [InlineData("missing-reservation", 404)]
    [InlineData("missing-dish", 404)]
    public async Task DomainErrorsReturnExpectedStatus(string scenario, int expectedStatus)
    {
        Exception error = scenario switch
        {
            "non-owner" => new ActionForbiddenException("Owner only"),
            "wrong-group" => new ValidationException("Session belongs to another group"),
            "missing-session" => new GuestSessionNotFoundException("Session missing"),
            "missing-group" => new GroupReservationNotFoundException(),
            "missing-reservation" => new ReservationNotFoundException("Reservation missing"),
            _ => new DishNotFoundException("Dish missing")
        };
        _service.Setup(s => s.DeleteGuestSessionAsync("owner", "group", "session1")).ThrowsAsync(error);
        Assert.Equal(expectedStatus, (await Handle(Request())).StatusCode);
    }

    private Task<APIGatewayProxyResponse> Handle(APIGatewayProxyRequest request) =>
        new DeleteGuestSessionHandler(_service.Object).HandleRequest(request, _context);

    private static APIGatewayProxyRequest Request() => new()
    {
        PathParameters = new Dictionary<string, string> { ["groupReservationId"] = "group", ["guestSessionId"] = "session1" },
        RequestContext = new() { Authorizer = new() { Claims = new() { ["sub"] = "owner" } } }
    };
}
