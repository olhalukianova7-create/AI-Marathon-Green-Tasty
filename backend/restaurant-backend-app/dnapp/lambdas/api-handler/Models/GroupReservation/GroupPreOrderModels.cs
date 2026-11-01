using System.ComponentModel.DataAnnotations;

namespace APIHandler.Models.GroupReservation;

public record GetOwnerSessionIdResponse(string GuestSessionId);

public record ConsolidatedDishResponse(string DishId, int Quantity);

public class SubmitGroupPreOrderRequest
{
    [Required]
    public string ReservationId { get; set; } = string.Empty;
}
