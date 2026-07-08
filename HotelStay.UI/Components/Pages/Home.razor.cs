using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using HotelStay.Contracts.Models;
using Microsoft.AspNetCore.Components;

namespace HotelStay.UI.Components.Pages;

public partial class Home : ComponentBase
{
    protected DateOnly CheckInDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    protected DateOnly CheckOutDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
    protected string Destination { get; set; } = "London";
    protected string SelectedRoomTypeString { get; set; } = string.Empty;
    protected List<RoomOffer>? Offers { get; set; }
    protected RoomOffer? SelectedOffer { get; set; }
    protected RoomOffer? BookedOfferSnapshot { get; set; }

    // BookingModel now is a mutable view-model used for binding in the UI.
    protected BookingViewModel BookingModel { get; set; } = new BookingViewModel();
    protected string? BookingError;
    protected string? BookingReference;
    protected bool ShowBookingModal; // success popup visibility
    protected bool ShowConfirmModal; // confirmation modal visibility

    // Lookup
    protected string LookupReference { get; set; } = "";
    protected ReservationResult? FoundReservation;
    protected string? LookupError;

    // Search error (visible in the search UI)
    protected string? SearchError;

    // UI sort: asc/desc for TotalPrice
    protected bool SortDescending { get; set; } = false;

    // Client-side city classification used for pre-flight validation
    private static readonly HashSet<string> InternationalCities = new(StringComparer.OrdinalIgnoreCase) { "Paris", "Tokyo", "New York", "London" };
    private static readonly HashSet<string> DomesticCities = new(StringComparer.OrdinalIgnoreCase) { "Delhi", "Bangalore" };

    [Inject] protected HttpClient Http { get; set; } = default!;

    // Mutable view-model used by the UI to bind inputs.
    public class BookingViewModel
    {
        public string OfferId { get; set; } = "";
        public string GuestName { get; set; } = "";
        public DocumentType DocumentType { get; set; } = DocumentType.Passport;
        public string DocumentNumber { get; set; } = "";
        public string Destination { get; set; } = "";
        public decimal TotalPrice { get; set; } = 0m;
        public string Provider { get; set; } = "";
        public RoomType RoomType { get; set; } = RoomType.Standard;
    }

    protected void OnSortChanged(ChangeEventArgs e)
    {
        var v = e?.Value?.ToString();
        SortDescending = string.Equals(v, "desc", StringComparison.OrdinalIgnoreCase);
        ApplySort();
    }

    private void ApplySort()
    {
        if (Offers is null) return;
        Offers = SortDescending
            ? Offers.OrderByDescending(o => o.TotalPrice).ToList()
            : Offers.OrderBy(o => o.TotalPrice).ToList();
        StateHasChanged();
    }

