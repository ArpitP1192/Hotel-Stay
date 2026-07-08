using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotelStay.Contracts.Models;
using HotelStay.Api.Providers;

namespace HotelStay.Api.Services;

public interface IHotelSearchService
{
    Task<IReadOnlyList<RoomOffer>> SearchAsync(HotelSearchQuery query, CancellationToken ct);
}