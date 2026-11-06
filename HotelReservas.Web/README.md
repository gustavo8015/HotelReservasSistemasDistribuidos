# HotelReservas.Web – aplicación web (Blazor WebAssembly)

Versión web de la aplicación móvil .NET MAUI del sistema Hotel Reservas. Conserva las mismas pantallas del `AppShell` de la app (Inicio, Reservas, Habitaciones, Huéspedes, Reportes) y el prototipo funcional, pero se ejecuta en el navegador y consume la API REST `HotelReservas.Api` con token JWT, respetando la arquitectura en capas de la sección 4 del documento maestro.

| Pantalla MAUI (`Views/`) | Página web (`Pages/`) | Qué hace en la versión web |
|---|---|---|
| InicioPage | `Inicio.razor` (`/`) | Encabezado, propósito, estado de hoy (desde la API), módulos y arquitectura en capas |
| ReservasPage | `Reservas.razor` (`/reservas`) | Huésped por documento (RF18), disponibilidad (CU-03), confirmación transaccional con IVA y precio neto (CU-04/05/06, RF16), comprobante por WhatsApp (RF17) |
| HabitacionesPage | `Habitaciones.razor` (`/habitaciones`) | Inventario por tipo con estado para un intervalo de fechas |
| HuespedesPage | `Huespedes.razor` (`/huespedes`) | Búsqueda por documento y registro de huéspedes (CU-02) |
| ReportesPage | `Reportes.razor` (`/reportes`) | Historial, indicadores, detalle con pagos, comprobante y bitácora (RNF05), check-in / check-out / cancelar con ROWVERSION |
| PrototipoPage (WebView) | `Prototipo.razor` (`/prototipo`) | El mismo `prototipo.html` de la APK, en un iframe (simulación local) |
| — | `Asistente.razor` (`/asistente`) | Servicio de IA de la sección 13 (`POST /api/asistente`) |
| — | `Login.razor` (`/login`) | CU-01: inicio de sesión y token JWT |

## Cómo ejecutar

1. Base de datos: scripts `database/01` a `06` en SSMS.
2. API: en `HotelReservas.Api/` ejecutar `dotnet run` (queda en `http://localhost:5080`; la API ya permite CORS desde cualquier origen).
3. Web: en `HotelReservas.Web/` ejecutar `dotnet run` (o F5 en Visual Studio 2022 con la carga de trabajo "ASP.NET y desarrollo web"). Se abre en `http://localhost:5180`.
4. Iniciar sesión con `recepcion1@hotel.com` / `Hotel2026*` (script 06).

Si la API está en otra dirección, cambie `ApiUrl` en `wwwroot/appsettings.json`. Para publicar como sitio estático: `dotnet publish -c Release` y copiar `bin/Release/net8.0/publish/wwwroot` a cualquier servidor web (IIS, Nginx, Azure Static Web Apps).

Para agregar el proyecto a la solución: `dotnet sln HotelReservas.sln add HotelReservas.Web/HotelReservas.Web.csproj`.

## Estructura

- `Program.cs` – arranque, `HttpClient` apuntando a la API, servicios `Sesion` y `ApiClient`.
- `Services/ApiClient.cs` – un método por recurso de la API (Tabla 6 del documento); traduce 409 / 404 / 401 a mensajes.
- `Models/Dtos.cs` – contratos JSON de la API.
- `Layout/MainLayout.razor` – barra lateral (equivalente al TabBar de MAUI; en móvil pasa a pestañas inferiores). `RequiereSesion.razor` protege las páginas.
- `wwwroot/css/app.css` – misma paleta que `Resources/Styles/Colors.xaml` de la app móvil.
- `wwwroot/prototipo.html` – prototipo funcional (copia del asset de la APK).

Nota: el código fue revisado estáticamente; compílelo con el SDK de .NET 8 (`dotnet build`) y, si aparece algún aviso, corríjalo o repórtelo.
