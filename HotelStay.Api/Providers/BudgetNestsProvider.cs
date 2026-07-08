using HotelStay.Contracts.Models;

namespace HotelStay.Api.Providers;

public class BudgetNestsProvider : IHotelProvider
{
    public string Name => "BudgetNests";

    public Task<IReadOnlyList<RoomOffer>> SearchAsync(HotelSearchQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        int nights = Math.Max(0, query.CheckOut.DayNumber - query.CheckIn.DayNumber);
        if (nights == 0)
        {
            return Task.FromResult((IReadOnlyList<RoomOffer>)Array.Empty<RoomOffer>());
        }

        var dest = (query.Destination ?? string.Empty).Trim().ToLowerInvariant();

        // internal fixture entries include an availability flag to simulate missing offers
        IEnumerable<(string nativeId, RoomType roomType, decimal rate, bool available, bool detailed)> fixtures =
            dest switch
            {
                "delhi" => new[]
                {
                    ("BN-DEL-STD-1", RoomType.Standard, 80m, true, false),
                    ("BN-DEL-DLX-1", RoomType.Deluxe, 130m, true, false),
                    // unavailable suite to exercise filter-out
                    ("BN-DEL-SUI-1", RoomType.Suite, 240m, false, false)
                },
                "bangalore" => new[]
                {
                    ("BN-BAN-STD-1", RoomType.Standard, 60m, true, false),
                    // no deluxe/suite offered by BudgetNests in Manchester (fewer rooms)
                },
                "paris" => new[]
                {
                    ("BN-PAR-STD-1", RoomType.Standard, 110m, true, false),
                    ("BN-PAR-DLX-1", RoomType.Deluxe, 170m, false, false) // unavailable
                },
                "tokyo" => new[]
                {
                    ("BN-TYO-STD-1", RoomType.Standard, 100m, true, false),
                    ("BN-TYO-DLX-1", RoomType.Deluxe, 160m, true, false)
                },
                "new york" => new[]
                {
                    ("BN-NYC-STD-1", RoomType.Standard, 120m, true, false),
                    // intentionally omit suite to model limited inventory
                },
               "london" => new[]
               {
                    ("BN-LON-STD-1", RoomType.Standard, 850m, true, false),
                },
                _ => Array.Empty<(string, RoomType, decimal, bool, bool)>()
            };

        var offers = fixtures
            .Where(f => f.available)
            .Where(f => query.RoomType is null || query.RoomType == f.roomType)
            .Select(f =>
            {
                var total = f.rate * nights;
                // BudgetNests returns minimal detail tier: null Amenities and StarRating
                return new RoomOffer(
                    OfferId: $"{Name}:{f.nativeId}",
                    Provider: Name,
                    RoomType: f.roomType,
                    RatePerNight: f.rate,
                    TotalPrice: total,
                    Cancellation: new CancellationPolicy(CancellationPolicyType.Flexible, 24),
                    Amenities: null,
                    StarRating: null
                );
            })
            .OrderBy(o => o.TotalPrice)
            .ToArray();

        return Task.FromResult((IReadOnlyList<RoomOffer>)offers);
    }
}