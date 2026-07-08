using HotelStay.Api.Models;

namespace HotelStay.Api.Providers;

public interface IHotelProvider
{
    string Name { get; }
    Task<IReadOnlyList<RoomOffer>> SearchAsync(HotelSearchQuery query, CancellationToken ct);
}
