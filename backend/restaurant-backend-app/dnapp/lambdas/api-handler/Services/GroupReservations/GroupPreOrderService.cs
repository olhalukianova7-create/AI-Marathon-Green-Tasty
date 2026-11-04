using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Services.GroupReservations;

public class GroupPreOrderService(
    IReservationRepository reservationRepository,
    IGroupReservationRepository groupReservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IOrderRepository orderRepository) : IGroupPreOrderService
{
    public async Task<GetOwnerSessionIdResponse> GetOwnerSessionIdAsync(string userId, string reservationId)
    {
        await GetOwnedReservationAsync(userId, reservationId);
        var group = await groupReservationRepository.GetByReservationIdAsync(reservationId)
            ?? throw new GroupReservationNotFoundException();
        var sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId);
        var ownerSession = sessions.FirstOrDefault(s => s.IsOwnerSession && s.OwnerUserId == userId);
        if (ownerSession is null)
        {
            // A customer's existing individual pre-order is their group basket.
            // Groups with no owner pre-order retain the existing 404 contract.
            var order = await orderRepository.GetOrderByReservationIdAsync(reservationId);
            if (group.Status != GroupReservationStatus.Open || order is null
                || order.CustomerId != userId || order.Dishes.Count == 0
                || order.Status is not (OrderStatus.Created or OrderStatus.SubmittedByCustomer))
                throw new GuestSessionNotFoundException("Owner session was not found.");

            // Stable identity prevents duplicate owner entries if two reads race.
            ownerSession = new GuestSession
            {
                Id = $"owner-{group.GroupReservationId}",
                GroupReservationId = group.GroupReservationId,
                GuestName = "Owner",
                IsOwnerSession = true,
                OwnerUserId = userId,
                IsSubmitted = order.Status == OrderStatus.SubmittedByCustomer,
                Dishes = order.Dishes.Select(d => new OrderItem
                {
                    DishId = d.DishId, Quantity = d.Quantity, State = d.State
                }).ToList()
            };
            await guestSessionRepository.SaveAsync(ownerSession);
        }

        return new GetOwnerSessionIdResponse(ownerSession.Id);
    }

    public async Task<IReadOnlyList<ConsolidatedDishResponse>> SubmitAsync(string userId, string groupReservationId, string reservationId)
    {
        await GetOwnedReservationAsync(userId, reservationId);
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        if (group.ReservationId != reservationId)
            throw new ValidationException("Group reservation does not belong to the specified reservation.");
        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();
        if (group.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();

        var sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId);
        var submittedSessions = sessions.Where(s => s.IsSubmitted).ToArray();
        if (submittedSessions.Length == 0)
            throw new ValidationException("There are no submitted guest sessions.");

        var dishes = submittedSessions.SelectMany(s => s.Dishes).ToArray();
        if (dishes.Length == 0)
            throw new ValidationException("Submitted guest sessions contain no dishes.");
        if (dishes.Any(d => string.IsNullOrWhiteSpace(d.DishId) || d.Quantity <= 0))
            throw new ValidationException("Each submitted dish must have an ID and a positive quantity.");

        var consolidated = dishes.GroupBy(d => d.DishId)
            .Select(g => new ConsolidatedDishResponse(g.Key, g.Sum(d => d.Quantity)))
            .ToArray();
        var order = await orderRepository.GetOrderByReservationIdAsync(reservationId)
            ?? throw new ValidationException("Order was not found for this reservation.");
        if (order.CustomerId != userId || order.ReservationId != reservationId)
            throw new ValidationException("Order does not belong to the reservation owner.");
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Submitted)
            throw new ValidationException("Order can no longer be submitted by the customer.");

        // Rebuild the snapshot instead of appending, so a retry after a failed group save
        // cannot add the same guest dishes to the order twice.
        order.Dishes = consolidated.Select(d => new OrderItem
        {
            DishId = d.DishId,
            Quantity = d.Quantity,
            State = OrderItemState.Pending
        }).ToList();
        order.Status = OrderStatus.SubmittedByCustomer;
        await orderRepository.SaveOrderAsync(order);

        group.Status = GroupReservationStatus.Submitted;
        await groupReservationRepository.SaveGroupReservationAsync(group);

        return consolidated;
    }

    private async Task<Reservation> GetOwnedReservationAsync(string userId, string reservationId)
    {
        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation '{reservationId}' was not found.");
        if (string.IsNullOrWhiteSpace(userId) || reservation.CustomerId != userId)
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");
        return reservation;
    }
}
