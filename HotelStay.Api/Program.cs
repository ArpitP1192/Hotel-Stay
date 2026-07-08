using System.Globalization;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using HotelStay.Api.Models;
using HotelStay.Api.Providers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IHotelProvider, PremierStaysProvider>();
builder.Services.AddSingleton<IHotelProvider, BudgetNestsProvider>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.PropertyNamingPolicy = null;
});
var app = builder.Build();

app.UseCors("AllowAll");

var internationalCities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Paris", "Tokyo", "New York","London" };
var domesticCities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Bangalore", "Delhi" };

app.MapGet("/hotels/search", async (HttpContext http, IEnumerable<IHotelProvider> providers) =>
{
    var q = http.Request.Query;
    var destination = q["destination"].ToString();
    var checkInRaw = q["checkIn"].ToString();
    var checkOutRaw = q["checkOut"].ToString();
    var roomTypeRaw = q["roomType"].ToString();

    if (string.IsNullOrWhiteSpace(destination) ||
        string.IsNullOrWhiteSpace(checkInRaw) ||
        string.IsNullOrWhiteSpace(checkOutRaw))
    {
        return Results.BadRequest(new { error = "destination, checkIn and checkOut are required." });
    }

    if (!DateOnly.TryParse(checkInRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var checkIn) ||
        !DateOnly.TryParse(checkOutRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var checkOut))
    {
        return Results.BadRequest(new { error = "checkIn or checkOut could not be parsed as a date (expected yyyy-MM-dd)." });
    }

    if (checkOut <= checkIn)
    {
        return Results.BadRequest(new { error = "checkOut must be after checkIn." });
    }

    RoomType? roomType = null;
    if (!string.IsNullOrWhiteSpace(roomTypeRaw))
    {
        if (!Enum.TryParse<RoomType>(roomTypeRaw, ignoreCase: true, out var rt))
            return Results.BadRequest(new { error = "roomType must be one of Standard, Deluxe, Suite." });

        roomType = rt;
    }

    var query = new HotelSearchQuery(destination, checkIn, checkOut, roomType);

    var tasks = providers.Select(p => p.SearchAsync(query, default));
    var results = await Task.WhenAll(tasks);

    var aggregated = results.SelectMany(r => r).OrderBy(o => o.TotalPrice).ToArray();

    // --- cache each returned offer so /hotels/reserve can verify against it later ---
    foreach (var offer in aggregated)
    {
        OfferCache.Offers[offer.OfferId] = offer;
    }

    return Results.Ok(aggregated);
});

app.MapPost("/hotels/reserve", (ReservationRequest req, IEnumerable<IHotelProvider> providers) =>
{
    if (req is null)
        return Results.BadRequest(new { error = "Request body is required." });

    if (string.IsNullOrWhiteSpace(req.OfferId))
        return Results.BadRequest(new { error = "OfferId is required." });

    if (string.IsNullOrWhiteSpace(req.Provider))
        return Results.BadRequest(new { error = "Provider is required." });

    if (internationalCities.Contains(req.Destination))
    {
        if (req.DocumentType != DocumentType.Passport)
        {
            var msg = $"Passport is required for international destination '{req.Destination}'.";
            return Results.UnprocessableEntity(new { error = msg });
        }
    }
    else if (!domesticCities.Contains(req.Destination))
    {
        return Results.BadRequest(new { error = $"Unknown destination '{req.Destination}'." });
    }

    var parts = req.OfferId.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 2)
        return Results.NotFound(new { error = "OfferId did not resolve to a known provider/offer." });

    var providerNameFromId = parts[0];

    if (!string.Equals(req.Provider, providerNameFromId, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Provider in request body does not match OfferId provider." });

    var provider = providers.FirstOrDefault(p => string.Equals(p.Name, providerNameFromId, StringComparison.OrdinalIgnoreCase));
    if (provider is null)
        return Results.NotFound(new { error = $"Provider '{providerNameFromId}' not recognized." });

    // --- resolve the real offer from the search cache instead of trusting the client, it will Block the UI temper attacks ---
    if (!OfferCache.Offers.TryGetValue(req.OfferId, out var matchedOffer))
    {
        return Results.NotFound(new { error = $"OfferId '{req.OfferId}' was not found. Please search again — offers expire." });
    }

    var reference = "HS-" + GenerateReference(8);

    var result = new ReservationResult(
        Reference: reference,
        ReservedAt: DateTime.UtcNow,
        Provider: provider.Name,
        RoomType: matchedOffer.RoomType,        // server-verified, not client-supplied
        TotalPrice: matchedOffer.TotalPrice,     // server-verified, not client-supplied
        Cancellation: matchedOffer.Cancellation, // server-verified, not client-supplied
        GuestName: req.GuestName
    );

    ReservationStore.Reservations[reference] = result;
    return Results.Ok(result);
});

app.MapGet("/hotels/reservation/{reference}", (string reference) =>
{
    if (ReservationStore.Reservations.TryGetValue(reference, out var res))
        return Results.Ok(res);

    return Results.NotFound(new { error = "Reservation not found." });
});

app.Run();

static string GenerateReference(int length)
{
    const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    Span<char> buffer = stackalloc char[length];
    var bytes = RandomNumberGenerator.GetBytes(length);
    for (int i = 0; i < length; i++)
        buffer[i] = alphabet[bytes[i] % alphabet.Length];
    return new string(buffer);
}

// Data store (must be placed below app.Run())
static class ReservationStore
{
    public static readonly ConcurrentDictionary<string, ReservationResult> Reservations = new();
}

// Offer cache — lets /hotels/reserve verify price/room type/cancellation against
// what /hotels/search actually returned, without needing dates on ReservationRequest.
static class OfferCache
{
    public static readonly ConcurrentDictionary<string, RoomOffer> Offers = new();
}
public partial class Program { }