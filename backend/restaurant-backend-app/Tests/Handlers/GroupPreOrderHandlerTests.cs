using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers;
using APIHandler.Handlers.GroupReservations;
using APIHandler.Models.GroupReservation;
using APIHandler.Services.GroupReservations;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers;

public class GroupPreOrderHandlerTests
{
    private readonly Mock<IGroupPreOrderService> _service = new();
    private readonly ILambdaContext _context = Mock.Of<ILambdaContext>(c => c.Logger == Mock.Of<ILambdaLogger>());

    [Fact]
    public async Task OwnerSessionResponseUsesGuestSessionIdField()
    {
        _service.Setup(s => s.GetOwnerSessionIdAsync("owner", "reservation"))
            .ReturnsAsync(new GetOwnerSessionIdResponse("owner-session-123"));

        var response = await Handler("lookup").HandleRequest(Request(), _context);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("owner-session-123", json.RootElement.GetProperty("guestSessionId").GetString());
    }

    [Fact]
    public async Task SubmitParsesReservationIdAndReturnsOnlyDishIdAndQuantity()
    {
        _service.Setup(s => s.SubmitAsync("owner", "group", "reservation"))
            .ReturnsAsync(new[] { new ConsolidatedDishResponse("dish1", 3), new ConsolidatedDishResponse("dish2", 2) });

        var response = await Handler("submit").HandleRequest(Request(), _context);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.Equal("dish1", json.RootElement[0].GetProperty("dishId").GetString());
        Assert.Equal(3, json.RootElement[0].GetProperty("quantity").GetInt32());
        Assert.Equal(2, json.RootElement[0].EnumerateObject().Count());
        Assert.Equal("dish2", json.RootElement[1].GetProperty("dishId").GetString());
        Assert.Equal(2, json.RootElement[1].GetProperty("quantity").GetInt32());
    }

    [Theory]
    [InlineData("lookup")]
    [InlineData("submit")]
    public async Task MissingAuthenticationReturns401(string operation)
    {
        var request = Request();
        request.RequestContext = null;
        var response = await Handler(operation).HandleRequest(request, _context);
        Assert.Equal(401, response.StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("lookup")]
    [InlineData("submit")]
    public async Task MissingPathParameterReturns400(string operation)
    {
        var request = Request();
        request.PathParameters = null;
        var response = await Handler(operation).HandleRequest(request, _context);
        Assert.Equal(400, response.StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"reservationId\":null}")]
    [InlineData("{\"reservationId\":\" \"}")]
    public async Task InvalidSubmitBodyReturns400WithoutCallingService(string body)
    {
        var request = Request();
        request.Body = body;
        var response = await Handler("submit").HandleRequest(request, _context);
        Assert.Equal(400, response.StatusCode);
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("lookup", "reservation", 404)]
    [InlineData("lookup", "group", 404)]
    [InlineData("lookup", "session", 404)]
    [InlineData("lookup", "owner", 403)]
    [InlineData("submit", "reservation", 404)]
    [InlineData("submit", "group", 404)]
    [InlineData("submit", "owner", 403)]
    [InlineData("submit", "validation", 400)]
    public async Task DomainErrorsMapToHomeworkStatusCodes(string operation, string failure, int expectedStatus)
    {
        Exception error = failure switch
        {
            "reservation" => new ReservationNotFoundException("Missing reservation"),
            "group" => new GroupReservationNotFoundException(),
            "session" => new GuestSessionNotFoundException("Missing session"),
            "owner" => new ActionForbiddenException("Owner only"),
            _ => new ValidationException("No submitted sessions, dishes or order")
        };
        _service.Setup(s => s.GetOwnerSessionIdAsync("owner", "reservation")).ThrowsAsync(error);
        _service.Setup(s => s.SubmitAsync("owner", "group", "reservation")).ThrowsAsync(error);

        var response = await Handler(operation).HandleRequest(Request(), _context);
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    private IApiRequestHandler Handler(string operation) => operation == "lookup"
        ? new GetOwnerSessionIdHandler(_service.Object) : new SubmitGroupPreOrderHandler(_service.Object);

    private static APIGatewayProxyRequest Request() => new()
    {
        PathParameters = new Dictionary<string, string>
        {
            ["reservationId"] = "reservation", ["groupReservationId"] = "group"
        },
        RequestContext = new() { Authorizer = new() { Claims = new() { ["sub"] = "owner" } } },
        Body = "{\"reservationId\":\"reservation\"}"
    };
}
