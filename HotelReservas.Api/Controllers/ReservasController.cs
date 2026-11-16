using HotelReservas.Api.Data;
using HotelReservas.Api.Seguridad;
using HotelReservas.Api.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HotelReservas.Api.Controllers;

public record SolicitudReserva(int IdHuesped, int IdHabitacion, DateOnly FechaEntrada, DateOnly FechaSalida, int NumeroPersonas, string MedioPago, bool? ExentoIva);
public record SolicitudCambioEstado(string? VersionFila, string? Detalle);

/// <summary>CU-03 Consultar disponibilidad (RF03).</summary>
[ApiController]
[Route("api/disponibilidad")]
[Authorize(Roles = "ADMINISTRADOR")]
public class DisponibilidadController : ControllerBase
{
    private readonly ServicioReservas _svc;
    public DisponibilidadController(ServicioReservas svc) => _svc = svc;

    /// <summary>GET /api/disponibilidad?tipo=Doble&entrada=2026-10-16&salida=2026-10-18&exentoIva=false</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<HabitacionDisponible>>> Get([FromQuery] string tipo, [FromQuery] DateOnly entrada, [FromQuery] DateOnly salida, [FromQuery] bool exentoIva = false)
    {
        try { return Ok(await _svc.ConsultarDisponibilidadAsync(tipo, entrada, salida, exentoIva)); }
        catch (ErrorNegocio ex) { return BadRequest(new { codigo = ex.Codigo, mensaje = ex.Message }); }
    }
}

/// <summary>CU-04, CU-05, CU-06 (confirmación transaccional), CU-08, CU-09, CU-10 y CU-12.</summary>
[ApiController]
[Route("api/reservas")]
[Authorize(Roles = "ADMINISTRADOR")]
public class ReservasController : ControllerBase
{
    private readonly ServicioReservas _svc;
    private readonly HotelReservasContext _db;
    public ReservasController(ServicioReservas svc, HotelReservasContext db) { _svc = svc; _db = db; }

    /// <summary>POST /api/reservas: ejecuta sp_ConfirmarReserva. 201 con código y comprobante; 409 si la habitación ya no está disponible.</summary>
    [HttpPost]
    public async Task<ActionResult<ResultadoConfirmacion>> Confirmar([FromBody] SolicitudReserva s)
    {
        try
        {
            var r = await _svc.ConfirmarAsync(s.IdHuesped, s.IdHabitacion, User.IdUsuario(), s.FechaEntrada, s.FechaSalida, s.NumeroPersonas, s.MedioPago, s.ExentoIva);
            return CreatedAtAction(nameof(Obtener), new { codigo = r.Codigo }, r);
        }
        catch (ErrorNegocio ex) when (ex.Codigo == 50001) { return Conflict(new { codigo = ex.Codigo, mensaje = ex.Message }); }
        catch (ErrorNegocio ex) { return BadRequest(new { codigo = ex.Codigo, mensaje = ex.Message }); }
    }

