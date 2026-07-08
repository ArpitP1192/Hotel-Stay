# Spec — Hotel Stay Availability

This document is committed before any implementation code, per the challenge requirements.
It defines the domain model, the provider abstraction, and the API contract. Implementation
must conform to this spec; if reality forces a deviation, this file is updated in the same
commit as the code change.

## 1. Domain Model

### RoomType (unified enum)
```csharp
public enum RoomType
{
    Standard,
    Deluxe,
    Suite
}
```
Both providers expose these three types under different names/casing; each provider's client
maps its own room type strings onto this enum. Unknown/unrecognized values from a provider are
treated as a mapping error (logged, offer skipped) rather than silently defaulted, so bad data
never masquerades as a real offer.

### CancellationPolicy (unified enum)
```csharp
public enum CancellationPolicyType
{
    FreeCancellation,   // PremierStays: "FreeCancellation up to 48h before check-in"
    Flexible,           // BudgetNests: "Flexible up to 24h before check-in"
    NonRefundable        // both providers
}
```
```csharp
public record CancellationPolicy(
    CancellationPolicyType Type,
    int? HoursBeforeCheckIn // null when NonRefundable
);
```

### RoomOffer (unified, provider-agnostic — what /hotels/search returns)
```csharp
public record RoomOffer(
    string OfferId,          // synthetic id: "{provider}:{originalId}", used at reservation time
    string Provider,         // "PremierStays" | "BudgetNests"
    RoomType RoomType,
    decimal RatePerNight,
    decimal TotalPrice,      // RatePerNight * nights
    CancellationPolicy Cancellation,
    string? Amenities,       // null for BudgetNests (minimal detail tier)
    int? StarRating          // null for BudgetNests (minimal detail tier)
);
```
`OfferId` is a composite of provider name + the provider's native id, so `/hotels/reserve` can
route back to the correct provider without a persistence layer.

### DocumentType
```csharp
public enum DocumentType { Passport, NationalId }
```

### Reservation
```csharp
public record ReservationRequest(
    string OfferId,
    string GuestName,
    DocumentType DocumentType,
    string DocumentNumber,
    string Destination, // needed server-side to re-validate document requirement,
    decimal TotalPrice,  
    string Provider,
    RoomType RoomType);
);

public record ReservationResult(
    string ReferenceNumber,
    string Provider,
    RoomType RoomType,
    decimal TotalPrice,
    CancellationPolicy Cancellation,
    string GuestName
);
```

## 2. City Directory (document validation)

Hard-coded, deterministic list (extensible later to config/DB without changing the contract):

| City | Classification |
|---|---|
| Delhi | Domestic |
| Bangalore | Domestic |
| Paris | International |
| Tokyo | International |
| New York | International |
| London | International |

Rule:
- Domestic destination → `NationalId` or `Passport` both accepted
- International destination → `Passport` required; `NationalId` → rejected

Validation happens twice:
1. **Client-side** (Blazor): disables/warns before submit, using the same city list (duplicated
   as a small constant in the UI project — acceptable duplication for a project this size; noted
   in reflection.md as a thing to unify via a shared contracts project if this grew).
2. **Server-side** (authoritative): `POST /hotels/reserve` re-validates regardless of what the
   client sent. Mismatch → `422 Unprocessable Entity` with a clear message, e.g.:
   `"Passport is required for international destination 'Tokyo'."`

## 3. Provider Abstraction

```csharp
public interface IHotelProvider
{
    string Name { get; }
    Task<IReadOnlyList<RoomOffer>> SearchAsync(HotelSearchQuery query, CancellationToken ct);
}

public record HotelSearchQuery(string Destination, DateOnly CheckIn, DateOnly CheckOut, RoomType? RoomType);
```
Two DI-registered implementations: `PremierStaysProvider`, `BudgetNestsProvider`. Both:
- Return their own stub payload shape (PascalCase / snake_case) internally
- Map to `RoomOffer` before returning from `SearchAsync`
- Are deterministic: same query → same offers, seeded from an in-memory fixture keyed by
  destination + room type (no randomness, no wall-clock dependence beyond the nights calculation)
- `BudgetNestsProvider` includes some fixture rows with `"available": false` to exercise the
  filter-out requirement

Adding a third provider means writing one new class implementing `IHotelProvider` and adding one
DI registration line — `HotelSearchService` and the endpoint layer are provider-count-agnostic
(they just iterate `IEnumerable<IHotelProvider>`).

## 4. API Contract

### `GET /hotels/search`
Query params: `destination` (string, required), `checkIn` (date, required), `checkOut` (date,
required), `roomType` (string, optional — `Standard|Deluxe|Suite`).

- 400 if `destination`, `checkIn`, or `checkOut` missing/unparseable
- 400 if `checkOut <= checkIn`
- 200 with `RoomOffer[]` (possibly empty array) otherwise, sorted by `TotalPrice` ascending by
  default (frontend can re-sort client-side)

### `POST /hotels/reserve`
Body: `ReservationRequest`.
- 422 with message on document/destination mismatch
- 404 if `OfferId` doesn't resolve to a known provider/offer
- 200 with `ReservationResult` (includes generated `ReferenceNumber`, format `HS-XXXXXXXX`)

### `GET /hotels/reservation/{reference}`
- 404 if unknown reference
- 200 with `ReservationResult` otherwise

Reservations are stored in-memory (`ConcurrentDictionary`) for the lifetime of the process —
no persistence, per scope.

## 5. Non-goals (explicit scope boundary)
No auth, no real persistence, no real provider calls, no payment. This mirrors the challenge's
"Scope" section and is repeated here so implementation doesn't quietly grow beyond it.
