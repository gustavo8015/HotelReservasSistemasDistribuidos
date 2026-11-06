using HotelReservas.Web;
using HotelReservas.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// Hotel Reservas - aplicación web (Blazor WebAssembly).
// Es la versión web de la app móvil .NET MAUI: mismas pantallas (Inicio, Reservas, Habitaciones,
// Huéspedes, Reportes) y el prototipo funcional, pero ejecutándose en el navegador y consumiendo
// la API REST HotelReservas.Api por HTTPS + JWT (misma arquitectura en capas de la sección 4).
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// URL de la API: wwwroot/appsettings.json ("ApiUrl"); por defecto http://localhost:5080
var apiUrl = builder.Configuration["ApiUrl"] ?? "http://localhost:5080";
builder.Services.AddSingleton<Sesion>();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/") });
builder.Services.AddScoped<ApiClient>();

await builder.Build().RunAsync();