    /// <summary>GET /api/reservas?estado=CONFIRMADA  (historial, CU-12)</summary>
    [HttpGet]
    public async Task<ActionResult> Listar([FromQuery] string? estado)
    {
        var q = _db.Reservas.Include(r => r.Habitacion).ThenInclude(h => h.Tipo).Include(r => r.Huesped).Include(r => r.Comprobante).AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(estado)) q = q.Where(r => r.Estado == estado);
        var lista = await q.OrderByDescending(r => r.IdReserva).Take(200).Select(r => new
        {
            r.Codigo, r.Estado, habitacion = r.Habitacion.Numero, tipo = r.Habitacion.Tipo.Nombre,
            huesped = r.Huesped.Nombres + " " + r.Huesped.Apellidos, r.FechaEntrada, r.FechaSalida, r.NumeroPersonas,
            r.Subtotal, r.Impuesto, r.ExentoIva, precioNeto = r.ValorTotal, comprobante = r.Comprobante != null ? r.Comprobante.Numero : null,
            versionFila = Convert.ToBase64String(r.VersionFila),
        }).ToListAsync();
        return Ok(lista);
    }

    /// <summary>GET /api/reservas/{codigo}: detalle con pagos, comprobante y bitácora (RNF05).</summary>
    [HttpGet("{codigo}")]
    public async Task<ActionResult> Obtener(string codigo)
    {
        var r = await _db.Reservas.Include(x => x.Habitacion).ThenInclude(h => h.Tipo).Include(x => x.Huesped).Include(x => x.Pagos)
            .Include(x => x.Comprobante).Include(x => x.Bitacora).ThenInclude(b => b.Usuario).AsNoTracking().FirstOrDefaultAsync(x => x.Codigo == codigo);
        if (r == null) return NotFound();
        return Ok(new
        {
            r.Codigo, r.Estado, habitacion = r.Habitacion.Numero, tipo = r.Habitacion.Tipo.Nombre,
            huesped = new { r.Huesped.Nombres, r.Huesped.Apellidos, documento = r.Huesped.TipoDocumento + " " + r.Huesped.NumeroDocumento },
            r.FechaEntrada, r.FechaSalida, r.NumeroPersonas, r.Subtotal, r.TasaIva, r.Impuesto, r.ExentoIva, precioNeto = r.ValorTotal,
            pagos = r.Pagos.Select(p => new { p.Valor, p.Medio, p.Fecha, p.Estado }),
            comprobante = r.Comprobante == null ? null : new { r.Comprobante.Numero, r.Comprobante.FechaEmision, r.Comprobante.Valor },
            bitacora = r.Bitacora.OrderBy(b => b.Fecha).Select(b => new { b.Fecha, usuario = b.Usuario.Nombre, b.EstadoAnterior, b.EstadoNuevo, b.Detalle }),
            versionFila = Convert.ToBase64String(r.VersionFila),
        });
    }

    [HttpPut("{codigo}/checkin")]
    public Task<ActionResult> CheckIn(string codigo, [FromBody] SolicitudCambioEstado? s) => Cambiar(codigo, "EN_CASA", s);

    [HttpPut("{codigo}/checkout")]
    public Task<ActionResult> CheckOut(string codigo, [FromBody] SolicitudCambioEstado? s) => Cambiar(codigo, "FINALIZADA", s);

    /// <summary>
    /// Recepción cobra una reserva retenida en efectivo: PENDIENTE pasa a CONFIRMADA, se borra la
    /// hora límite y se registra la fila de PAGO, que hasta ese momento no existía (script 18).
    /// </summary>
    [HttpPut("{codigo}/registrar-pago")]
    public Task<ActionResult> RegistrarPago(string codigo, [FromBody] SolicitudCambioEstado? s) =>
        Cambiar(codigo, "CONFIRMADA", s ?? new SolicitudCambioEstado(null, "Pago en efectivo recibido en recepción."));

    /// <summary>CU-10 Cancelar reserva: reservado al ADMINISTRADOR (anula los pagos). El recepcionista recibe 403.</summary>
    [HttpPut("{codigo}/cancelar")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public Task<ActionResult> Cancelar(string codigo, [FromBody] SolicitudCambioEstado? s) => Cambiar(codigo, "CANCELADA", s);

    private async Task<ActionResult> Cambiar(string codigo, string estado, SolicitudCambioEstado? s)
    {
        try
        {
            byte[]? version = string.IsNullOrEmpty(s?.VersionFila) ? null : Convert.FromBase64String(s!.VersionFila!);
            var r = await _svc.CambiarEstadoAsync(codigo, estado, User.IdUsuario(), version, s?.Detalle);
            return Ok(new { r.Codigo, r.Estado, versionFila = Convert.ToBase64String(r.VersionFila) });
        }
        catch (ErrorNegocio ex) when (ex.Codigo == 50012) { return Conflict(new { codigo = ex.Codigo, mensaje = ex.Message }); }
        catch (ErrorNegocio ex) when (ex.Codigo == 50010) { return NotFound(new { codigo = ex.Codigo, mensaje = ex.Message }); }
        catch (ErrorNegocio ex) { return BadRequest(new { codigo = ex.Codigo, mensaje = ex.Message }); }
    }
}

/// <summary>CU-02 y RF18 (reserva rápida para huésped frecuente).</summary>
[ApiController]
[Route("api/huespedes")]
[Authorize(Roles = "ADMINISTRADOR")]
public class HuespedesController : ControllerBase
{
    private readonly HotelReservasContext _db;
    public HuespedesController(HotelReservasContext db) => _db = db;

    /// <summary>GET /api/huespedes?documento=2020202020: perfil, número de estadías y preferencias de la última (sp_HuespedFrecuente).</summary>
    [HttpGet]
    public async Task<ActionResult> PorDocumento([FromQuery] string documento)
    {
        var h = await _db.Huespedes.AsNoTracking().FirstOrDefaultAsync(x => x.NumeroDocumento == documento);
        if (h == null) return NotFound(new { mensaje = "Documento no registrado." });
        var activas = new[] { "CONFIRMADA", "EN_CASA", "FINALIZADA" };
        var estadias = await _db.Reservas.CountAsync(r => r.IdHuesped == h.IdHuesped && activas.Contains(r.Estado));
        var ultima = await _db.Reservas.Include(r => r.Habitacion).ThenInclude(x => x.Tipo).Include(r => r.Pagos).AsNoTracking()
            .Where(r => r.IdHuesped == h.IdHuesped && activas.Contains(r.Estado)).OrderByDescending(r => r.FechaEntrada)
            .Select(r => new { tipo = r.Habitacion.Tipo.Nombre, idTipo = r.Habitacion.IdTipo, habitacion = r.Habitacion.Numero, personas = r.NumeroPersonas,
                               entrada = r.FechaEntrada, salida = r.FechaSalida, medioPago = r.Pagos.OrderByDescending(p => p.IdPago).Select(p => p.Medio).FirstOrDefault() })
            .FirstOrDefaultAsync();
        var ultimaEstadia = ultima == null ? null : new { ultima.tipo, ultima.idTipo, ultima.habitacion, ultima.personas, noches = ultima.salida.DayNumber - ultima.entrada.DayNumber, ultima.medioPago, fecha = ultima.entrada };
        return Ok(new { h.IdHuesped, h.Nombres, h.Apellidos, h.TipoDocumento, h.NumeroDocumento, h.Telefono, h.ExtranjeroNoResidente, estadias, esFrecuente = estadias >= 2, ultimaEstadia });
    }

