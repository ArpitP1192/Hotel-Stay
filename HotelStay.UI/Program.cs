using System;
using System.Net.Http;
using HotelStay.UI.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Make API base address configurable via appsettings / env: ApiBaseUrl
var apiBase = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7119";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBase) });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
