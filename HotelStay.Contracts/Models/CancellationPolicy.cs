namespace HotelStay.Contracts.Models;

public record CancellationPolicy(CancellationPolicyType Type, int? HoursBeforeCheckIn);