    protected async Task SearchAsync()
    {
        // clear state
        BookingError = null;
        BookingReference = null;
        ShowBookingModal = false;
        ShowConfirmModal = false;
        FoundReservation = null;
        LookupError = null;
        SearchError = null;

        if (string.IsNullOrWhiteSpace(Destination))
        {
            Offers = new List<RoomOffer>();
            SearchError = "Destination is required.";
            StateHasChanged();
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (CheckInDate < today)
        {
            Offers = new List<RoomOffer>();
            SearchError = "Check-in cannot be in the past.";
            StateHasChanged();
            return;
        }

        if (CheckOutDate <= CheckInDate)
        {
            Offers = new List<RoomOffer>();
            SearchError = "Check-out must be after check-in.";
            StateHasChanged();
            return;
        }

        if (!string.IsNullOrWhiteSpace(SelectedRoomTypeString) &&
            !Enum.TryParse<RoomType>(SelectedRoomTypeString, ignoreCase: true, out _))
        {
            Offers = new List<RoomOffer>();
            SearchError = "RoomType must be one of Standard, Deluxe, Suite.";
            StateHasChanged();
            return;
        }

        var checkIn = CheckInDate.ToString("yyyy-MM-dd");
        var checkOut = CheckOutDate.ToString("yyyy-MM-dd");

        var url = $"/hotels/search?destination={Uri.EscapeDataString(Destination)}&checkIn={checkIn}&checkOut={checkOut}";
        if (!string.IsNullOrWhiteSpace(SelectedRoomTypeString))
            url += $"&roomType={Uri.EscapeDataString(SelectedRoomTypeString)}";

        try
        {
            var result = await Http.GetFromJsonAsync<List<RoomOffer>>(url);
            Offers = result ?? new List<RoomOffer>();
            ApplySort();
        }
        catch (Exception ex)
        {
            Offers = new List<RoomOffer>();
            SearchError = $"Search failed: {ex.Message}";
        }
        StateHasChanged();
    }

    protected void SelectOffer(RoomOffer offer)
    {
        SelectedOffer = offer;
        BookingModel = new BookingViewModel
        {
            OfferId = offer.OfferId,
            GuestName = "",
            DocumentType = DocumentType.Passport,
            DocumentNumber = "",
            Destination = Destination,
            TotalPrice = offer.TotalPrice,
            Provider = offer.Provider,
            RoomType = offer.RoomType
        };
        BookingError = null;
        BookingReference = null;
        ShowBookingModal = false;
        ShowConfirmModal = false;
    }

    protected Task OpenConfirmationAsync()
    {
        BookingError = null;

        if (string.IsNullOrWhiteSpace(BookingModel.GuestName))
        {
            BookingError = "Guest name is required.";
            StateHasChanged();
            return Task.CompletedTask;
        }
        if (string.IsNullOrWhiteSpace(BookingModel.DocumentNumber))
        {
            BookingError = "Document number is required.";
            StateHasChanged();
            return Task.CompletedTask;
        }
        if (string.IsNullOrWhiteSpace(BookingModel.OfferId))
        {
            BookingError = "No offer selected.";
            StateHasChanged();
            return Task.CompletedTask;
        }

        // Pre-flight document validation (UI): if destination is international require Passport
        if (!string.IsNullOrWhiteSpace(BookingModel.Destination) &&
            InternationalCities.Contains(BookingModel.Destination) &&
            BookingModel.DocumentType != DocumentType.Passport)
        {
            BookingError = $"Passport is required for international destination '{BookingModel.Destination}'.";
            StateHasChanged();
            return Task.CompletedTask;
        }

        ShowConfirmModal = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    protected async Task ConfirmAndSubmitAsync()
    {
        BookingError = null;
        ShowConfirmModal = false;
        try
        {
            // Map mutable view-model to immutable Contracts ReservationRequest
            var request = new ReservationRequest(
                OfferId: BookingModel.OfferId,
                GuestName: BookingModel.GuestName,
                DocumentType: BookingModel.DocumentType,
                DocumentNumber: BookingModel.DocumentNumber,
                Destination: BookingModel.Destination,
                TotalPrice: BookingModel.TotalPrice,
                Provider: BookingModel.Provider,
                RoomType: BookingModel.RoomType
            );

            var resp = await Http.PostAsJsonAsync("/hotels/reserve", request);
            if (resp.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var payload = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                if (payload != null && payload.TryGetValue("error", out var msg))
                    BookingError = msg;
                else
                    BookingError = "Document validation failed.";
                StateHasChanged();
                return;
            }
            if (!resp.IsSuccessStatusCode)
            {
                BookingError = $"Booking failed: {resp.StatusCode}";
                StateHasChanged();
                return;
            }
            var reservation = await resp.Content.ReadFromJsonAsync<ReservationResult>();
            if (reservation is not null)
            {
                BookingReference = reservation.ReferenceNumber;
                // capture snapshot of the offer that was booked so success modal can show details
                BookedOfferSnapshot = SelectedOffer;
                ShowBookingModal = true;
                BookingModel = new BookingViewModel();
                SelectedOffer = null;
            }
        }
        catch (Exception ex)
        {
            BookingError = $"Booking failed: {ex.Message}";
        }
        StateHasChanged();
    }

    protected void CancelBooking()
    {
        SelectedOffer = null;
        BookingError = null;
        BookingReference = null;
        ShowBookingModal = false;
        ShowConfirmModal = false;
    }
    protected void CloseBookingModal()
    {
        ShowBookingModal = false;
        BookingReference = null; // remove the reference when modal is closed
        BookedOfferSnapshot = null; // clear snapshot
        StateHasChanged();
    }

    protected void CloseConfirmModal()
    {
        ShowConfirmModal = false;
        StateHasChanged();
    }

    protected async Task LookupAsync()
    {
        LookupError = null;
        FoundReservation = null;

        if (string.IsNullOrWhiteSpace(LookupReference))
        {
            LookupError = "Reference Number is required.";
            return;
        }

        try
        {
            var resp = await Http.GetAsync($"/hotels/reservation/{Uri.EscapeDataString(LookupReference)}");
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                LookupError = "Reservation not found.";
                return;
            }
            resp.EnsureSuccessStatusCode();
            var reservation = await resp.Content.ReadFromJsonAsync<ReservationResult>();
            FoundReservation = reservation;
        }
        catch (Exception ex)
        {
            LookupError = $"Lookup failed: {ex.Message}";
        }
        StateHasChanged();
    }
}