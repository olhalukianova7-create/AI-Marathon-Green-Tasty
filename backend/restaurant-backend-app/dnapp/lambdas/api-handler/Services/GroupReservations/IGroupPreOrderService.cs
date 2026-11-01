using APIHandler.Models.GroupReservation;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Services.GroupReservations;

public interface IGroupPreOrderService
{
    Task<GetOwnerSessionIdResponse> GetOwnerSessionIdAsync(string userId, string reservationId);
    Task<IReadOnlyList<ConsolidatedDishResponse>> SubmitAsync(string userId, string groupReservationId, string reservationId);
}
