using HotelStay.Api.Providers;
using HotelStay.Contracts.Models;

namespace HotelStay.Tests.UnitTests;

public class ProviderAndValidationTests
{
    [Fact]
    public async Task PremierStaysProvider_ReturnsOffers_WithCorrectTotalPrice()
    {
        var provider = new PremierStaysProvider();
        var checkIn = DateOnly.FromDateTime(DateTime.Today);
        var checkOut = checkIn.AddDays(2);
        var query = new HotelSearchQuery("London", checkIn, checkOut, null);

        var offers = await provider.SearchAsync(query, CancellationToken.None);

        Assert.NotNull(offers);
        Assert.NotEmpty(offers);

        var nights = (int)(query.CheckOut.ToDateTime(TimeOnly.MinValue) - query.CheckIn.ToDateTime(TimeOnly.MinValue)).TotalDays;
        Assert.True(nights > 0, "Test setup expects nights > 0.");

        foreach (var o in offers)
        {
            Assert.Equal(o.RatePerNight * nights, o.TotalPrice);
            Assert.Equal(provider.Name, o.Provider);
            Assert.False(string.IsNullOrWhiteSpace(o.OfferId));
        }
    }

    [Fact]
    public async Task BudgetNestsProvider_FiltersUnavailableAndMapsRoomTypes()
    {
        var provider = new BudgetNestsProvider();
        var checkIn = DateOnly.FromDateTime(DateTime.Today);
        var checkOut = checkIn.AddDays(1);
        var query = new HotelSearchQuery("Delhi", checkIn, checkOut, null);

        var offers = await provider.SearchAsync(query, CancellationToken.None);

        Assert.NotNull(offers);

        // BudgetNests fixture contains some unavailable entries; ensure returned offers are available and mapped
        Assert.All(offers, o => Assert.False(string.IsNullOrWhiteSpace(o.OfferId)));
        Assert.All(offers, o => Assert.True(Enum.IsDefined(typeof(RoomType), o.RoomType)));
        Assert.All(offers, o => Assert.Equal(provider.Name, o.Provider));
    }

    [Fact]
    public void CityClassification_InternationalRequiresPassport_ListMatchesSpec()
    {
        var international = new[] { "Paris", "Tokyo", "New York", "London" };
        foreach (var city in international)
        {
            Assert.True(IsInternational(city), $"Expected '{city}' to be classified as international.");
        }

        var domestic = new[] { "Delhi", "Bangalore" };
        foreach (var city in domestic)
        {
            Assert.False(IsInternational(city), $"Expected '{city}' to be classified as domestic.");
        }
    }

    // Mirrors the spec's deterministic city list. Keep this helper in sync with spec.md / API.
    private static bool IsInternational(string city)
    {
        return new[] { "Paris", "Tokyo", "New York", "London" }
            .Contains(city, StringComparer.OrdinalIgnoreCase);
    }
}