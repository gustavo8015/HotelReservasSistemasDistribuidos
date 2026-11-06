using HotelReservas.Api.Asistente;
using HotelReservas.Api.Data;
using HotelReservas.Api.Seguridad;
using HotelReservas.Api.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HotelReservas.Api.Controllers;

// ---------- Contratos del portal del usuario final (CU-16 / RF21) ----------

public record DatosHuespedPortal(string TipoDocumento, string NumeroDocumento, string Nombres, string Apellidos,
    string? Telefono, string? Correo, bool ExtranjeroNoResidente);

public record SolicitudReservaPortal(DatosHuespedPortal Huesped, int IdTipo, int? IdHabitacion,
    DateOnly FechaEntrada, DateOnly FechaSalida, int NumeroPersonas,
    string MedioPago, DatosTarjeta? Tarjeta, string CanalComprobante, string? DestinoComprobante);

/// <summary>Cancelación hecha por el propio huésped: documento con el que reservó y motivo opcional.</summary>
public record SolicitudCancelacion(string? Documento, string? Motivo);

/// <summary>
/// Portal público del usuario final (CU-16 / RF21). Permite ver el catálogo de habitaciones con su
/// foto y su precio, reservar escribiendo documento y nombre, pagar en efectivo o con tarjeta a través
/// de la pasarela simulada y elegir si el comprobante se envía por correo o por WhatsApp.
///
/// El catálogo y la reserva son anónimos: no exigen cuenta. Si el visitante sí inició sesión como
/// CLIENTE, la reserva queda a su nombre y aparece en "Mis reservas"; si no, se registra a nombre del
/// usuario técnico portal@hotel.com que crea el script 07, porque RESERVA.id_usuario es obligatorio.
///
/// La confirmación sigue pasando por sp_ConfirmarReserva: el portal no escribe reservas por su cuenta,
/// de modo que la transacción con bloqueo (SERIALIZABLE + UPDLOCK/HOLDLOCK) sigue siendo la única puerta.
/// </summary>
[ApiController]
[Route("api/portal")]
public class PortalController : ControllerBase
{
    private readonly HotelReservasContext _db;
    private readonly ServicioReservas _svc;
    private readonly PasarelaSimulada _pasarela;
    private static readonly string[] EstadosActivos = { "CONFIRMADA", "EN_CASA" };

    /// <summary>
    /// Horas que el huésped tiene para pagar en recepción cuando reserva en efectivo.
    /// Mientras el plazo corre, la habitación no se le ofrece a nadie más; si vence sin
    /// pago, sp_LiberarReservasVencidas cancela la reserva y libera la habitación.
    /// </summary>
    public const int HorasPlazoPago = 24;

    /// <summary>
    /// Antelación mínima para que el huésped cancele desde el portal. Dentro de las últimas
    /// horas antes de la llegada la cancelación queda en manos de recepción.
    /// </summary>
    public const int HorasMinimasCancelacion = 24;

    public PortalController(HotelReservasContext db, ServicioReservas svc, PasarelaSimulada pasarela)
    { _db = db; _svc = svc; _pasarela = pasarela; }

    /// <summary>
    /// Habitaciones que no se pueden ofrecer en un rango de fechas. Además de las reservas
    /// activas, cuentan las retenidas en efectivo cuyo plazo de pago sigue vigente: si no
    /// contaran, dos personas podrían retener la misma habitación al mismo tiempo.
    /// </summary>
    private async Task<List<int>> OcupadasAsync(DateOnly entrada, DateOnly salida)
    {
        await _svc.LiberarVencidasAsync();
        var ahora = DateTime.Now;
        return await _db.Reservas
            .Where(r => (EstadosActivos.Contains(r.Estado)
                         || (r.Estado == "PENDIENTE" && r.LimitePago != null && r.LimitePago > ahora))
                        && r.FechaEntrada < salida && r.FechaSalida > entrada)
            .Select(r => r.IdHabitacion).Distinct().ToListAsync();
    }

    /// <summary>
    /// Dice si el huésped todavía puede cancelar esta reserva desde el portal, y por qué no
    /// cuando no puede. La regla es una sola: faltan más de <see cref="HorasMinimasCancelacion"/>
    /// horas para la hora de entrada, que son las 3:00 p. m. del día de llegada.
    /// </summary>
    private static (bool Puede, string Motivo) PuedeCancelar(string estado, DateOnly fechaEntrada)
    {
        if (estado == "CANCELADA") return (false, "La reserva ya está cancelada.");
        if (estado == "EN_CASA") return (false, "El huésped ya hizo el registro de entrada. Hable con recepción.");
        if (estado == "FINALIZADA") return (false, "La estadía ya terminó.");
        if (estado != "CONFIRMADA" && estado != "PENDIENTE") return (false, "Esta reserva no se puede cancelar desde el portal.");

        var horaEntrada = fechaEntrada.ToDateTime(new TimeOnly(15, 0));
        var faltan = horaEntrada - DateTime.Now;
        if (faltan.TotalHours < HorasMinimasCancelacion)
            return (false, $"La cancelación en línea se cierra {HorasMinimasCancelacion} horas antes de la llegada. Comuníquese con recepción.");
        return (true, "");
    }

