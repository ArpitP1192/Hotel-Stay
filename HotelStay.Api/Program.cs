using System.Globalization;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using HotelStay.Contracts.Models;
using HotelStay.Api.Providers;
using HotelStay.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// CORS: allow any origin/method/header
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// Preserve PascalCase in JSON
builder.Services.ConfigureHttpJsonOptions(opt => opt.SerializerOptions.PropertyNamingPolicy = null);

// Register providers as singletons
builder.Services.AddSingleton<IHotelProvider, PremierStaysProvider>();
builder.Services.AddSingleton<IHotelProvider, BudgetNestsProvider>();

// Register memory cache and the search service via interface
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IHotelSearchService, HotelSearchService>();

var app = builder.Build();

app.UseCors("AllowAll");

var internationalCities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Paris", "Tokyo", "New York","London" };
var domesticCities = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Bangalore", "Delhi" };

// GET /hotels/search
app.MapGet("/hotels/search", async (HttpContext http, IHotelSearchService searchService, CancellationToken ct) =>
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
    var aggregated = await searchService.SearchAsync(query, ct);
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

    // Document/destination validation
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
    var nativeId = parts[1];

    // body provider must match OfferId prefix
    if (!string.Equals(req.Provider, providerNameFromId, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Provider in request body does not match OfferId provider." });

    var provider = providers.FirstOrDefault(p => string.Equals(p.Name, providerNameFromId, StringComparison.OrdinalIgnoreCase));
    if (provider is null)
        return Results.NotFound(new { error = $"Provider '{providerNameFromId}' not recognized." });

    // Use provided RoomType from the body
    var providedRoomType = req.RoomType;

    CancellationPolicy cancellation = provider.Name.Equals("PremierStays", StringComparison.OrdinalIgnoreCase)
        ? new CancellationPolicy(CancellationPolicyType.FreeCancellation, 48)
        : new CancellationPolicy(CancellationPolicyType.Flexible, 24);

    // Generate HS-XXXXXXXX reference
    var reference = "HS-" + GenerateReference(8);

    var result = new ReservationResult(
        ReferenceNumber: reference,
        ReservedAt: DateTime.UtcNow,
        Provider: provider.Name,
        RoomType: providedRoomType,
        TotalPrice: req.TotalPrice,
        Cancellation: cancellation,
        GuestName: req.GuestName
    );

    // Persist into the required store
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
public partial class Program { }