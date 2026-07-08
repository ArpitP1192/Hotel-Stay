using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using HotelStay.Contracts.Models;
using HotelStay.Api.Providers;

namespace HotelStay.Api.Services;

public class HotelSearchService : IHotelSearchService
{
    private readonly IEnumerable<IHotelProvider> _providers;
    private readonly IMemoryCache _cache;
    private readonly MemoryCacheEntryOptions _cacheOptions;

    public HotelSearchService(IEnumerable<IHotelProvider> providers, IMemoryCache cache)
    {
        _providers = providers;
        _cache = cache;
        // cache TTL: 5 minutes (adjust per load)
        _cacheOptions = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };
    }

    private static string MakeKey(string destination, DateOnly checkIn, DateOnly checkOut, RoomType? roomType) =>
        $"{destination}|{checkIn:yyyy-MM-dd}|{checkOut:yyyy-MM-dd}|{roomType?.ToString() ?? "any"}";

    public async Task<IReadOnlyList<RoomOffer>> SearchAsync(HotelSearchQuery q, CancellationToken ct)
    {
        var key = MakeKey(q.Destination, q.CheckIn, q.CheckOut, q.RoomType);
        if (_cache.TryGetValue<IReadOnlyList<RoomOffer>>(key, out var cached))
            return cached;

        var tasks = _providers.Select(p => p.SearchAsync(q, ct));
        var results = await Task.WhenAll(tasks);
        var aggregated = results.SelectMany(r => r).OrderBy(o => o.TotalPrice).ToArray();

        _cache.Set(key, aggregated, _cacheOptions);
        return aggregated;
    }
}