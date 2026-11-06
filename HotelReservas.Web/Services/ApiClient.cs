using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HotelReservas.Web.Models;

namespace HotelReservas.Web.Services;

/// <summary>Estado de la sesión del usuario (token JWT y perfil), compartido por todas las páginas.</summary>
public class Sesion
{
    public string? Token { get; private set; }
    public string Nombre { get; private set; } = "";
    public string Rol { get; private set; } = "";
    /// <summary>Ficha de huésped ligada a la cuenta cuando el perfil es CLIENTE (script 07).</summary>
    public int? IdHuesped { get; private set; }
    public string? Documento { get; private set; }
    public bool Activa => !string.IsNullOrEmpty(Token);
    /// <summary>true cuando el rol del token es ADMINISTRADOR: operación del hotel y módulo de administración.</summary>
    public bool EsAdministrador => Activa && Rol == "ADMINISTRADOR";
    /// <summary>true cuando el rol del token es CLIENTE: usuario final que reserva desde el portal.</summary>
    public bool EsCliente => Activa && Rol == "CLIENTE";
    public event Action? Cambio;

    public void Iniciar(RespuestaLogin r)
    { Token = r.Token; Nombre = r.Nombre; Rol = r.Rol; IdHuesped = r.IdHuesped; Documento = r.Documento; Cambio?.Invoke(); }
    public void Cerrar()
    { Token = null; Nombre = ""; Rol = ""; IdHuesped = null; Documento = null; Cambio?.Invoke(); }
}

/// <summary>Error devuelto por la API con su código HTTP (409 = conflicto de concurrencia, 404, 400...).</summary>
public class ApiException : Exception
{
    public HttpStatusCode Estado { get; }
    public ApiException(HttpStatusCode estado, string mensaje) : base(mensaje) => Estado = estado;
}

/// <summary>Cliente de HotelReservas.Api. Cada método corresponde a un recurso de la Tabla 6 del documento.</summary>
public class ApiClient
{
    private readonly HttpClient _http;
    private readonly Sesion _sesion;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ApiClient(HttpClient http, Sesion sesion) { _http = http; _sesion = sesion; }

    public string BaseUrl => _http.BaseAddress?.ToString().TrimEnd('/') ?? "";

    private HttpRequestMessage Req(HttpMethod m, string ruta, object? cuerpo = null)
    {
        var r = new HttpRequestMessage(m, ruta.TrimStart('/'));
        if (_sesion.Token != null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _sesion.Token);
        if (cuerpo != null) r.Content = JsonContent.Create(cuerpo, options: Json);
        return r;
    }

