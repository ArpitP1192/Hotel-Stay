# Development Prompts Log

This document tracks the sequence of prompts used to build the Hotel Stay Availability Engine, structured by development phase.

## Phase 0: Project Foundation & Documentation
> **Prompt:** You are an engineer/assistant implementing the Hotel Stay service described by spec.md. Follow the spec exactly.
Goal
- Implement a .NET 8 web API + Blazor UI that conforms to spec.md: domain model, provider abstraction, deterministic providers, endpoints, validations and in-memory reservation store.
Key rules (must follow)
- Domain types: RoomType, CancellationPolicyType, DocumentType, RoomOffer, ReservationRequest, ReservationResult (use PascalCase in JSON).
- OfferId format: "{Provider}:{NativeId}".
- City directory (document validation): 
  - Domestic: Delhi, Bangalore
  - International: Paris, Tokyo, New York, London
  - Rule: International requires Passport; otherwise return 422 with message: "Passport is required for international destination '{City}'."
- GET /hotels/search:
  - Required query params: destination, checkIn, checkOut
  - 400 if missing/unparseable or if checkOut <= checkIn
  - Optional roomType string: Standard|Deluxe|Suite (400 if invalid)
  - Return RoomOffer[] sorted by TotalPrice
- POST /hotels/reserve:
  - Accept ReservationRequest body
  - 422 if document/destination mismatch
  - 404 if OfferId cannot be resolved to a registered provider
  - Return ReservationResult with ReferenceNumber format HS-XXXXXXXX when success (200)
- GET /hotels/reservation/{reference}: 404 if unknown; otherwise 200 with ReservationResult
- Providers: implement IHotelProvider, deterministic fixtures, map provider-specific payloads to RoomOffer; skip offers with mapping errors or available=false
- Persistence: in-memory ConcurrentDictionary for reservations only (no DB)
- JSON: preserve PascalCase property names

Acceptance tests (examples)
1) GET /hotels/search?destination=London&checkIn=2026-07-07&checkOut=2026-07-07 -> 400
2) POST /hotels/reserve with Destination="Tokyo" and DocumentType=NationalId -> 422 and message: "Passport is required for international destination 'Tokyo'."
3) POST /hotels/reserve with Destination="Delhi" and DocumentType=NationalId -> 200 and JSON with ReferenceNumber starting "HS-"

Deliverables
- API implementation (Program.cs / minimal APIs or controllers)
- Two provider implementations and DI registration
- Blazor UI that uses the same city list client-side for validation
- Unit/integration tests exercising the above rules

If anything in spec.md is ambiguous, ask one targeted question before implementing.

> **Prompt:** Based on the implemented architecture, write a `README.md` that provides clear instructions on how to set up the environment, run the API, run the Blazor UI, and execute the test suite.

> **Prompt:** Write a `reflection.md` document. Analyze the current implementation in terms of production readiness. Discuss the limitations of the current in-memory storage, the benefits of the Blazor Server architecture, and how validation logic could be abstracted.

## Phase 1: Domain Modeling & Data Contract
> **Prompt:** Based strictly on the specifications outlined in spec.md, generate the domain models inside the HotelStay.Api/Models folder. CRITICAL RULE: Do NOT put them in a single file. Create a separate .cs file for every single enum and record. Use file-scoped namespaces (namespace HotelStay.Api.Models;) and C# 12 record syntax.
> Enums: RoomType, CancellationPolicyType, DocumentType.
> Records: CancellationPolicy, RoomOffer, ReservationRequest (must include decimal TotalPrice), and ReservationResult (must include string Reference and DateTime ReservedAt).

## Phase 2: Provider Logic & Business Services
> **Prompt:** Based on spec.md, create the provider logic inside HotelStay.Api/Providers. CRITICAL RULES:
> Create HotelSearchQuery.cs (Destination, CheckIn, CheckOut, RoomType).
> Create IHotelProvider.cs matching the interface in the spec (using CancellationToken).
> Create PremierStaysProvider.cs and BudgetNestsProvider.cs. Both MUST use a deterministic switch statement on the Destination (London, Manchester, Paris, Tokyo, New York) to return specific arrays of available RoomTypes. BudgetNests must model missing availability by returning fewer rooms. PremierStays must calculate TotalPrice and use FreeCancellation.

