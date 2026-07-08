using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HotelStay.Tests;
public class HotelStayTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = null /* preserve PascalCase */ };

    public HotelStayTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Search_London_Returns200_AndOffers()
    {
        using var client = _factory.CreateClient();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var checkIn = today;
        var checkOut = today.AddDays(1);

        var url = $"/hotels/search?destination=London&checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}";

        var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var offers = await resp.Content.ReadFromJsonAsync<List<JsonElement>>(_jsonOptions);
        Assert.NotNull(offers);
        Assert.NotEmpty(offers);
    }

    [Fact]
    public async Task Search_WithBadDates_Returns400()
    {
        using var client = _factory.CreateClient();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var checkIn = today;
        var checkOut = today;
        var url = $"/hotels/search?destination=London&checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}";
        var resp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
    
    [Fact]
    public async Task Reserve_Tokyo_WithNationalId_Returns422()
    {
        using var client = _factory.CreateClient();

        var req = new
        {
            OfferId = "PremierStays:TYO-STD-1",
            GuestName = "Test Guest",
            DocumentType = (int)1, // NationalId
            DocumentNumber = "NI-12345",
            Destination = "Tokyo",
            TotalPrice = 100.0m,
            Provider = "PremierStays",
            RoomType = (int)0
        };

        var content = new StringContent(JsonSerializer.Serialize(req, _jsonOptions), Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/hotels/reserve", content);

        Assert.Equal((HttpStatusCode)422, resp.StatusCode);

        var payload = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(payload);
        Assert.True(payload.TryGetValue("error", out var msg));
        Assert.Contains("Passport is required", msg);
    }

    [Fact]
    public async Task Reserve_Domestic_Bangalore_WithNationalId_Returns200_AndReferenceNumberGetting()
    {
        using var client = _factory.CreateClient();

        // Search first so a real offer exists in OfferCache
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var checkIn = today;
        var checkOut = today.AddDays(1);
        var searchUrl = $"/hotels/search?destination=Bangalore&checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}";

        var searchResp = await client.GetAsync(searchUrl);
        Assert.Equal(HttpStatusCode.OK, searchResp.StatusCode);

        var offers = await searchResp.Content.ReadFromJsonAsync<List<JsonElement>>(_jsonOptions);
        Assert.NotNull(offers);
        Assert.NotEmpty(offers);

        var firstOffer = offers[0];
        var offerId = firstOffer.GetProperty("OfferId").GetString();
        var provider = firstOffer.GetProperty("Provider").GetString();

        var req = new
        {
            OfferId = offerId,           // real, cached OfferId — required
            GuestName = "Test Guest",
            DocumentType = (int)1,       // NationalId
            DocumentNumber = "NI-67890",
            Destination = "Bangalore",
            TotalPrice = 999.0m,         // deliberately wrong — server must ignore this
            Provider = provider,
            RoomType = (int)0
        };

        var content = new StringContent(JsonSerializer.Serialize(req, _jsonOptions), Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/hotels/reserve", content);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var reservation = await resp.Content.ReadFromJsonAsync<ReservationResult>(_jsonOptions);
        Assert.NotNull(reservation);
        Assert.False(string.IsNullOrWhiteSpace(reservation.Reference));
        Assert.StartsWith("HS-", reservation.Reference);
        Assert.NotEqual(999.0m, reservation.TotalPrice); // proves tampered price was overridden
    }

    [Fact]
    public async Task GetReservation_AfterReserve_Domestic_DelhiRoom_Returns200_AndMatchesReference()
    {
        using var client = _factory.CreateClient();

        // Search first so the offer is added to OfferCache before we reserve it.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var checkIn = today;
        var checkOut = today.AddDays(1);
        var searchUrl = $"/hotels/search?destination=Delhi&checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}";

        var searchResp = await client.GetAsync(searchUrl);
        Assert.Equal(HttpStatusCode.OK, searchResp.StatusCode);

        var offers = await searchResp.Content.ReadFromJsonAsync<List<JsonElement>>(_jsonOptions);
        Assert.NotNull(offers);
        Assert.NotEmpty(offers);

        var firstOffer = offers[0];
        var offerId = firstOffer.GetProperty("OfferId").GetString();
        var provider = firstOffer.GetProperty("Provider").GetString();

        var req = new
        {
            OfferId = offerId,
            GuestName = "Lookup Guest",
            DocumentType = (int)1,
            DocumentNumber = "NI-00001",
            Destination = "Delhi",
            TotalPrice = 1.0m, // deliberately wrong — server must ignore this
            Provider = provider,
            RoomType = (int)0
        };

        var content = new StringContent(JsonSerializer.Serialize(req, _jsonOptions), Encoding.UTF8, "application/json");
        var postResp = await client.PostAsync("/hotels/reserve", content);
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);

        var created = await postResp.Content.ReadFromJsonAsync<ReservationResult>(_jsonOptions);
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created.Reference));

        var getResp = await client.GetAsync($"/hotels/reservation/{Uri.EscapeDataString(created.Reference)}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var fetched = await getResp.Content.ReadFromJsonAsync<ReservationResult>(_jsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal(created.Reference, fetched.Reference);
        Assert.Equal(created.Provider, fetched.Provider);
        Assert.Equal(created.GuestName, fetched.GuestName);
    }

    [Fact]
    public async Task GetReservation_UnknownReference_Returns404()
    {
        using var client = _factory.CreateClient();

        var getResp = await client.GetAsync($"/hotels/reservation/HS-UNKNOWN1");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    // Minimal local type to deserialize the API response
    private record ReservationResult(
        string Reference,
        DateTime ReservedAt,
        string Provider,
        int RoomType,
        decimal TotalPrice,
        object Cancellation,
        string GuestName
    );
}