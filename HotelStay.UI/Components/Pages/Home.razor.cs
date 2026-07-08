using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace HotelStay.UI.Components.Pages;

public partial class Home : ComponentBase
{
    protected DateOnly CheckInDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    protected DateOnly CheckOutDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddDays(1);

    protected string Destination { get; set; } = "London";
    protected string SelectedRoomTypeString { get; set; } = string.Empty;

    // Results (PascalCase variable names)
    protected List<RoomOffer>? Offers { get; set; }
    protected RoomOffer? SelectedOffer { get; set; }

    // Booking state
    protected ReservationRequest BookingModel { get; set; } = new ReservationRequest
    {
        OfferId = "",
        GuestName = "",
        DocumentType = DocumentType.Passport,
        DocumentNumber = "",
        Destination = "",
        TotalPrice = 0m,
        Provider = "",
        RoomType = RoomType.Standard
    };
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

    [Inject] protected HttpClient Http { get; set; } = default!;

    // Local models matching API
    public enum RoomType { Standard, Deluxe, Suite }
    public enum DocumentType { Passport, NationalId }
    public enum CancellationPolicyType { FreeCancellation, Flexible, NonRefundable }

    public record CancellationPolicy(CancellationPolicyType Type, int? HoursBeforeCheckIn);

    public record RoomOffer(
        string OfferId,
        string Provider,
        RoomType RoomType,
        decimal RatePerNight,
        decimal TotalPrice,
        CancellationPolicy Cancellation,
        string? Amenities,
        int? StarRating
    );

    public class ReservationRequest
    {
        public string OfferId { get; set; }
        public string GuestName { get; set; }
        public DocumentType DocumentType { get; set; }
        public string DocumentNumber { get; set; } // <-- Change 'init' to 'set'
        public string Destination { get; set; }
        public decimal TotalPrice { get; set; }
        public string Provider { get; set; }
        public RoomType RoomType { get; set; }
    }

    public record ReservationResult(
        string Reference,
        DateTime ReservedAt,
        string Provider,
        RoomType RoomType,
        decimal TotalPrice,
        CancellationPolicy Cancellation,
        string GuestName
    );

    // Search
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
            if (Offers.Count == 0)
                SearchError = "No rooms available.";
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
        BookingModel = new ReservationRequest
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
            var resp = await Http.PostAsJsonAsync("/hotels/reserve", BookingModel);
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
                BookingReference = reservation.Reference;
                ShowBookingModal = true; 
                BookingModel = new ReservationRequest
                {
                    OfferId = "",
                    GuestName = "",
                    DocumentType = DocumentType.Passport,
                    DocumentNumber = "",
                    Destination = "",
                    TotalPrice = 0m,
                    Provider = "",
                    RoomType = RoomType.Standard
                };
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
            LookupError = "Reference is required.";
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