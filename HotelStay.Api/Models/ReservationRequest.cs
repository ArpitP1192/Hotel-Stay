namespace HotelStay.Api.Models;
public record ReservationRequest(
    string OfferId,
    string GuestName,
    DocumentType DocumentType,
    string DocumentNumber,
    string Destination,
    decimal TotalPrice,
    string Provider,
    RoomType RoomType
);