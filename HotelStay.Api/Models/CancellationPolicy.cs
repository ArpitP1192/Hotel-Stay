namespace HotelStay.Api.Models;
public record CancellationPolicy(
    CancellationPolicyType Type,
    int? HoursBeforeCheckIn
);