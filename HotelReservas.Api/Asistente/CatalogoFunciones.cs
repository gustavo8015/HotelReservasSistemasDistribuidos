using System.Globalization;
using System.Text.Json;
using HotelReservas.Api.Data;
using HotelReservas.Api.Servicios;
using Microsoft.EntityFrameworkCore;

namespace HotelReservas.Api.Asistente;

/// <summary>
/// Catálogo de funciones de SOLO LECTURA que el asistente puede invocar (sección 13.3.3).
/// El modelo de IA nunca accede a la base de datos: elige una función y la API la ejecuta
/// reutilizando los mismos servicios de negocio de CU-03 y CU-12. Ninguna función devuelve
/// documentos de identidad ni datos de contacto de los huéspedes.
/// </summary>
public class CatalogoFunciones
{
    private readonly HotelReservasContext _db;
    private readonly ServicioReservas _reservas;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-CO");

    public CatalogoFunciones(HotelReservasContext db, ServicioReservas reservas) { _db = db; _reservas = reservas; }

    public static readonly IReadOnlyList<DescripcionFuncion> Descripciones = new List<DescripcionFuncion>
    {
        new("consultarDisponibilidad",
            "Lista las habitaciones de un tipo que están libres, reservadas o en mantenimiento para un intervalo de fechas, con la tarifa por noche y el precio neto (IVA 19 % incluido).",
            new { type = "object", properties = new {
                tipo = new { type = "string", @enum = new[] { "Sencilla", "Doble", "Triple", "Cuádruple" }, description = "Tipo de habitación" },
                entrada = new { type = "string", description = "Fecha de entrada en formato AAAA-MM-DD" },
                salida = new { type = "string", description = "Fecha de salida en formato AAAA-MM-DD (posterior a la entrada)" } },
                required = new[] { "tipo", "entrada", "salida" } }),
        new("consultarTarifa",
            "Devuelve la tarifa vigente por noche de un tipo de habitación y el desglose subtotal, IVA 19 % y precio neto para un número de noches.",
            new { type = "object", properties = new {
                tipo = new { type = "string", @enum = new[] { "Sencilla", "Doble", "Triple", "Cuádruple" } },
                noches = new { type = "integer", minimum = 1, description = "Número de noches; 1 si no se indica" },
                fecha = new { type = "string", description = "Fecha de referencia AAAA-MM-DD; hoy si no se indica" } },
                required = new[] { "tipo" } }),
        new("consultarReserva",
            "Devuelve el estado actual y el último movimiento de bitácora de una reserva identificada por su código (RSV seguido de nueve dígitos).",
            new { type = "object", properties = new { codigo = new { type = "string", description = "Código de la reserva, por ejemplo RSV000000045" } }, required = new[] { "codigo" } }),
        new("consultarLlegadas",
            "Cuenta y lista las reservas confirmadas cuya fecha de entrada es la fecha indicada (llegadas programadas).",
            new { type = "object", properties = new { fecha = new { type = "string", description = "Fecha AAAA-MM-DD; mañana si el usuario dice 'mañana'" } }, required = new[] { "fecha" } }),
        new("consultarReservasConfirmadas",
            "Cuenta las reservas por estado (confirmadas, en casa, finalizadas, canceladas) y la ocupación actual de las habitaciones.",
            new { type = "object", properties = new { } }),

        // --- Funciones del huésped (perfil CLIENTE): solo sus propios datos ---
        new("consultarMisReservas",
            "Lista las reservas del huésped que hace la consulta, con su estado, las fechas, el valor, si falta pagarlas y hasta cuándo, y si todavía puede cancelarlas. No recibe parámetros: el huésped se toma de la sesión.",
            new { type = "object", properties = new { } }),
        new("consultarPoliticas",
            "Explica las condiciones de pago y de cancelación del hotel: cuántas horas hay para pagar una reserva en efectivo, qué pasa si vence el plazo, hasta cuándo se puede cancelar en línea, y las horas de entrada y salida.",
            new { type = "object", properties = new { } }),

        // --- Funciones del personal nacidas con el plazo de pago y la cancelación ---
        new("consultarReservasSinPagar",
            "Lista las reservas retenidas en efectivo que todavía no se han pagado, con su hora límite y cuántas horas faltan para que venza.",
            new { type = "object", properties = new { } }),
        new("consultarPlazosPorVencer",
            "Lista las reservas sin pagar cuyo plazo vence dentro del número de horas indicado, para llamar al huésped antes de liberar la habitación.",
            new { type = "object", properties = new {
                horas = new { type = "integer", minimum = 1, description = "Ventana en horas; 6 si no se indica" } } }),
        new("consultarCancelaciones",
            "Cuenta y lista las reservas canceladas en una fecha, distinguiendo las que canceló el propio huésped desde el portal de las que canceló el hotel o el vencimiento del plazo.",
            new { type = "object", properties = new {
                fecha = new { type = "string", description = "Fecha AAAA-MM-DD; hoy si no se indica" } } }),
    };