    /// <summary>
    /// GET /api/portal/catalogo?entrada=2026-11-02&amp;salida=2026-11-04&amp;personas=2
    /// Tipos de habitación con foto, descripción, capacidad, tarifa vigente, precio del rango y cuántas
    /// habitaciones quedan libres. Es la vitrina de la página de inicio.
    /// </summary>
    [HttpGet("catalogo")]
    [AllowAnonymous]
    public async Task<ActionResult> Catalogo([FromQuery] DateOnly? entrada, [FromQuery] DateOnly? salida, [FromQuery] int personas = 1)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var ent = entrada ?? hoy.AddDays(1);
        var sal = salida ?? ent.AddDays(1);
        if (sal <= ent) return BadRequest(new { codigo = 50000, mensaje = "La fecha de salida debe ser posterior a la de entrada." });
        if (ent < hoy) return BadRequest(new { codigo = 50006, mensaje = "La fecha de entrada no puede estar en el pasado." });

        var noches = sal.DayNumber - ent.DayNumber;
        var ocupadas = await OcupadasAsync(ent, sal);

        var tipos = await _db.TiposHabitacion.AsNoTracking().OrderBy(t => t.IdTipo).ToListAsync();
        var habitaciones = await _db.Habitaciones.AsNoTracking().OrderBy(h => h.Numero).ToListAsync();
        var salida_ = new List<object>();

