# Hotel Stay — README

A concise guide to run, test, and inspect the Hotel Stay sample (API + Blazor Server UI). This README preserves the original content and appends the items newly added to this solution.

Prerequisites
- .NET SDK 8.x or 9.x installed. Verify:
  ```bash
  dotnet --list-sdks
  ```
- Recommended: Visual Studio 2022/2023 or VS Code.

Repository layout
- HotelStay.Api — minimal Web API (endpoints: GET /hotels/search, POST /hotels/reserve, GET /hotels/reservation/{reference})
- HotelStay.UI — Blazor Server UI that calls the API
- HotelStay.Contracts — shared DTOs / enums (single source of truth)
- HotelStay.Tests — xUnit tests (integration + unit)

Quick start (command line)
1. Restore and build:
   ```bash
   dotnet restore
   dotnet build
   ```

2. Run the API:
   ```bash
   dotnet run --project HotelStay.Api
   ```
   - The console will show listen URLs (Kestrel). The API enables a permissive CORS policy to allow the UI to call it.

3. In a second terminal, run the Blazor UI:
   ```bash
   dotnet run --project HotelStay.UI
   ```
   - Open the URL shown for the UI process in your browser and use the site to search and reserve.

Run API + UI from your IDE
- Set both `HotelStay.Api` and `HotelStay.UI` as startup projects (multiple startup projects) in Visual Studio, or launch them separately in VS Code using two terminals / launch profiles.

Run the test suite
- From repository root:
  ```bash
  dotnet test
  ```

- Unit tests
  - Unit tests added under `HotelStay.Tests/UnitTests` covering provider mapping, pricing calculation and city-classification rules.
- Integration tests
   - Integration tests uses `WebApplicationFactory<Program>` to host the API in-memory. The API project must expose a `public partial class Program { }` for the test host to discover the app entry point.

Important testing notes / troubleshooting
- Do not force the test host environment to `Development` (for example, avoid setting `ASPNETCORE_ENVIRONMENT=Development` for the in-memory test runs). The Developer Exception Page attempts to serialize exception details using a TestServer pipe writer that can cause a 500 with a "PipeWriter does not implement UnflushedBytes" error. Use the default environment the test host provides.
- If a test fails, re-run with:
  ```bash
  dotnet test --logger "console;verbosity=detailed"
  ```
  and copy the response body printed by failing assertions for debugging.
- If the UI cannot reach the API, ensure both projects run on reachable ports and that the API's CORS policy is enabled (the API registers an AllowAll policy by default).

Spec / business rules (summary)
- JSON uses PascalCase property names.
- Room types: Standard, Deluxe, Suite.
- City classification:
  - Domestic: Delhi, Bangalore
  - International: Paris, Tokyo, New York, London
  - International destinations require DocumentType = Passport; otherwise POST /hotels/reserve returns 422 with message: "Passport is required for international destination '{City}'."
- GET /hotels/search:
  - Required query params: destination, checkIn (yyyy-MM-dd), checkOut (yyyy-MM-dd).
  - 400 for missing/unparseable params or when checkOut <= checkIn.
  - Optional roomType string must be Standard|Deluxe|Suite (400 if invalid).
  - Uses `IMemoryCache` with a TTL to cache aggregated search results.
- POST /hotels/reserve:
  - Accepts ReservationRequest; server re-validates document/destination classification.
  - 404 if OfferId cannot be resolved to a registered provider.
  - On success returns ReservationResult with ReferenceNumber like `HS-XXXXXXXX` and persists into an in-memory store (ConcurrentDictionary).
- GET /hotels/reservation/{reference} returns 404 if unknown.

Extra tips
- To change ports, set ASPNETCORE_URLS or configure launch profiles in your IDE.
- Provider implementations are deterministic fixtures; to add a provider implement `IHotelProvider` and add DI registration in `Program.cs`.
- The sample intentionally keeps reservations in-memory (no DB); restarting the API clears reservations.