    /// <summary>
    /// Catálogo visible para cada perfil. Es la segunda barrera del asistente: el modelo solo
    /// recibe las funciones de su perfil, y el orquestador vuelve a validar contra esta misma
    /// lista antes de ejecutar, de modo que un cliente no puede obtener datos de otros huéspedes
    /// ni cifras de ocupación del hotel aunque lo pida de forma ingeniosa.
    /// </summary>
    public static IReadOnlyList<DescripcionFuncion> ParaRol(string? rol)
    {
        var r = (rol ?? "").Trim().ToUpperInvariant();
        string[] permitidas = r switch
        {
            "ADMINISTRADOR" or "RECEPCIONISTA" => new[]
            {
                "consultarDisponibilidad", "consultarTarifa", "consultarReserva", "consultarLlegadas",
                "consultarReservasConfirmadas", "consultarReservasSinPagar", "consultarPlazosPorVencer",
                "consultarCancelaciones", "consultarPoliticas",
            },
            "CLIENTE" => new[] { "consultarDisponibilidad", "consultarTarifa", "consultarMisReservas", "consultarPoliticas" },
            // Visitante sin cuenta: solo lo que ya es público en el catálogo del portal.
            _ => new[] { "consultarDisponibilidad", "consultarTarifa", "consultarPoliticas" },
        };
        return Descripciones.Where(d => permitidas.Contains(d.Nombre)).ToList();
    }

