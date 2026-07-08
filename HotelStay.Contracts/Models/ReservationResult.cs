namespace HotelStay.Contracts.Models;

public record ReservationResult(
    string ReferenceNumber,
    DateTime ReservedAt,
    string Provider,
    RoomType RoomType,
    decimal TotalPrice,
    CancellationPolicy Cancellation,
    string GuestName
);