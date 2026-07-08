namespace HotelStay.Api.Models;
public record RoomOffer(
    string OfferId,
    string Provider,
    RoomType RoomType,
    decimal RatePerNight,
    decimal TotalPrice,
    CancellationPolicy Cancellation,
    string? Amenities,
    int? StarRating
);