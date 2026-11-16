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
// Dirección de la API. Si viene vacía, la aplicación habla con su propio origen: así funciona
// tanto publicada dentro del wwwroot de la API como detrás de la pasarela nginx del despliegue
// en contenedores, donde /api se reenvía al servicio de la API y no hace falta CORS.
var apiUrl = builder.Configuration["ApiUrl"];
if (string.IsNullOrWhiteSpace(apiUrl)) apiUrl = builder.HostEnvironment.BaseAddress;
builder.Services.AddSingleton<Sesion>();

// Resiliencia del cliente frente a la red y a la indisponibilidad de la API (tolerancia a
// fallos): tiempo de espera, reintentos con retirada exponencial solo en operaciones
// idempotentes, e interruptor de circuito compartido por toda la aplicación.
var politica = new PoliticaResiliencia();
builder.Services.AddSingleton(politica);
builder.Services.AddSingleton<InterruptorCircuito>();
builder.Services.AddScoped(sp => new HttpClient(new ManejadorResiliencia(politica, sp.GetRequiredService<InterruptorCircuito>()))
{
    BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"),
    // El tiempo de espera real lo impone el manejador por intento; aquí se deja un tope global
    // holgado para que no corte un reintento legítimo.
    Timeout = TimeSpan.FromSeconds(60),
});
builder.Services.AddScoped<ApiClient>();

await builder.Build().RunAsync();