    public record NuevoHuesped(string TipoDocumento, string NumeroDocumento, string Nombres, string Apellidos, string? Telefono, string? Correo, bool ExtranjeroNoResidente);

    /// <summary>POST /api/huespedes (CU-02, RF02). Documento duplicado → 409.</summary>
    [HttpPost]
    public async Task<ActionResult> Crear([FromBody] NuevoHuesped n)
    {
        if (await _db.Huespedes.AnyAsync(x => x.NumeroDocumento == n.NumeroDocumento)) return Conflict(new { mensaje = "Ya existe un huésped con ese documento." });
        var h = new Huesped { TipoDocumento = n.TipoDocumento, NumeroDocumento = n.NumeroDocumento, Nombres = n.Nombres, Apellidos = n.Apellidos, Telefono = n.Telefono, Correo = n.Correo, ExtranjeroNoResidente = n.ExtranjeroNoResidente };
        _db.Huespedes.Add(h); await _db.SaveChangesAsync();
        return Created($"/api/huespedes?documento={h.NumeroDocumento}", new { h.IdHuesped });
    }
}

/// <summary>CU-06 y RF17 (comprobante compartible por WhatsApp).</summary>
[ApiController]
[Route("api/comprobantes")]
[Authorize(Roles = "ADMINISTRADOR")]
public class ComprobantesController : ControllerBase
{
    private readonly HotelReservasContext _db;
    public ComprobantesController(HotelReservasContext db) => _db = db;

    /// <summary>GET /api/comprobantes/{numero}/compartir: texto del comprobante y enlace wa.me para el teléfono del huésped.</summary>
    [HttpGet("{numero}/compartir")]
    public async Task<ActionResult> Compartir(string numero)
    {
        var c = await _db.Comprobantes.AsNoTracking().FirstOrDefaultAsync(x => x.Numero == numero);
        if (c == null) return NotFound();
        var r = await _db.Reservas.Include(x => x.Habitacion).ThenInclude(h => h.Tipo).Include(x => x.Huesped).Include(x => x.Pagos).AsNoTracking().FirstAsync(x => x.IdReserva == c.IdReserva);
        var noches = r.FechaSalida.DayNumber - r.FechaEntrada.DayNumber;
        var medio = r.Pagos.OrderByDescending(p => p.IdPago).Select(p => p.Medio).FirstOrDefault() ?? "-";
        string M(decimal v) => Asistente.CatalogoFunciones.Moneda(v);
        var texto = $"🏨 *Hotel Reservas* · Comprobante {c.Numero}\nReserva: {r.Codigo}\nHuésped: {r.Huesped.Nombres} {r.Huesped.Apellidos} ({r.Huesped.TipoDocumento} {r.Huesped.NumeroDocumento})\n" +
                    $"Habitación: {r.Habitacion.Numero} · {r.Habitacion.Tipo.Nombre} · {r.NumeroPersonas} persona{(r.NumeroPersonas > 1 ? "s" : "")}\n" +
                    $"Entrada: {r.FechaEntrada:yyyy-MM-dd} · Salida: {r.FechaSalida:yyyy-MM-dd} ({noches} noche{(noches > 1 ? "s" : "")})\n" +
                    $"Subtotal: {M(r.Subtotal)}\nIVA 19 %: {(r.ExentoIva ? "exento (extranjero no residente)" : M(r.Impuesto))}\n*Precio neto: {M(r.ValorTotal)}*\n" +
                    $"Pago: {medio} · Estado: {r.Estado}\nPresente este comprobante en recepción. ¡Lo esperamos!";
        var tel = new string((r.Huesped.Telefono ?? "").Where(char.IsDigit).ToArray());
        if (tel.Length == 10 && tel.StartsWith("3")) tel = "57" + tel;
        var enlace = tel.Length == 0 ? null : $"https://wa.me/{tel}?text={Uri.EscapeDataString(texto)}";
        return Ok(new { comprobante = c.Numero, reserva = r.Codigo, telefono = tel, texto, enlaceWhatsApp = enlace });
    }
}