        foreach (var t in tipos)
        {
            var tarifa = await _svc.TarifaVigenteAsync(t.IdTipo, ent);
            var libres = habitaciones.Where(h => h.IdTipo == t.IdTipo && h.Estado != "MANTENIMIENTO" && !ocupadas.Contains(h.IdHabitacion)).ToList();
            var subtotal = (tarifa ?? 0) * noches;
            var iva = ServicioReservas.CalcularIva(subtotal, false);
            salida_.Add(new
            {
                t.IdTipo,
                tipo = t.Nombre,
                t.Capacidad,
                t.Descripcion,
                imagen = t.Imagen ?? "img/habitaciones/doble.svg",
                tarifaNoche = tarifa,
                hayTarifa = tarifa != null,
                noches,
                subtotal,
                iva,
                precioNeto = subtotal + iva,
                libres = libres.Count,
                disponible = tarifa != null && libres.Count > 0 && personas <= t.Capacidad,
                cabe = personas <= t.Capacidad,
                idHabitacionSugerida = libres.Select(h => (int?)h.IdHabitacion).FirstOrDefault(),
                habitacionSugerida = libres.Select(h => h.Numero).FirstOrDefault(),
            });
        }
        return Ok(new { entrada = ent, salida = sal, noches, personas, tipos = salida_ });
    }

    /// <summary>
    /// GET /api/portal/tipos/{idTipo}?entrada=&amp;salida=&amp;personas= (CU-17): ficha completa de un tipo de
    /// habitación, con su galería de fotos, sus comodidades, el precio del rango elegido y cuántas
    /// habitaciones de ese tipo quedan libres. Es lo que alimenta la pantalla de detalle del portal.
    /// </summary>
    [HttpGet("tipos/{idTipo:int}")]
    [AllowAnonymous]
    public async Task<ActionResult> Tipo(int idTipo, [FromQuery] DateOnly? entrada, [FromQuery] DateOnly? salida, [FromQuery] int personas = 1)
    {
        var t = await _db.TiposHabitacion.AsNoTracking().FirstOrDefaultAsync(x => x.IdTipo == idTipo);
        if (t == null) return NotFound(new { mensaje = "El tipo de habitación no existe." });

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var ent = entrada ?? hoy.AddDays(1);
        var sal = salida ?? ent.AddDays(1);
        if (sal <= ent) sal = ent.AddDays(1);
        var noches = sal.DayNumber - ent.DayNumber;
        if (personas < 1) personas = 1;

        var ocupadas = await OcupadasAsync(ent, sal);

        var habitaciones = await _db.Habitaciones.AsNoTracking()
            .Where(h => h.IdTipo == idTipo).OrderBy(h => h.Numero).ToListAsync();
        var libres = habitaciones.Where(h => h.Estado != "MANTENIMIENTO" && !ocupadas.Contains(h.IdHabitacion)).ToList();

        var fotos = await _db.FotosHabitacion.AsNoTracking().Where(f => f.IdTipo == idTipo)
            .OrderBy(f => f.Orden).ThenBy(f => f.IdFoto)
            .Select(f => new { f.Ruta, f.Descripcion }).ToListAsync();
        if (fotos.Count == 0 && !string.IsNullOrWhiteSpace(t.Imagen))
            fotos = new[] { new { Ruta = t.Imagen!, Descripcion = (string?)("Habitación " + t.Nombre) } }.ToList();

        var tarifa = await _svc.TarifaVigenteAsync(t.IdTipo, ent);
        var subtotal = (tarifa ?? 0) * noches;
        var iva = ServicioReservas.CalcularIva(subtotal, false);

        var comodidades = (t.Comodidades ?? "")
            .Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim()).Where(c => c.Length > 0).ToList();

        return Ok(new
        {
            t.IdTipo,
            tipo = t.Nombre,
            t.Descripcion,
            t.Capacidad,
            comodidades,
            fotos,
            imagen = t.Imagen,
            entrada = ent,
            salida = sal,
            noches,
            personas,
            tarifaNoche = tarifa,
            hayTarifa = tarifa != null,
            subtotal,
            iva,
            precioNeto = subtotal + iva,
            libres = libres.Count,
            totalHabitaciones = habitaciones.Count,
            cabe = personas <= t.Capacidad,
            disponible = tarifa != null && libres.Count > 0 && personas <= t.Capacidad,
            idHabitacionSugerida = libres.Select(h => (int?)h.IdHabitacion).FirstOrDefault(),
            habitacionSugerida = libres.Select(h => h.Numero).FirstOrDefault(),
            pisos = libres.Select(h => h.Piso).Distinct().OrderBy(x => x).ToList(),
        });
    }

    /// <summary>GET /api/portal/huespedes/{documento}: el portal precarga el nombre si la persona ya se hospedó.</summary>
    [HttpGet("huespedes/{documento}")]
    [AllowAnonymous]
    public async Task<ActionResult> Huesped(string documento)
    {
        var h = await _db.Huespedes.AsNoTracking().FirstOrDefaultAsync(x => x.NumeroDocumento == documento);
        if (h == null) return NotFound(new { mensaje = "Documento no registrado. Se creará la ficha al reservar." });
        var estadias = await _db.Reservas.CountAsync(r => r.IdHuesped == h.IdHuesped && r.Estado != "CANCELADA");
        return Ok(new { h.IdHuesped, h.TipoDocumento, h.NumeroDocumento, h.Nombres, h.Apellidos, h.Telefono, h.Correo, h.ExtranjeroNoResidente, estadias, esFrecuente = estadias >= 2 });
    }

    /// <summary>
    /// POST /api/portal/reservas: el usuario final reserva, paga y recibe el comprobante.
    ///
    /// Orden de la operación, que es el punto interesante del proyecto: primero se verifica la
    /// disponibilidad, después se cobra en la pasarela y por último se confirma la reserva con
    /// sp_ConfirmarReserva. Si la confirmación falla porque otra persona tomó la habitación en ese
    /// instante, el cobro ya ocurrió y se deshace con PasarelaSimulada.Reversar, que es la
    /// compensación del patrón Saga: pasarela y base de datos no comparten una transacción.
    /// </summary>
    [HttpPost("reservas")]
    [AllowAnonymous]
    public async Task<ActionResult> Reservar([FromBody] SolicitudReservaPortal s)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        if (s.FechaSalida <= s.FechaEntrada) return BadRequest(new { codigo = 50000, mensaje = "La fecha de salida debe ser posterior a la de entrada." });
        if (s.FechaEntrada < hoy) return BadRequest(new { codigo = 50006, mensaje = "La fecha de entrada no puede estar en el pasado." });
        if (s.NumeroPersonas < 1) return BadRequest(new { codigo = 50005, mensaje = "Indique cuántas personas se hospedan." });

        // ---- 1. Datos de la persona: documento y nombre son obligatorios ----
        var doc = (s.Huesped?.NumeroDocumento ?? "").Trim();
        var nombres = (s.Huesped?.Nombres ?? "").Trim();
        var apellidos = (s.Huesped?.Apellidos ?? "").Trim();
        if (doc.Length < 5) return BadRequest(new { codigo = 50007, mensaje = "Escriba el número de documento." });
        if (nombres.Length < 2) return BadRequest(new { codigo = 50007, mensaje = "Escriba el nombre de quien se hospeda." });

        var telefono = SoloDigitos(s.Huesped!.Telefono);
        var correo = (s.Huesped.Correo ?? "").Trim();

        // ---- 2. Canal del comprobante ----
        var canal = (s.CanalComprobante ?? "CORREO").Trim().ToUpperInvariant();
        if (canal != "CORREO" && canal != "WHATSAPP")
            return BadRequest(new { codigo = 50008, mensaje = "Elija si el comprobante se envía por CORREO o por WHATSAPP." });
        var destino = (s.DestinoComprobante ?? (canal == "CORREO" ? correo : telefono)).Trim();
        if (canal == "CORREO" && !CorreoValido(destino))
            return BadRequest(new { codigo = 50008, mensaje = "Escriba un correo válido para enviarle el comprobante." });
        if (canal == "WHATSAPP" && SoloDigitos(destino).Length < 10)
            return BadRequest(new { codigo = 50008, mensaje = "Escriba un número de WhatsApp de al menos diez dígitos." });

        // ---- 3. Habitación: la indicada o la primera libre del tipo ----
        var tipo = await _db.TiposHabitacion.AsNoTracking().FirstOrDefaultAsync(t => t.IdTipo == s.IdTipo);
        if (tipo == null) return BadRequest(new { codigo = 50004, mensaje = "El tipo de habitación no existe." });
        if (s.NumeroPersonas > tipo.Capacidad)
            return BadRequest(new { codigo = 50005, mensaje = $"La habitación {tipo.Nombre} admite máximo {tipo.Capacidad} persona(s)." });

        var ocupadas = await OcupadasAsync(s.FechaEntrada, s.FechaSalida);
        var candidatas = await _db.Habitaciones.AsNoTracking()
            .Where(h => h.IdTipo == tipo.IdTipo && h.Estado != "MANTENIMIENTO").OrderBy(h => h.Numero).ToListAsync();
        var libres = candidatas.Where(h => !ocupadas.Contains(h.IdHabitacion)).ToList();

        var habitacion = s.IdHabitacion.HasValue
            ? libres.FirstOrDefault(h => h.IdHabitacion == s.IdHabitacion.Value)
            : libres.FirstOrDefault();
        if (habitacion == null)
            return Conflict(new { codigo = 50001, mensaje = $"Ya no quedan habitaciones {tipo.Nombre} libres en esas fechas. Pruebe con otras fechas u otro tipo." });

        var tarifa = await _svc.TarifaVigenteAsync(tipo.IdTipo, s.FechaEntrada);
        if (tarifa == null) return BadRequest(new { codigo = 50003, mensaje = "No hay una tarifa vigente para ese tipo de habitación." });

        // ---- 4. Ficha del huésped: se crea o se actualiza ----
        var h = await _db.Huespedes.FirstOrDefaultAsync(x => x.NumeroDocumento == doc);
        if (h == null)
        {
            h = new Huesped
            {
                TipoDocumento = string.IsNullOrWhiteSpace(s.Huesped.TipoDocumento) ? "CC" : s.Huesped.TipoDocumento.Trim().ToUpperInvariant(),
                NumeroDocumento = doc, Nombres = nombres, Apellidos = apellidos,
                Telefono = string.IsNullOrEmpty(telefono) ? null : telefono,
                Correo = string.IsNullOrEmpty(correo) ? null : correo,
                ExtranjeroNoResidente = s.Huesped.ExtranjeroNoResidente,
            };
            _db.Huespedes.Add(h);
        }
        else
        {
            if (!string.IsNullOrEmpty(telefono)) h.Telefono = telefono;
            if (!string.IsNullOrEmpty(correo)) h.Correo = correo;
        }
        await _db.SaveChangesAsync();

        // ---- 5. Precio que se va a cobrar ----
        var noches = s.FechaSalida.DayNumber - s.FechaEntrada.DayNumber;
        var subtotal = tarifa.Value * noches;
        var iva = ServicioReservas.CalcularIva(subtotal, h.ExtranjeroNoResidente);
        var total = subtotal + iva;

        // ---- 6. Efectivo: la reserva se retiene, no se cobra ----
        // Con tarjeta el dinero entra ahora y la reserva nace CONFIRMADA. En efectivo no hay
        // nada que cobrar en línea: la reserva queda retenida en PENDIENTE con una hora límite
        // para pagar en recepción. Es lo que permite decirle al huésped cuánto tiempo tiene.
        var medio = (s.MedioPago ?? "").Trim().ToUpperInvariant();
        var enEfectivo = medio == "EFECTIVO";
        var idUsuario = await IdUsuarioResponsableAsync();
        ResultadoConfirmacion r;
        ResultadoPago? pago = null;

        if (enEfectivo)
        {
            try
            {
                r = await _svc.ConfirmarAsync(h.IdHuesped, habitacion.IdHabitacion, idUsuario, s.FechaEntrada, s.FechaSalida,
                                              s.NumeroPersonas, "EFECTIVO", h.ExtranjeroNoResidente, "PENDIENTE", HorasPlazoPago);
            }
            catch (ErrorNegocio ex)
            {
                // No hubo cobro, así que no hay nada que reversar.
                var mensajeRetencion = ex.Codigo == 50001
                    ? "Otra persona tomó esa habitación mientras usted llenaba el formulario. No se le cobró nada. Intente con otras fechas."
                    : ex.Message;
                return StatusCode(ex.Codigo == 50001 ? 409 : 400, new { codigo = ex.Codigo, mensaje = mensajeRetencion, pagado = false, reversado = false });
            }
        }
        else
        {
            pago = await _pasarela.CobrarAsync(medio, total, s.Tarjeta);
            if (!pago.Aprobado)
                return StatusCode(402, new { codigo = 50020, mensaje = pago.Mensaje, codigoRechazo = pago.CodigoRechazo, pagado = false });

            // ---- 7. Confirmación transaccional ----
            try
            {
                r = await _svc.ConfirmarAsync(h.IdHuesped, habitacion.IdHabitacion, idUsuario, s.FechaEntrada, s.FechaSalida,
                                              s.NumeroPersonas, pago.Medio, h.ExtranjeroNoResidente);
            }
            catch (ErrorNegocio ex)
            {
                _pasarela.Reversar(pago.Autorizacion, $"La reserva no se pudo confirmar: {ex.Message}");
                var mensaje = ex.Codigo == 50001
                    ? "Otra persona tomó esa habitación mientras usted pagaba. El cobro se reversó y no se le descontó nada. Intente con otras fechas."
                    : ex.Message + " El cobro se reversó.";
                return StatusCode(ex.Codigo == 50001 ? 409 : 400, new { codigo = ex.Codigo, mensaje, pagado = false, reversado = true });
            }
        }

        // ---- 8. Canal del comprobante y referencia del pago ----
        var reserva = await _db.Reservas.FirstAsync(x => x.IdReserva == r.IdReserva);
        reserva.CanalComprobante = canal;
        reserva.DestinoComprobante = Recortar(destino, 120);
        if (pago != null)
        {
            var pagoFila = await _db.Pagos.Where(p => p.IdReserva == r.IdReserva).OrderByDescending(p => p.IdPago).FirstAsync();
            pagoFila.Referencia = Recortar(pago.Referencia, 40);
        }
        await _db.SaveChangesAsync();

        var limitePago = reserva.LimitePago;
        var texto = TextoComprobante(r.Codigo, r.Comprobante, h, habitacion.Numero, tipo.Nombre, s.NumeroPersonas,
                                     s.FechaEntrada, s.FechaSalida, r.Subtotal, r.Impuesto, r.ExentoIva, r.PrecioNeto,
                                     pago?.Medio ?? "EFECTIVO", pago?.Referencia, limitePago);

        return Ok(new
        {
            pagado = !enEfectivo,
            pendientePago = enEfectivo,
            estado = reserva.Estado,
            limitePago,
            horasPlazo = enEfectivo ? HorasPlazoPago : (int?)null,
            avisoPago = enEfectivo
                ? $"Su reserva quedó apartada. Tiene {HorasPlazoPago} horas para pagar en recepción, hasta el {limitePago:yyyy-MM-dd} a las {limitePago:HH:mm}. Si el plazo vence sin pago, la habitación se libera."
                : null,
            r.Codigo,
            comprobante = r.Comprobante,
            habitacion = habitacion.Numero,
            tipo = tipo.Nombre,
            imagen = habitacion.Imagen ?? tipo.Imagen,
            entrada = s.FechaEntrada,
            salida = s.FechaSalida,
            noches,
            personas = s.NumeroPersonas,
            huesped = $"{h.Nombres} {h.Apellidos}".Trim(),
            documento = h.TipoDocumento + " " + h.NumeroDocumento,
            r.Subtotal,
            impuesto = r.Impuesto,
            r.ExentoIva,
            precioNeto = r.PrecioNeto,
            medioPago = pago?.Medio ?? "EFECTIVO",
            autorizacion = pago?.Autorizacion,
            franquicia = pago?.Franquicia,
            ultimosCuatro = pago?.UltimosCuatro,
            mensajePago = pago?.Mensaje,
            canal,
            destino,
            texto,
            enlaceWhatsApp = EnlaceWhatsApp(canal == "WHATSAPP" ? destino : h.Telefono, texto),
            enlaceCorreo = EnlaceCorreo(canal == "CORREO" ? destino : h.Correo, r.Comprobante, texto),
        });
    }

    /// <summary>
    /// POST /api/portal/reservas/{codigo}/cancelar: el propio huésped cancela su reserva.
    ///
    /// Quién puede: el cliente con sesión iniciada sobre sus propias reservas, o cualquiera
    /// que presente el número de documento con el que se hizo la reserva, que es la misma
    /// llave con la que ya se consulta el comprobante sin cuenta.
    ///
    /// Hasta cuándo: faltando más de <see cref="HorasMinimasCancelacion"/> horas para la
    /// llegada. Después la cancelación la hace recepción, que es quien puede valorar el caso.
    ///
    /// Qué pasa con el dinero: si había pago con tarjeta, se reversa y se avisa la devolución.
    /// Si la reserva estaba apenas retenida en efectivo, no hay nada que devolver.
    /// </summary>
    [HttpPost("reservas/{codigo}/cancelar")]
    [AllowAnonymous]
    public async Task<ActionResult> CancelarPorHuesped(string codigo, [FromBody] SolicitudCancelacion? s)
    {
        var r = await _db.Reservas.Include(x => x.Huesped).Include(x => x.Pagos)
                                  .FirstOrDefaultAsync(x => x.Codigo == codigo);
        if (r == null) return NotFound(new { mensaje = "No existe una reserva con ese código." });

        // ---- Quién lo pide ----
        var autorizado = false;
        if (User?.Identity?.IsAuthenticated == true)
        {
            var u = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.IdUsuario == User.IdUsuario());
            if (u?.IdHuesped != null && u.IdHuesped == r.IdHuesped) autorizado = true;
        }
        var documento = (s?.Documento ?? "").Trim();
        if (!autorizado && documento.Length > 0 && documento == r.Huesped.NumeroDocumento) autorizado = true;
        if (!autorizado)
            return StatusCode(403, new { mensaje = "Para cancelar hay que iniciar sesión o escribir el documento con el que se hizo la reserva." });

        // ---- Hasta cuándo ----
        var (puede, motivo) = PuedeCancelar(r.Estado, r.FechaEntrada);
        if (!puede) return Conflict(new { mensaje = motivo, estado = r.Estado });

        var habiaPagoTarjeta = r.Pagos.Any(p => p.Estado == "APROBADO" && p.Medio == "TARJETA");
        var referencia = r.Pagos.Where(p => p.Estado == "APROBADO").OrderByDescending(p => p.IdPago)
                                .Select(p => p.Referencia).FirstOrDefault();
        var estabaRetenida = r.Estado == "PENDIENTE";
        var detalle = string.IsNullOrWhiteSpace(s?.Motivo)
            ? "Cancelada por el huésped desde el portal."
            : "Cancelada por el huésped desde el portal: " + Recortar(s!.Motivo!, 180);

        var idUsuario = await IdUsuarioResponsableAsync();
        try { await _svc.CambiarEstadoAsync(codigo, "CANCELADA", idUsuario, null, detalle); }
        catch (ErrorNegocio ex) { return Conflict(new { codigo = ex.Codigo, mensaje = ex.Message }); }

        if (habiaPagoTarjeta) _pasarela.Reversar(referencia, "Cancelación hecha por el huésped desde el portal.");

        var mensajeFinal = estabaRetenida
            ? "Su reserva se canceló y la habitación quedó libre. Como aún no había pagado, no hay devolución pendiente."
            : habiaPagoTarjeta
                ? "Su reserva se canceló. La devolución del valor pagado se reversó sobre la misma tarjeta; puede tardar algunos días hábiles en verse reflejada."
                : "Su reserva se canceló. Si ya había pagado en recepción, acérquese con su documento para la devolución.";

        return Ok(new { codigo = r.Codigo, estado = "CANCELADA", devolucion = habiaPagoTarjeta, estabaRetenida, mensaje = mensajeFinal });
    }

    /// <summary>GET /api/portal/comprobantes/{numero}?documento=1010101010: recuperar el comprobante sin cuenta.</summary>
    [HttpGet("comprobantes/{numero}")]
    [AllowAnonymous]
    public async Task<ActionResult> Comprobante(string numero, [FromQuery] string documento)
    {
        var c = await _db.Comprobantes.AsNoTracking().FirstOrDefaultAsync(x => x.Numero == numero);
        if (c == null) return NotFound(new { mensaje = "No existe ese comprobante." });
        var r = await _db.Reservas.Include(x => x.Huesped).Include(x => x.Habitacion).ThenInclude(x => x.Tipo)
            .Include(x => x.Pagos).AsNoTracking().FirstAsync(x => x.IdReserva == c.IdReserva);
        if (!string.Equals(r.Huesped.NumeroDocumento, (documento ?? "").Trim(), StringComparison.Ordinal))
            return StatusCode(403, new { mensaje = "El documento no corresponde a ese comprobante." });

        var p = r.Pagos.OrderByDescending(x => x.IdPago).FirstOrDefault();
        var texto = TextoComprobante(r.Codigo, c.Numero, r.Huesped, r.Habitacion.Numero, r.Habitacion.Tipo.Nombre,
                                     r.NumeroPersonas, r.FechaEntrada, r.FechaSalida, r.Subtotal, r.Impuesto,
                                     r.ExentoIva, r.ValorTotal, p?.Medio ?? "-", p?.Referencia,
                                     r.Estado == "PENDIENTE" ? r.LimitePago : null);
        var (puedeCancelar, motivoCancelacion) = PuedeCancelar(r.Estado, r.FechaEntrada);
        return Ok(new
        {
            comprobante = c.Numero, r.Codigo, r.Estado, habitacion = r.Habitacion.Numero, tipo = r.Habitacion.Tipo.Nombre,
            entrada = r.FechaEntrada, salida = r.FechaSalida, precioNeto = r.ValorTotal, canal = r.CanalComprobante,
            destino = r.DestinoComprobante, texto,
            limitePago = r.Estado == "PENDIENTE" ? r.LimitePago : null,
            puedeCancelar, motivoCancelacion,
            enlaceWhatsApp = EnlaceWhatsApp(r.CanalComprobante == "WHATSAPP" ? r.DestinoComprobante : r.Huesped.Telefono, texto),
            enlaceCorreo = EnlaceCorreo(r.CanalComprobante == "CORREO" ? r.DestinoComprobante : r.Huesped.Correo, c.Numero, texto),
        });
    }

    /// <summary>GET /api/portal/mis-reservas: historial de la persona que inició sesión como CLIENTE.</summary>
    [HttpGet("mis-reservas")]
    [Authorize(Roles = "CLIENTE")]
    public async Task<ActionResult> MisReservas()
    {
        var u = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.IdUsuario == User.IdUsuario());
        if (u?.IdHuesped == null) return Ok(Array.Empty<object>());
        var lista = await _db.Reservas.Include(r => r.Habitacion).ThenInclude(h => h.Tipo).Include(r => r.Comprobante).Include(r => r.Pagos)
            .AsNoTracking().Where(r => r.IdHuesped == u.IdHuesped).OrderByDescending(r => r.IdReserva).Take(50)
            .Select(r => new
            {
                r.Codigo, r.Estado, habitacion = r.Habitacion.Numero, tipo = r.Habitacion.Tipo.Nombre,
                imagen = r.Habitacion.Imagen ?? r.Habitacion.Tipo.Imagen,
                r.FechaEntrada, r.FechaSalida, r.NumeroPersonas, r.Subtotal, r.Impuesto, r.ExentoIva,
                precioNeto = r.ValorTotal, comprobante = r.Comprobante != null ? r.Comprobante.Numero : null,
                canal = r.CanalComprobante, destino = r.DestinoComprobante,
                medioPago = r.Pagos.OrderByDescending(p => p.IdPago).Select(p => p.Medio).FirstOrDefault(),
                referencia = r.Pagos.OrderByDescending(p => p.IdPago).Select(p => p.Referencia).FirstOrDefault(),
                r.LimitePago,
            }).ToListAsync();

        // La regla de cancelación se evalúa aquí y no en la pantalla, para que el portal y la
        // API no puedan discrepar sobre lo que el huésped puede hacer.
        var conPermiso = lista.Select(x =>
        {
            var (puede, motivo) = PuedeCancelar(x.Estado, x.FechaEntrada);
            return new
            {
                x.Codigo, x.Estado, x.habitacion, x.tipo, x.imagen, x.FechaEntrada, x.FechaSalida,
                x.NumeroPersonas, x.Subtotal, x.Impuesto, x.ExentoIva, x.precioNeto, x.comprobante,
                x.canal, x.destino, x.medioPago, x.referencia,
                limitePago = x.LimitePago,
                puedeCancelar = puede,
                motivoCancelacion = motivo,
            };
        }).ToList();
        return Ok(conPermiso);
    }

    // ---------------- apoyo ----------------

    /// <summary>Usuario al que se atribuye la reserva: el cliente autenticado o el usuario técnico del portal.</summary>
    private async Task<int> IdUsuarioResponsableAsync()
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            var id = User.IdUsuario();
            if (id > 0 && await _db.Usuarios.AnyAsync(u => u.IdUsuario == id)) return id;
        }
        var portal = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Correo == "portal@hotel.com");
        if (portal != null) return portal.IdUsuario;
        return await _db.Usuarios.AsNoTracking().OrderBy(u => u.IdUsuario).Select(u => u.IdUsuario).FirstAsync();
    }

    public static string SoloDigitos(string? texto) => new((texto ?? "").Where(char.IsDigit).ToArray());

    public static bool CorreoValido([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? correo)
    {
        correo = (correo ?? "").Trim();
        var arroba = correo.IndexOf('@');
        return arroba > 0 && correo.LastIndexOf('.') > arroba + 1 && correo.Length >= 6 && !correo.Contains(' ');
    }

    private static string? Recortar(string? texto, int max) =>
        string.IsNullOrEmpty(texto) ? texto : (texto.Length <= max ? texto : texto[..max]);

    public static string? EnlaceWhatsApp(string? telefono, string texto)
    {
        var tel = SoloDigitos(telefono);
        if (tel.Length == 10 && tel.StartsWith("3")) tel = "57" + tel;
        return tel.Length < 10 ? null : $"https://wa.me/{tel}?text={Uri.EscapeDataString(texto)}";
    }

    public static string? EnlaceCorreo(string? correo, string comprobante, string texto)
    {
        if (!CorreoValido(correo)) return null;
        var asunto = Uri.EscapeDataString($"Hotel Reservas - Comprobante {comprobante}");
        return $"mailto:{correo.Trim()}?subject={asunto}&body={Uri.EscapeDataString(texto)}";
    }

    /// <summary>Texto del comprobante que se envía por correo o por WhatsApp (RF17).</summary>
    public static string TextoComprobante(string codigo, string comprobante, Huesped h, string habitacion, string tipo,
        int personas, DateOnly entrada, DateOnly salida, decimal subtotal, decimal impuesto, bool exento,
        decimal total, string medio, string? referencia, DateTime? limitePago = null)
    {
        var noches = salida.DayNumber - entrada.DayNumber;
        string M(decimal v) => CatalogoFunciones.Moneda(v);
        if (limitePago.HasValue)
            return $"Hotel Reservas · Reserva retenida {comprobante}\n" +
                   $"Reserva: {codigo}\n" +
                   $"Huésped: {h.Nombres} {h.Apellidos} ({h.TipoDocumento} {h.NumeroDocumento})\n" +
                   $"Habitación: {habitacion} · {tipo} · {personas} persona{(personas > 1 ? "s" : "")}\n" +
                   $"Entrada: {entrada:yyyy-MM-dd} · Salida: {salida:yyyy-MM-dd} ({noches} noche{(noches > 1 ? "s" : "")})\n" +
                   $"Subtotal: {M(subtotal)}\n" +
                   $"IVA 19 %: {(exento ? "exento (extranjero no residente, art. 481 E.T.)" : M(impuesto))}\n" +
                   $"Total por pagar: {M(total)}\n" +
                   $"Pago: EFECTIVO en recepción\n" +
                   $"Plazo: hasta el {limitePago.Value:yyyy-MM-dd} a las {limitePago.Value:HH:mm}\n" +
                   "Esta reserva aún no está pagada. Si el plazo vence sin pago, la habitación se libera.";
        return $"Hotel Reservas · Comprobante {comprobante}\n" +
               $"Reserva: {codigo}\n" +
               $"Huésped: {h.Nombres} {h.Apellidos} ({h.TipoDocumento} {h.NumeroDocumento})\n" +
               $"Habitación: {habitacion} · {tipo} · {personas} persona{(personas > 1 ? "s" : "")}\n" +
               $"Entrada: {entrada:yyyy-MM-dd} · Salida: {salida:yyyy-MM-dd} ({noches} noche{(noches > 1 ? "s" : "")})\n" +
               $"Subtotal: {M(subtotal)}\n" +
               $"IVA 19 %: {(exento ? "exento (extranjero no residente, art. 481 E.T.)" : M(impuesto))}\n" +
               $"Precio neto: {M(total)}\n" +
               $"Pago: {medio}{(string.IsNullOrEmpty(referencia) ? "" : " · " + referencia)}\n" +
               "Presente este comprobante en recepción. Lo esperamos.";
    }
}
