using HotelStay.Contracts.Models;

namespace HotelStay.Api.Providers;

public class PremierStaysProvider : IHotelProvider
{
    public string Name => "PremierStays";

    public Task<IReadOnlyList<RoomOffer>> SearchAsync(HotelSearchQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        int nights = Math.Max(0, query.CheckOut.DayNumber - query.CheckIn.DayNumber);
        if (nights == 0)
        {
            // return empty list when no nights (validation layer should prevent this)
            return Task.FromResult((IReadOnlyList<RoomOffer>)Array.Empty<RoomOffer>());
        }

        var dest = (query.Destination ?? string.Empty).Trim().ToLowerInvariant();

        // deterministic switch on destination to control which room types and base rates are offered
        IEnumerable<(string nativeId, RoomType roomType, decimal rate, string amenities, int star)> fixtures =
            dest switch
            {
                "london" => new[]
                {
                    ("LON-SUI-1", RoomType.Suite, 400m, "Free WiFi;Breakfast;Lounge access", 5)
                },
                "delhi" => new[]
                {
                    ("DEL-STD-1", RoomType.Standard, 120m, "Free WiFi;Breakfast", 4),
                    ("DEL-DLX-1", RoomType.Deluxe, 180m, "Free WiFi;Breakfast;River view", 4),
                    ("DEL-SUI-1", RoomType.Suite, 300m, "Free WiFi;Breakfast;Lounge access", 5)
                },
                "bangalore" => new[]
                {
                    ("BAN-STD-1", RoomType.Standard, 90m, "Free WiFi;Breakfast", 3),
                    ("BAN-DLX-1", RoomType.Deluxe, 140m, "Free WiFi;Breakfast;City view", 4)
                },
                "paris" => new[]
                {
                    ("PAR-STD-1", RoomType.Standard, 150m, "Free WiFi;Breakfast", 4),
                    ("PAR-DLX-1", RoomType.Deluxe, 220m, "Free WiFi;Breakfast;Eiffel view", 5),
                    ("PAR-SUI-1", RoomType.Suite, 420m, "Free WiFi;Breakfast;Rooftop terrace", 5)
                },
                "tokyo" => new[]
                {
                    ("TYO-STD-1", RoomType.Standard, 130m, "Free WiFi;Breakfast", 4),
                    ("TYO-DLX-1", RoomType.Deluxe, 200m, "Free WiFi;Breakfast;City view", 5)
                },
                "new york" => new[]
                {
                    ("NYC-STD-1", RoomType.Standard, 170m, "Free WiFi;Breakfast", 4),
                    ("NYC-DLX-1", RoomType.Deluxe, 260m, "Free WiFi;Breakfast;City skyline", 5),
                    ("NYC-SUI-1", RoomType.Suite, 520m, "Free WiFi;Breakfast;Butler service", 5)
                },
                _ => Array.Empty<(string, RoomType, decimal, string, int)>()
            };

        var offers = fixtures
            .Where(f => query.RoomType is null || query.RoomType == f.roomType)
            .Select(f =>
            {
                var total = f.rate * nights;
                return new RoomOffer(
                    OfferId: $"{Name}:{f.nativeId}",
                    Provider: Name,
                    RoomType: f.roomType,
                    RatePerNight: f.rate,
                    TotalPrice: total,
                    Cancellation: new CancellationPolicy(CancellationPolicyType.FreeCancellation, 48),
                    Amenities: f.amenities,
                    StarRating: f.star
                );
            })
            .OrderBy(o => o.TotalPrice)
            .ToArray();

        return Task.FromResult((IReadOnlyList<RoomOffer>)offers);
    }
}