    private async Task<T> Enviar<T>(HttpRequestMessage req)
    {
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req); }
        catch (HttpRequestException) { throw new ApiException(HttpStatusCode.ServiceUnavailable, $"No fue posible conectar con la API en {BaseUrl}. Verifique que HotelReservas.Api esté en ejecución."); }
        var texto = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            string msg = $"La API respondió {(int)resp.StatusCode}.";
            try { var e = JsonSerializer.Deserialize<ErrorApi>(texto, Json); if (!string.IsNullOrEmpty(e?.Mensaje)) msg = e.Mensaje; } catch { }
            if (resp.StatusCode == HttpStatusCode.Unauthorized) msg = "Sesión no válida o vencida. Inicie sesión de nuevo.";
            if (resp.StatusCode == HttpStatusCode.Forbidden) msg = "Operación reservada al rol ADMINISTRADOR (HTTP 403).";
            throw new ApiException(resp.StatusCode, msg);
        }
        if (typeof(T) == typeof(string)) return (T)(object)texto;
        try { return JsonSerializer.Deserialize<T>(texto, Json)!; }
        catch (JsonException)
        {
            // Un cuerpo que empieza por "<" es una página HTML, no datos: casi siempre significa
            // que la dirección consultada no corresponde a ninguna ruta del servicio.
            var mensaje = texto.TrimStart().StartsWith("<")
                ? $"El servicio devolvió una página web en lugar de datos. Verifique que la API esté respondiendo en {BaseUrl} y que la consulta lleve todos sus datos."
                : "El servicio devolvió una respuesta que no se pudo interpretar.";
            throw new ApiException(resp.StatusCode, mensaje);
        }
    }

    // CU-01
    public async Task<RespuestaLogin> LoginAsync(string correo, string clave)
    {
        var r = await Enviar<RespuestaLogin>(Req(HttpMethod.Post, "api/auth/login", new SolicitudLogin(correo, clave)));
        _sesion.Iniciar(r); return r;
    }
    // CU-03 / RF16
    public Task<List<HabitacionDisponible>> DisponibilidadAsync(string tipo, DateOnly entrada, DateOnly salida, bool exento = false)
        => Enviar<List<HabitacionDisponible>>(Req(HttpMethod.Get, $"api/disponibilidad?tipo={Uri.EscapeDataString(tipo)}&entrada={entrada:yyyy-MM-dd}&salida={salida:yyyy-MM-dd}&exentoIva={exento.ToString().ToLower()}"));
    // CU-04/05/06
    public Task<ResultadoConfirmacion> ConfirmarAsync(SolicitudReserva s) => Enviar<ResultadoConfirmacion>(Req(HttpMethod.Post, "api/reservas", s));
    // CU-12
    public Task<List<ReservaResumen>> ReservasAsync(string? estado = null) => Enviar<List<ReservaResumen>>(Req(HttpMethod.Get, "api/reservas" + (estado == null ? "" : "?estado=" + estado)));
    public Task<ReservaDetalle> ReservaAsync(string codigo) => Enviar<ReservaDetalle>(Req(HttpMethod.Get, $"api/reservas/{codigo}"));
    // CU-08/09/10
    public Task<string> CambiarEstadoAsync(string codigo, string accion, string? versionFila) => Enviar<string>(Req(HttpMethod.Put, $"api/reservas/{codigo}/{accion}", new { versionFila, detalle = (string?)null }));
    // CU-02 / RF18
    public Task<HuespedPerfil> HuespedPorDocumentoAsync(string documento) => Enviar<HuespedPerfil>(Req(HttpMethod.Get, "api/huespedes?documento=" + Uri.EscapeDataString(documento)));
    public Task<string> CrearHuespedAsync(NuevoHuesped n) => Enviar<string>(Req(HttpMethod.Post, "api/huespedes", n));
    // RF17
    public Task<ComprobanteCompartir> CompartirAsync(string numero) => Enviar<ComprobanteCompartir>(Req(HttpMethod.Get, $"api/comprobantes/{numero}/compartir"));
    // CU-15 Gestionar usuarios (solo ADMINISTRADOR)
    public Task<List<UsuarioResumen>> UsuariosAsync() => Enviar<List<UsuarioResumen>>(Req(HttpMethod.Get, "api/usuarios"));
    public Task<string> CrearUsuarioAsync(NuevoUsuario n) => Enviar<string>(Req(HttpMethod.Post, "api/usuarios", n));
    public Task<ResultadoUsuario> EliminarUsuarioAsync(int id) => Enviar<ResultadoUsuario>(Req(HttpMethod.Delete, $"api/usuarios/{id}"));
    public Task<ResultadoUsuario> ReactivarUsuarioAsync(int id) => Enviar<ResultadoUsuario>(Req(HttpMethod.Put, $"api/usuarios/{id}/reactivar"));
    // Sección 13
    public Task<RespuestaAsistente> AsistenteAsync(string consulta) => Enviar<RespuestaAsistente>(Req(HttpMethod.Post, "api/asistente", new SolicitudAsistente(consulta)));
    public Task<EstadoAsistente> EstadoAsistenteAsync() => Enviar<EstadoAsistente>(Req(HttpMethod.Get, "api/asistente/estado"));

    // CU-11 / RF11 Administración: precios, habitaciones, huéspedes y reservas (solo ADMINISTRADOR)
    public Task<List<TipoResumen>> TiposAsync() => Enviar<List<TipoResumen>>(Req(HttpMethod.Get, "api/admin/tipos"));
    public Task<List<TarifaResumen>> TarifasAsync(int? idTipo = null) => Enviar<List<TarifaResumen>>(Req(HttpMethod.Get, "api/admin/tarifas" + (idTipo == null ? "" : "?idTipo=" + idTipo)));
    public Task<ResultadoAdmin> CrearTarifaAsync(NuevaTarifa n) => Enviar<ResultadoAdmin>(Req(HttpMethod.Post, "api/admin/tarifas", n));
    public Task<ResultadoAdmin> ModificarTarifaAsync(int id, NuevaTarifa n) => Enviar<ResultadoAdmin>(Req(HttpMethod.Put, $"api/admin/tarifas/{id}", n));
    public Task<ResultadoAdmin> EliminarTarifaAsync(int id) => Enviar<ResultadoAdmin>(Req(HttpMethod.Delete, $"api/admin/tarifas/{id}"));

    public Task<List<HabitacionAdmin>> HabitacionesAdminAsync() => Enviar<List<HabitacionAdmin>>(Req(HttpMethod.Get, "api/admin/habitaciones"));
    public Task<ResultadoAdmin> CrearHabitacionAsync(DatosHabitacion d) => Enviar<ResultadoAdmin>(Req(HttpMethod.Post, "api/admin/habitaciones", d));
    public Task<ResultadoAdmin> ModificarHabitacionAsync(int id, DatosHabitacion d) => Enviar<ResultadoAdmin>(Req(HttpMethod.Put, $"api/admin/habitaciones/{id}", d));
    public Task<ResultadoAdmin> EliminarHabitacionAsync(int id) => Enviar<ResultadoAdmin>(Req(HttpMethod.Delete, $"api/admin/habitaciones/{id}"));

    public Task<List<HuespedAdmin>> HuespedesAdminAsync(string? buscar = null) => Enviar<List<HuespedAdmin>>(Req(HttpMethod.Get, "api/admin/huespedes" + (string.IsNullOrWhiteSpace(buscar) ? "" : "?buscar=" + Uri.EscapeDataString(buscar))));
    public Task<ResultadoAdmin> ModificarHuespedAsync(int id, DatosHuesped d) => Enviar<ResultadoAdmin>(Req(HttpMethod.Put, $"api/admin/huespedes/{id}", d));
    public Task<ResultadoAdmin> EliminarHuespedAsync(int id) => Enviar<ResultadoAdmin>(Req(HttpMethod.Delete, $"api/admin/huespedes/{id}"));

    public Task<ResultadoAdmin> ModificarReservaAsync(string codigo, CambioReserva c) => Enviar<ResultadoAdmin>(Req(HttpMethod.Put, $"api/admin/reservas/{codigo}", c));
    public Task<ResultadoAdmin> EliminarReservaAsync(string codigo) => Enviar<ResultadoAdmin>(Req(HttpMethod.Delete, $"api/admin/reservas/{codigo}"));

    // ---------------------------------------------------------------
    //  CU-16 / RF21 Portal del usuario final (no requiere iniciar sesión)
    // ---------------------------------------------------------------
    public Task<Catalogo> CatalogoAsync(DateOnly entrada, DateOnly salida, int personas)
        => Enviar<Catalogo>(Req(HttpMethod.Get, $"api/portal/catalogo?entrada={entrada:yyyy-MM-dd}&salida={salida:yyyy-MM-dd}&personas={personas}"));
    public Task<HuespedPortal> HuespedPortalAsync(string documento)
        => Enviar<HuespedPortal>(Req(HttpMethod.Get, "api/portal/huespedes/" + Uri.EscapeDataString(documento)));
    public Task<ResultadoReservaPortal> ReservarPortalAsync(SolicitudReservaPortal s)
        => Enviar<ResultadoReservaPortal>(Req(HttpMethod.Post, "api/portal/reservas", s));
    /// <summary>
    /// CU-18: el propio huésped cancela su reserva. Se identifica con la sesión de cliente o
    /// con el documento con el que reservó, igual que para consultar el comprobante.
    /// </summary>
    public Task<ResultadoCancelacion> CancelarMiReservaAsync(string codigo, string? documento, string? motivo = null)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ApiException(HttpStatusCode.BadRequest, "Falta el código de la reserva que quiere cancelar.");
        return Enviar<ResultadoCancelacion>(Req(HttpMethod.Post, $"api/portal/reservas/{Uri.EscapeDataString(codigo.Trim())}/cancelar",
            new { documento, motivo }));
    }

    /// <summary>
    /// Consulta un comprobante sin iniciar sesión. Si falta el número o el documento la petición
    /// no se envía: con el número vacío la dirección quedaría sin ese tramo, ninguna ruta de la
    /// API coincidiría y el servidor devolvería la página de la aplicación en lugar de datos.
    /// </summary>
    public Task<ComprobantePortal> ComprobantePortalAsync(string numero, string documento)
    {
        if (string.IsNullOrWhiteSpace(numero) || string.IsNullOrWhiteSpace(documento))
            throw new ApiException(HttpStatusCode.BadRequest,
                "Para consultar un comprobante se necesitan el número que recibió y el documento con el que reservó.");
        return Enviar<ComprobantePortal>(Req(HttpMethod.Get,
            $"api/portal/comprobantes/{Uri.EscapeDataString(numero.Trim())}?documento={Uri.EscapeDataString(documento.Trim())}"));
    }
    public Task<List<ReservaCliente>> MisReservasAsync()
        => Enviar<List<ReservaCliente>>(Req(HttpMethod.Get, "api/portal/mis-reservas"));
    public async Task<RespuestaLogin> RegistroAsync(SolicitudRegistro s)
    {
        var r = await Enviar<RespuestaLogin>(Req(HttpMethod.Post, "api/auth/registro", s));
        _sesion.Iniciar(r); return r;
    }
    public Task<TipoDetalle> TipoDetalleAsync(int idTipo, DateOnly entrada, DateOnly salida, int personas)
        => Enviar<TipoDetalle>(Req(HttpMethod.Get, $"api/portal/tipos/{idTipo}?entrada={entrada:yyyy-MM-dd}&salida={salida:yyyy-MM-dd}&personas={personas}"));

    // Galería de la ficha (solo ADMINISTRADOR)
    public Task<List<FotoAdmin>> FotosTipoAsync(int idTipo) => Enviar<List<FotoAdmin>>(Req(HttpMethod.Get, $"api/admin/tipos/{idTipo}/fotos"));
    public Task<ResultadoAdmin> AgregarFotoAsync(int idTipo, NuevaFoto n) => Enviar<ResultadoAdmin>(Req(HttpMethod.Post, $"api/admin/tipos/{idTipo}/fotos", n));
    public Task<ResultadoAdmin> EliminarFotoAsync(int idTipo, int idFoto) => Enviar<ResultadoAdmin>(Req(HttpMethod.Delete, $"api/admin/tipos/{idTipo}/fotos/{idFoto}"));
    public Task<ResultadoAdmin> FotoPortadaAsync(int idTipo, int idFoto) => Enviar<ResultadoAdmin>(Req(HttpMethod.Put, $"api/admin/tipos/{idTipo}/fotos/{idFoto}/portada"));

    public Task<ResultadoAdmin> ModificarTipoAsync(int id, DatosTipo d)
        => Enviar<ResultadoAdmin>(Req(HttpMethod.Put, $"api/admin/tipos/{id}", d));

    public static string Moneda(decimal v) => "$ " + Math.Round(v).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", ".");
}
