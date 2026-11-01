using System.Text.Json;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Mappers;
using Xunit;

namespace APIHandler.Tests.Mappers;

public class OrderMapperStateTests
{
    [Theory]
    [InlineData(OrderItemState.Pending, 0)]
    [InlineData(OrderItemState.Confirmed, 1)]
    [InlineData(OrderItemState.Canceled, 2)]
    public void PreservesDishStateAsNumericJson(OrderItemState state, int expected)
    {
        var order = new Order
        {
            OrderId = "order-1",
            CustomerId = "customer-1",
            ReservationId = "reservation-1",
            ReservationAddress = "Rustaveli Avenue 48",
            ReservationTime = "16:00 - 17:30",
            Dishes = [new OrderItem { DishId = "dish-1", Quantity = 2, State = state }],
        };
        var dish = new Dish { Id = "dish-1", Name = "Spring Salad", Price = 12, LocationId = "location-1" };

        var response = OrderMapper.ToOrderResponse(order, [dish]);
        var item = Assert.Single(response.OrderItems);
        Assert.Equal(expected, item.State);
        Assert.Equal(2, item.Quantity);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));
        var serializedState = json.RootElement.GetProperty("orderItems")[0].GetProperty("state");
        Assert.Equal(JsonValueKind.Number, serializedState.ValueKind);
        Assert.Equal(expected, serializedState.GetInt32());
    }
}