## Phase 3: Minimal API Implementation
> **Prompt:** Based strictly on spec.md, rewrite HotelStay.Api/Program.cs.
> CRITICAL ARCHITECTURE RULES:
> Setup: Add CORS policy (AllowAnyOrigin/Method/Header). Configure JSON options: builder.Services.ConfigureHttpJsonOptions(opt => opt.SerializerOptions.PropertyNamingPolicy = null); (Keeps PascalCase).
> Endpoints: Implement GET /hotels/search, POST /hotels/reserve (enforcing the 422 document/destination validation rules from the spec), and GET /hotels/reservation/{reference}. Place these mappings BEFORE app.Run();.
> The Data Store: Below app.Run();, add this exact block: C# static class ReservationStore { public static readonly ConcurrentDictionary<string, ReservationResult> Reservations = new(); } public partial class Program { } Integration: Ensure the POST endpoint saves the ReservationResult into ReservationStore.Reservations[reference] = result; and uses the HS-XXXXXXXX reference format as defined in spec.md.

## Phase 4: Blazor UI Integration
> **Prompt:** For UI we will be implementing blazor UI,So I need this Blazor Server app to communicate with the Minimal API. Add builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://localhost:5000") }); CRITICAL RULE: Do NOT delete, move, or modify the existing Blazor routing! Ensure app.MapRazorComponents<App>().AddInteractiveServerRenderMode(); remains intact at the bottom of the file.

## Phase 5: Frontend Logic & Component State
> **Prompt:** I am building the frontend using @rendermode InteractiveServer. Inject HttpClient. CRITICAL RULES: State: Use native DateOnly properties for CheckInDate and CheckOutDate bound directly to <input type="date">. Do not use strings or TryParse. Models: Define local C# records for RoomOffer and ReservationRequest to match the API. Search: Build a method hitting GET /hotels/search and render the results using PascalCase variable names. Book: Build a form for Guest Name, DocumentType, and DocumentNumber. The POST payload MUST include Destination and TotalPrice from the selected offer. Handle 422 errors gracefully and display the HS-XXXXXXXX reference on success. Lookup: Build a method hitting GET /hotels/reservation/{reference} to display stored details. Seperate the logic of code from razor page and c#, logic, create a new razor.cs file and add those c# functionality in that.

## Phase 6: UI/UX Grid Refinement
> **Prompt:** I can see the UI of blazor razor is not good, please make Hotel Search section grid wise, Results section is ok but please make a scroll bar in that, and Booking section should also be in grid mode like section of 3 or 4 grid columns, Lookup Reservation also make this in grid columns too.

## Phase 7: Interactive Modals
> **Prompt:** Need a popup, so i want to get the confirmation details before sending it to the api actually instead of just displayed the information just like a normal text ,in popup it should asked confirmation like something "Are you sure want to confirm the details" and below their details -reference number, provider, total price, cancellation policy , then if confirm then it will be submit the book , cancel button will only close the box. 
> [Follow-up:] Please add a popup in razor page when we get the reference number in return after confirming the room booking instead of just displayed the information just like a normal text, we will utilize a popup here, in the popup only ok button will be there, when we close it will be removed.

## Phase 8: Integration Testing
> **Prompt:** Write xUnit integration tests using WebApplicationFactory<Program> testing the exact business rules in spec.md: Search with bad dates -> 400. Reserve Tokyo with NationalId -> 422. Reserve London with NationalId -> 200. Write one additional test of "app.MapGet("/hotels/search"," to be positive. 
> [Follow-up:] Please add two more test case of app.MapGet("/hotels/reservation/{reference} scenarios should be positive and one negative.