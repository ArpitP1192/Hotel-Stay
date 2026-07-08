using System;
namespace HotelStay.Api.Models;

public record ReservationResult(
    string Reference,
    DateTime ReservedAt,
    string Provider,
    RoomType RoomType,
    decimal TotalPrice,
    CancellationPolicy Cancellation,
    string GuestName
);