    /// <summary>
    /// Ejecuta una función del catálogo. <paramref name="idUsuario"/> hace falta para las funciones
    /// del huésped, que solo pueden devolver sus propias reservas.
    /// </summary>
    public async Task<ResultadoFuncion> EjecutarAsync(string funcion, JsonElement? p, CancellationToken ct, int idUsuario = 0)
    {
        string S(string k, string? def = null) => p.HasValue && p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : def ?? "";
        int I(string k, int def) => int.TryParse(S(k), out var n) ? n : def;
        DateOnly F(string k, DateOnly def) => DateOnly.TryParse(S(k), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : def;
        var hoy = DateOnly.FromDateTime(DateTime.Now);

        switch (funcion)
        {
            case "consultarDisponibilidad":
            {
                var tipo = NormalizarTipo(S("tipo", "Doble"));
                var entrada = F("entrada", hoy.AddDays(1)); var salida = F("salida", entrada.AddDays(1));
                var lista = await _reservas.ConsultarDisponibilidadAsync(tipo, entrada, salida);
                var libres = lista.Where(h => h.Disponible).ToList();
                var datos = new { tipo, entrada, salida, noches = lista.FirstOrDefault()?.Noches ?? 0,
                    tarifaNoche = lista.FirstOrDefault()?.TarifaNoche ?? 0, precioNeto = lista.FirstOrDefault()?.PrecioNeto ?? 0,
                    disponibles = libres.Select(h => h.Numero), noDisponibles = lista.Where(h => !h.Disponible).Select(h => new { h.Numero, h.Estado }) };
                var ocupadas = lista.Where(h => !h.Disponible).ToList();
                var resumen = libres.Count == 0
                    ? $"Tipo {tipo}, del {entrada:yyyy-MM-dd} al {salida:yyyy-MM-dd}: no hay habitaciones libres. " +
                      $"Tarifa {Moneda(datos.tarifaNoche)} por noche."
                    : $"Tipo {tipo}, del {entrada:yyyy-MM-dd} al {salida:yyyy-MM-dd}: {libres.Count} disponibles ({string.Join(", ", libres.Select(h => h.Numero))}). " +
                      (ocupadas.Count == 0
                          ? ""
                          : $"No disponibles: {string.Join(", ", ocupadas.Select(h => h.Numero + " (" + h.Estado.ToLower() + ")"))}. ") +
                      $"Tarifa {Moneda(datos.tarifaNoche)} por noche; precio neto por {Noches(datos.noches)} {Moneda(datos.precioNeto)} (IVA 19 % incluido).";
                return new(funcion, datos, resumen);
            }
            case "consultarTarifa":
            {
                var tipo = NormalizarTipo(S("tipo", "Doble")); var noches = Math.Max(1, I("noches", 1)); var fecha = F("fecha", hoy);
                var t = await _db.TiposHabitacion.FirstOrDefaultAsync(x => x.Nombre == tipo, ct) ?? throw new ErrorNegocio(50004, "Tipo de habitación no válido.");
                var tarifa = await _reservas.TarifaVigenteAsync(t.IdTipo, fecha) ?? throw new ErrorNegocio(50003, "No hay tarifa vigente.");
                var subtotal = tarifa * noches; var iva = ServicioReservas.CalcularIva(subtotal, false);
                var datos = new { tipo, noches, tarifaNoche = tarifa, subtotal, iva, precioNeto = subtotal + iva, exentoExtranjeroNoResidente = subtotal };
                return new(funcion, datos, $"Tarifa vigente {tipo}: {Moneda(tarifa)} por noche. Por {Noches(noches)}: subtotal {Moneda(subtotal)} + IVA 19 % {Moneda(iva)} = precio neto {Moneda(subtotal + iva)}. Huésped extranjero no residente: exento de IVA, pagaría {Moneda(subtotal)}.");
            }
            case "consultarReserva":
            {
                var codigo = S("codigo").ToUpperInvariant();
                var r = await _db.Reservas.Include(x => x.Habitacion).ThenInclude(h => h.Tipo).AsNoTracking().FirstOrDefaultAsync(x => x.Codigo == codigo, ct);
                if (r == null) return new(funcion, new { codigo, existe = false }, $"No existe ninguna reserva con el código {codigo}.");
                var ultimo = await _db.Bitacora.Where(b => b.IdReserva == r.IdReserva).OrderByDescending(b => b.Fecha).Select(b => new { b.Fecha, b.EstadoAnterior, b.EstadoNuevo }).FirstOrDefaultAsync(ct);
                var datos = new { codigo, existe = true, estado = r.Estado, habitacion = r.Habitacion.Numero, tipo = r.Habitacion.Tipo.Nombre, entrada = r.FechaEntrada, salida = r.FechaSalida, precioNeto = r.ValorTotal, ultimoMovimiento = ultimo };
                return new(funcion, datos, $"Reserva {codigo}: estado {r.Estado}, habitación {r.Habitacion.Numero} ({r.Habitacion.Tipo.Nombre}), del {r.FechaEntrada:yyyy-MM-dd} al {r.FechaSalida:yyyy-MM-dd}, precio neto {Moneda(r.ValorTotal)}. Último movimiento: {(ultimo == null ? "sin registros" : $"{ultimo.EstadoAnterior ?? "-"} → {ultimo.EstadoNuevo} el {ultimo.Fecha:yyyy-MM-dd HH:mm}")}.");
            }
            case "consultarLlegadas":
            {
                var fecha = F("fecha", hoy.AddDays(1));
                var llegadas = await _db.Reservas.Include(x => x.Habitacion).ThenInclude(h => h.Tipo).AsNoTracking()
                    .Where(x => x.Estado == "CONFIRMADA" && x.FechaEntrada == fecha).OrderBy(x => x.Habitacion.Numero)
                    .Select(x => new { x.Codigo, habitacion = x.Habitacion.Numero, tipo = x.Habitacion.Tipo.Nombre, x.NumeroPersonas, salida = x.FechaSalida }).ToListAsync(ct);
                return new(funcion, new { fecha, total = llegadas.Count, llegadas },
                    $"Para el {fecha:yyyy-MM-dd} hay {llegadas.Count} llegada(s) programada(s): {string.Join("; ", llegadas.Select(l => $"{l.Codigo} hab. {l.habitacion} ({l.tipo}, {l.NumeroPersonas} pers.)"))}.");
            }
            case "consultarReservasConfirmadas":
            {
                var porEstado = await _db.Reservas.GroupBy(x => x.Estado).Select(g => new { estado = g.Key, total = g.Count() }).ToListAsync(ct);
                var habs = await _db.Habitaciones.GroupBy(h => h.Estado).Select(g => new { estado = g.Key, total = g.Count() }).ToListAsync(ct);
                int N(string e) => porEstado.FirstOrDefault(x => x.estado == e)?.total ?? 0;
                return new(funcion, new { reservas = porEstado, habitaciones = habs },
                    $"Reservas: {N("CONFIRMADA")} confirmadas, {N("EN_CASA")} en casa, {N("FINALIZADA")} finalizadas, {N("CANCELADA")} canceladas. Habitaciones: {string.Join(", ", habs.Select(h => h.total + " " + h.estado.ToLower()))}.");
            }
            case "consultarMisReservas":
            {
                // El huésped sale de la sesión, nunca de la pregunta: así el asistente no puede
                // ser convencido de mostrar las reservas de otra persona.
                var u = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.IdUsuario == idUsuario, ct);
                if (u?.IdHuesped == null)
                    return new(funcion, new { reservas = Array.Empty<object>() },
                        "La cuenta que consulta no tiene ficha de huésped, de modo que no hay reservas asociadas.");
                var ahora = DateTime.Now;
                var mias = await _db.Reservas.Include(x => x.Habitacion).ThenInclude(h => h.Tipo).AsNoTracking()
                    .Where(x => x.IdHuesped == u.IdHuesped).OrderByDescending(x => x.IdReserva).Take(10)
                    .Select(x => new { x.Codigo, x.Estado, habitacion = x.Habitacion.Numero, tipo = x.Habitacion.Tipo.Nombre,
                                       entrada = x.FechaEntrada, salida = x.FechaSalida, precioNeto = x.ValorTotal, x.LimitePago })
                    .ToListAsync(ct);
                if (mias.Count == 0) return new(funcion, new { reservas = mias }, "El huésped no tiene reservas registradas.");
                var lineas = mias.Select(x =>
                {
                    var plazo = x.Estado == "PENDIENTE" && x.LimitePago.HasValue
                        ? $", sin pagar: tiene plazo hasta el {x.LimitePago:yyyy-MM-dd} a las {x.LimitePago:HH:mm}"
                        : "";
                    var horaEntrada = x.entrada.ToDateTime(new TimeOnly(15, 0));
                    var cancelable = (x.Estado == "CONFIRMADA" || x.Estado == "PENDIENTE") && (horaEntrada - ahora).TotalHours >= 24
                        ? ", todavía se puede cancelar en línea" : "";
                    return $"{x.Codigo} ({x.Estado.ToLower()}), habitación {x.habitacion} {x.tipo}, del {x.entrada:yyyy-MM-dd} al {x.salida:yyyy-MM-dd}, {Moneda(x.precioNeto)}{plazo}{cancelable}";
                });
                return new(funcion, new { total = mias.Count, reservas = mias },
                    $"El huésped tiene {mias.Count} reserva(s): {string.Join("; ", lineas)}.");
            }
            case "consultarPoliticas":
            {
                var datos = new
                {
                    horasPlazoPago = 24,
                    horasMinimasCancelacion = 24,
                    entrada = "3:00 p. m.",
                    salida = "12:00 m.",
                    iva = "19 %",
                    exencion = "El huésped extranjero no residente está exento del IVA, según el artículo 481 del Estatuto Tributario.",
                };
                return new(funcion, datos,
                    "Condiciones del hotel: quien reserva en efectivo tiene 24 horas para pagar en recepción; si el plazo vence sin pago, " +
                    "la reserva se cancela sola y la habitación se libera. Quien paga con tarjeta queda confirmado de inmediato. " +
                    "La cancelación en línea es libre hasta 24 horas antes de la llegada, con devolución del valor pagado; dentro de ese último día " +
                    "la cancelación la hace recepción. La entrada es desde las 3:00 p. m. y la salida hasta las 12:00 m. " +
                    "Los precios incluyen el IVA del 19 %, salvo el huésped extranjero no residente, exento según el artículo 481 del Estatuto Tributario.");
            }
            case "consultarReservasSinPagar":
            {
                var ahora = DateTime.Now;
                var pendientes = await _db.Reservas.Include(x => x.Habitacion).ThenInclude(h => h.Tipo).AsNoTracking()
                    .Where(x => x.Estado == "PENDIENTE" && x.LimitePago != null && x.LimitePago > ahora)
                    .OrderBy(x => x.LimitePago)
                    .Select(x => new { x.Codigo, habitacion = x.Habitacion.Numero, tipo = x.Habitacion.Tipo.Nombre,
                                       entrada = x.FechaEntrada, precioNeto = x.ValorTotal, x.LimitePago })
                    .ToListAsync(ct);
                var lineas = pendientes.Select(x => $"{x.Codigo} hab. {x.habitacion} ({x.tipo}), {Moneda(x.precioNeto)}, vence el {x.LimitePago:yyyy-MM-dd} a las {x.LimitePago:HH:mm}");
                return new(funcion, new { total = pendientes.Count, reservas = pendientes },
                    pendientes.Count == 0
                        ? "No hay reservas retenidas sin pagar en este momento."
                        : $"Hay {pendientes.Count} reserva(s) sin pagar: {string.Join("; ", lineas)}.");
            }
            case "consultarPlazosPorVencer":
            {
                var horas = Math.Max(1, I("horas", 6));
                var ahora = DateTime.Now; var tope = ahora.AddHours(horas);
                var porVencer = await _db.Reservas.Include(x => x.Habitacion).AsNoTracking()
                    .Where(x => x.Estado == "PENDIENTE" && x.LimitePago != null && x.LimitePago > ahora && x.LimitePago <= tope)
                    .OrderBy(x => x.LimitePago)
                    .Select(x => new { x.Codigo, habitacion = x.Habitacion.Numero, precioNeto = x.ValorTotal, x.LimitePago })
                    .ToListAsync(ct);
                var lineas = porVencer.Select(x => $"{x.Codigo} hab. {x.habitacion}, {Moneda(x.precioNeto)}, vence a las {x.LimitePago:HH:mm}");
                return new(funcion, new { horas, total = porVencer.Count, reservas = porVencer },
                    porVencer.Count == 0
                        ? $"Ninguna reserva vence su plazo de pago en las próximas {horas} hora(s)."
                        : $"En las próximas {horas} hora(s) vence(n) {porVencer.Count} reserva(s): {string.Join("; ", lineas)}.");
            }
            case "consultarCancelaciones":
            {
                var fecha = F("fecha", hoy);
                var desde = fecha.ToDateTime(TimeOnly.MinValue); var hasta = desde.AddDays(1);
                // BITACORA_RESERVA no tiene propiedad de navegación hacia RESERVA, así que se une a mano.
                var movimientos = await (from b in _db.Bitacora.AsNoTracking()
                                         join r in _db.Reservas.AsNoTracking() on b.IdReserva equals r.IdReserva
                                         where b.EstadoNuevo == "CANCELADA" && b.Fecha >= desde && b.Fecha < hasta
                                         orderby b.Fecha
                                         select new { codigo = r.Codigo, b.Fecha, b.EstadoAnterior, b.Detalle })
                                        .ToListAsync(ct);
                var porHuesped = movimientos.Count(m => (m.Detalle ?? "").Contains("huésped", StringComparison.OrdinalIgnoreCase));
                var porVencimiento = movimientos.Count(m => (m.Detalle ?? "").Contains("plazo", StringComparison.OrdinalIgnoreCase));
                var porHotel = movimientos.Count - porHuesped - porVencimiento;
                return new(funcion, new { fecha, total = movimientos.Count, porHuesped, porVencimiento, porHotel, movimientos },
                    movimientos.Count == 0
                        ? $"El {fecha:yyyy-MM-dd} no se canceló ninguna reserva."
                        : $"El {fecha:yyyy-MM-dd} se cancelaron {movimientos.Count} reserva(s): {porHuesped} por el propio huésped desde el portal, " +
                          $"{porVencimiento} por vencimiento del plazo de pago y {porHotel} por el hotel. Códigos: {string.Join(", ", movimientos.Select(m => m.codigo))}.");
            }
            default:
                throw new ErrorNegocio(50020, $"La función '{funcion}' no está en el catálogo de solo lectura.");
        }
    }

    public static string NormalizarTipo(string t)
    {
        t = t.Trim().ToLowerInvariant();
        if (t.StartsWith("sen")) return "Sencilla";
        if (t.StartsWith("cua") || t.StartsWith("cuá")) return "Cuádruple";
        if (t.StartsWith("tri")) return "Triple";
        return "Doble";
    }

    public static string Moneda(decimal v) => "$ " + v.ToString("N0", Es);

    /// <summary>Evita el "noche(s)" de las plantillas: el asistente le habla a una persona.</summary>
    public static string Noches(int n) => n == 1 ? "1 noche" : n + " noches";
}
