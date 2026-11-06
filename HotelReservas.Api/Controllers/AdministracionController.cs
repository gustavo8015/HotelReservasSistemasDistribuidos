using HotelReservas.Api.Data;
using HotelReservas.Api.Seguridad;
using HotelReservas.Api.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HotelReservas.Api.Controllers;

// Contratos de entrada del módulo de administración
public record DatosTipo(string? Descripcion, string? Imagen, string? Comodidades = null);
public record NuevaFoto(string Ruta, string? Descripcion);
public record NuevaTarifa(int IdTipo, decimal ValorNoche, DateOnly VigenciaDesde);
public record DatosHabitacion(string Numero, int Piso, int IdTipo, string Estado);
public record DatosHuesped(string TipoDocumento, string NumeroDocumento, string Nombres, string Apellidos, string? Telefono, string? Correo, bool ExtranjeroNoResidente);
public record CambioReserva(DateOnly FechaEntrada, DateOnly FechaSalida, int NumeroPersonas, int IdHabitacion, string? VersionFila, string? Motivo);

/// <summary>
/// CU-11 / RF11 Gestión administrativa. Reservado al ADMINISTRADOR: tarifas (precios),
/// habitaciones, huéspedes y reservas. El recepcionista recibe 403 Forbidden en todo el controlador.
///
/// Reglas de conservación (RNF05): nada que tenga historial se borra en silencio. Una habitación
/// con reservas pasa a MANTENIMIENTO, un huésped con reservas no se elimina y una reserva solo se
/// borra definitivamente cuando ya está CANCELADA o FINALIZADA.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "ADMINISTRADOR")]
public class AdministracionController : ControllerBase
{
    private static readonly string[] EstadosActivos = { "CONFIRMADA", "EN_CASA" };
    private static readonly string[] EstadosHabitacion = { "DISPONIBLE", "OCUPADA", "MANTENIMIENTO" };
    private static readonly string[] TiposDocumento = { "CC", "CE", "PA", "TI" };

    private readonly HotelReservasContext _db;
    private readonly ServicioReservas _svc;
    public AdministracionController(HotelReservasContext db, ServicioReservas svc) { _db = db; _svc = svc; }

    private static string Recortar(string texto, int max) => texto.Length <= max ? texto : texto.Substring(0, max);

    // =====================================================================
    //  TIPOS DE HABITACIÓN
    // =====================================================================

    /// <summary>GET /api/admin/tipos: tipos con su capacidad, número de habitaciones y tarifa vigente.</summary>
    [HttpGet("tipos")]
    public async Task<ActionResult> Tipos()
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var lista = await _db.TiposHabitacion.AsNoTracking().OrderBy(t => t.IdTipo).Select(t => new
        {
            t.IdTipo,
            t.Nombre,
            t.Capacidad,
            t.Descripcion,
            t.Imagen,
            habitaciones = _db.Habitaciones.Count(h => h.IdTipo == t.IdTipo),
            tarifaVigente = _db.Tarifas
                .Where(x => x.IdTipo == t.IdTipo && x.VigenciaDesde <= hoy && (x.VigenciaHasta == null || x.VigenciaHasta >= hoy))
                .OrderByDescending(x => x.VigenciaDesde).Select(x => (decimal?)x.ValorNoche).FirstOrDefault(),
        }).ToListAsync();
        return Ok(lista);
    }

    /// <summary>
    /// PUT /api/admin/tipos/{id}: cambia la foto y la descripción que el portal muestra en el catálogo.
    /// La foto se guarda como ruta relativa a la aplicación web (por ejemplo img/habitaciones/suite.svg)
    /// o como URL completa; no se suben archivos al servidor.
    /// </summary>
    [HttpPut("tipos/{id:int}")]
    public async Task<ActionResult> ModificarTipo(int id, [FromBody] DatosTipo d)
    {
        var t = await _db.TiposHabitacion.FirstOrDefaultAsync(x => x.IdTipo == id);
        if (t == null) return NotFound(new { mensaje = "El tipo de habitación no existe." });

        var img = (d.Imagen ?? "").Trim();
        if (img.Length > 200) return BadRequest(new { mensaje = "La ruta de la imagen no puede superar los 200 caracteres." });
        if (img.Length > 0 && !img.StartsWith("img/") && !img.StartsWith("http://") && !img.StartsWith("https://"))
            return BadRequest(new { mensaje = "Use una ruta que empiece por img/ o una dirección http(s) completa." });

        t.Imagen = img.Length == 0 ? null : img;
        var desc = (d.Descripcion ?? "").Trim();
        if (desc.Length > 0) t.Descripcion = Recortar(desc, 255);
        var com = (d.Comodidades ?? "").Trim();
        if (com.Length > 0) t.Comodidades = Recortar(com, 400);
        await _db.SaveChangesAsync();
        return Ok(new { actualizado = true, mensaje = $"Se actualizó la presentación de {t.Nombre} en el catálogo." });
    }

    // =====================================================================
    //  GALERÍA DE FOTOS DEL TIPO DE HABITACIÓN (script 08)
    // =====================================================================

    /// <summary>GET /api/admin/tipos/{id}/fotos: fotos registradas para ese tipo, en el orden en que se muestran.</summary>
    [HttpGet("tipos/{id:int}/fotos")]
    public async Task<ActionResult> Fotos(int id)
    {
        var lista = await _db.FotosHabitacion.AsNoTracking().Where(f => f.IdTipo == id)
            .OrderBy(f => f.Orden).ThenBy(f => f.IdFoto)
            .Select(f => new { f.IdFoto, f.Ruta, f.Descripcion, f.Orden }).ToListAsync();
        return Ok(lista);
    }

    /// <summary>
    /// POST /api/admin/tipos/{id}/fotos: agrega una foto a la galería. La ruta puede ser un archivo
    /// dentro de wwwroot/img/habitaciones de la aplicación web o una dirección http completa.
    /// </summary>
    [HttpPost("tipos/{id:int}/fotos")]
    public async Task<ActionResult> AgregarFoto(int id, [FromBody] NuevaFoto n)
    {
        if (!await _db.TiposHabitacion.AnyAsync(t => t.IdTipo == id))
            return NotFound(new { mensaje = "El tipo de habitación no existe." });

        var ruta = (n.Ruta ?? "").Trim();
        if (ruta.Length == 0) return BadRequest(new { mensaje = "Escriba la ruta o la dirección de la foto." });
        if (ruta.Length > 200) return BadRequest(new { mensaje = "La ruta no puede superar los 200 caracteres." });
        if (!ruta.StartsWith("img/") && !ruta.StartsWith("http://") && !ruta.StartsWith("https://"))
            ruta = "img/habitaciones/" + ruta.TrimStart('/');
        if (await _db.FotosHabitacion.AnyAsync(f => f.IdTipo == id && f.Ruta == ruta))
            return Conflict(new { mensaje = "Esa foto ya está en la galería de este tipo." });

        var orden = await _db.FotosHabitacion.Where(f => f.IdTipo == id).Select(f => (int?)f.Orden).MaxAsync() ?? 0;
        var foto = new FotoHabitacion { IdTipo = id, Ruta = ruta, Descripcion = Recortar((n.Descripcion ?? "").Trim(), 120), Orden = orden + 1 };
        _db.FotosHabitacion.Add(foto);

        var t = await _db.TiposHabitacion.FirstAsync(x => x.IdTipo == id);
        if (string.IsNullOrWhiteSpace(t.Imagen)) t.Imagen = ruta;
        await _db.SaveChangesAsync();
        return Ok(new { foto.IdFoto, mensaje = $"Foto agregada a la galería de {t.Nombre}." });
    }

    /// <summary>DELETE /api/admin/tipos/{id}/fotos/{idFoto}: quita una foto de la galería.</summary>
    [HttpDelete("tipos/{id:int}/fotos/{idFoto:int}")]
    public async Task<ActionResult> EliminarFoto(int id, int idFoto)
    {
        var f = await _db.FotosHabitacion.FirstOrDefaultAsync(x => x.IdFoto == idFoto && x.IdTipo == id);
        if (f == null) return NotFound(new { mensaje = "La foto no existe en esta galería." });

        var t = await _db.TiposHabitacion.FirstAsync(x => x.IdTipo == id);
        _db.FotosHabitacion.Remove(f);
        await _db.SaveChangesAsync();

        if (t.Imagen == f.Ruta)
        {
            t.Imagen = await _db.FotosHabitacion.Where(x => x.IdTipo == id)
                .OrderBy(x => x.Orden).ThenBy(x => x.IdFoto).Select(x => x.Ruta).FirstOrDefaultAsync();
            await _db.SaveChangesAsync();
        }
        return Ok(new { eliminada = true, mensaje = "La foto salió de la galería." });
    }

    /// <summary>PUT /api/admin/tipos/{id}/fotos/{idFoto}/portada: esa foto pasa a encabezar la galería y el catálogo.</summary>
    [HttpPut("tipos/{id:int}/fotos/{idFoto:int}/portada")]
    public async Task<ActionResult> FotoPortada(int id, int idFoto)
    {
        var fotos = await _db.FotosHabitacion.Where(f => f.IdTipo == id).OrderBy(f => f.Orden).ThenBy(f => f.IdFoto).ToListAsync();
        var elegida = fotos.FirstOrDefault(f => f.IdFoto == idFoto);
        if (elegida == null) return NotFound(new { mensaje = "La foto no existe en esta galería." });

        var orden = 2;
        foreach (var f in fotos) f.Orden = f.IdFoto == idFoto ? 1 : orden++;
        var t = await _db.TiposHabitacion.FirstAsync(x => x.IdTipo == id);
        t.Imagen = elegida.Ruta;
        await _db.SaveChangesAsync();
        return Ok(new { mensaje = $"Esa foto encabeza ahora la ficha de {t.Nombre}." });
    }

    // =====================================================================
    //  TARIFAS (PRECIOS)
    // =====================================================================

    /// <summary>GET /api/admin/tarifas: histórico completo de precios por tipo, con la marca de la vigente.</summary>
    [HttpGet("tarifas")]
    public async Task<ActionResult> Tarifas([FromQuery] int? idTipo)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var q = _db.Tarifas.AsNoTracking().AsQueryable();
        if (idTipo.HasValue) q = q.Where(t => t.IdTipo == idTipo.Value);
        var lista = await q.OrderBy(t => t.IdTipo).ThenByDescending(t => t.VigenciaDesde).Select(t => new
        {
            t.IdTarifa,
            t.IdTipo,
            tipo = _db.TiposHabitacion.Where(x => x.IdTipo == t.IdTipo).Select(x => x.Nombre).FirstOrDefault() ?? "",
            t.ValorNoche,
            t.VigenciaDesde,
            t.VigenciaHasta,
            vigente = t.VigenciaDesde <= hoy && (t.VigenciaHasta == null || t.VigenciaHasta >= hoy),
            futura = t.VigenciaDesde > hoy,
        }).ToListAsync();
        return Ok(lista);
    }

    /// <summary>
    /// POST /api/admin/tarifas: registra un precio nuevo. La tarifa abierta del mismo tipo se cierra
    /// el día anterior, de modo que las reservas ya confirmadas conservan el precio con el que se vendieron.
    /// </summary>
    [HttpPost("tarifas")]
    public async Task<ActionResult> CrearTarifa([FromBody] NuevaTarifa n)
    {
        if (n.ValorNoche <= 0) return BadRequest(new { mensaje = "El valor por noche debe ser mayor que cero." });
        var tipo = await _db.TiposHabitacion.AsNoTracking().FirstOrDefaultAsync(t => t.IdTipo == n.IdTipo);
        if (tipo == null) return BadRequest(new { mensaje = "El tipo de habitación no existe." });
        if (await _db.Tarifas.AnyAsync(t => t.IdTipo == n.IdTipo && t.VigenciaDesde == n.VigenciaDesde))
            return Conflict(new { mensaje = $"Ya existe una tarifa de {tipo.Nombre} que empieza el {n.VigenciaDesde:yyyy-MM-dd}." });

        var posterior = await _db.Tarifas.AsNoTracking()
            .Where(t => t.IdTipo == n.IdTipo && t.VigenciaDesde > n.VigenciaDesde)
            .OrderBy(t => t.VigenciaDesde).FirstOrDefaultAsync();
        if (posterior != null)
            return Conflict(new { mensaje = $"Ya hay una tarifa de {tipo.Nombre} con vigencia desde el {posterior.VigenciaDesde:yyyy-MM-dd}. Elimínela antes de insertar una anterior." });

        await using var tx = await _db.Database.BeginTransactionAsync();
        var abierta = await _db.Tarifas.Where(t => t.IdTipo == n.IdTipo && t.VigenciaHasta == null)
            .OrderByDescending(t => t.VigenciaDesde).FirstOrDefaultAsync();
        if (abierta != null)
        {
            var cierre = n.VigenciaDesde.AddDays(-1);
            if (cierre < abierta.VigenciaDesde)
                return BadRequest(new { mensaje = $"La nueva vigencia debe ser posterior al {abierta.VigenciaDesde:yyyy-MM-dd}, fecha en que empieza la tarifa actual." });
            abierta.VigenciaHasta = cierre;
        }
        var t2 = new Tarifa { IdTipo = n.IdTipo, ValorNoche = n.ValorNoche, VigenciaDesde = n.VigenciaDesde, VigenciaHasta = null };
        _db.Tarifas.Add(t2);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return Created($"/api/admin/tarifas?idTipo={n.IdTipo}", new
        {
            t2.IdTarifa,
            mensaje = $"Nueva tarifa de {tipo.Nombre} por {t2.ValorNoche:N0} desde el {t2.VigenciaDesde:yyyy-MM-dd}." +
                      (abierta != null ? $" La anterior quedó cerrada el {abierta.VigenciaHasta:yyyy-MM-dd}." : "")
        });
    }

    /// <summary>PUT /api/admin/tarifas/{id}: corrige el valor de una tarifa que aún no ha empezado a regir.</summary>
    [HttpPut("tarifas/{id:int}")]
    public async Task<ActionResult> ModificarTarifa(int id, [FromBody] NuevaTarifa n)
    {
        var t = await _db.Tarifas.FirstOrDefaultAsync(x => x.IdTarifa == id);
        if (t == null) return NotFound(new { mensaje = "La tarifa no existe." });
        if (n.ValorNoche <= 0) return BadRequest(new { mensaje = "El valor por noche debe ser mayor que cero." });
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        if (t.VigenciaDesde <= hoy)
            return BadRequest(new { mensaje = "Una tarifa que ya rigió no se modifica: registre una nueva con la vigencia que corresponda." });
        t.ValorNoche = n.ValorNoche;
        await _db.SaveChangesAsync();
        return Ok(new { t.IdTarifa, t.ValorNoche, mensaje = "Tarifa actualizada." });
    }

    /// <summary>
    /// DELETE /api/admin/tarifas/{id}: elimina cualquier tarifa, vigente, futura o histórica.
    ///
    /// Dos salvaguardas. Primera: no se borra la última tarifa de un tipo que todavía tiene habitaciones,
    /// porque sin precio vigente la consulta de disponibilidad falla con el error 50003. Segunda: si la
    /// tarifa eliminada había cerrado a una anterior, esa anterior se reabre para que no quede un hueco
    /// de fechas sin precio.
    ///
    /// Las reservas ya vendidas no se ven afectadas: RESERVA guarda su propio subtotal, impuesto y
    /// valor_total, de modo que su precio no se recalcula a partir de la tabla TARIFA.
    /// </summary>
    [HttpDelete("tarifas/{id:int}")]
    public async Task<ActionResult> EliminarTarifa(int id)
    {
        var t = await _db.Tarifas.FirstOrDefaultAsync(x => x.IdTarifa == id);
        if (t == null) return NotFound(new { mensaje = "La tarifa no existe." });

        var tipoTarifa = t.IdTipo;
        var nombreTipo = await _db.TiposHabitacion.Where(x => x.IdTipo == tipoTarifa).Select(x => x.Nombre).FirstOrDefaultAsync() ?? "el tipo";
        var cuantas = await _db.Tarifas.CountAsync(x => x.IdTipo == tipoTarifa);
        var habitaciones = await _db.Habitaciones.CountAsync(h => h.IdTipo == tipoTarifa);
        if (cuantas <= 1 && habitaciones > 0)
            return Conflict(new { mensaje = $"Es la única tarifa de {nombreTipo} y hay {habitaciones} habitación(es) de ese tipo. Sin precio vigente la consulta de disponibilidad falla: registre primero la tarifa que la reemplaza." });

        var diaPrevio = t.VigenciaDesde.AddDays(-1);
        var fin = t.VigenciaHasta;
        await using var tx = await _db.Database.BeginTransactionAsync();
        var anterior = await _db.Tarifas.Where(x => x.IdTipo == tipoTarifa && x.VigenciaHasta == diaPrevio)
            .OrderByDescending(x => x.VigenciaDesde).FirstOrDefaultAsync();
        if (anterior != null) anterior.VigenciaHasta = fin;   // la anterior absorbe el periodo que queda libre
        _db.Tarifas.Remove(t);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        var cola = anterior == null
            ? " El periodo que cubría queda sin tarifa propia."
            : (fin == null ? " La tarifa anterior se reabrió sin fecha de cierre." : $" La tarifa anterior extendió su vigencia hasta el {fin:yyyy-MM-dd}.");
        return Ok(new { eliminada = true, mensaje = $"Tarifa de {nombreTipo} por {t.ValorNoche:N0} eliminada." + cola });
    }

    // =====================================================================
    //  HABITACIONES
    // =====================================================================

    /// <summary>GET /api/admin/habitaciones: inventario completo con su carga de reservas.</summary>
    [HttpGet("habitaciones")]
    public async Task<ActionResult> Habitaciones()
    {
        var lista = await _db.Habitaciones.AsNoTracking().OrderBy(h => h.Numero).Select(h => new
        {
            h.IdHabitacion,
            h.Numero,
            h.Piso,
            h.IdTipo,
            tipo = h.Tipo.Nombre,
            capacidad = h.Tipo.Capacidad,
            h.Estado,
            reservas = _db.Reservas.Count(r => r.IdHabitacion == h.IdHabitacion),
            reservasActivas = _db.Reservas.Count(r => r.IdHabitacion == h.IdHabitacion && EstadosActivos.Contains(r.Estado)),
        }).ToListAsync();
        return Ok(lista);
    }

    /// <summary>POST /api/admin/habitaciones: agrega una habitación al inventario. Número duplicado → 409.</summary>
    [HttpPost("habitaciones")]
    public async Task<ActionResult> CrearHabitacion([FromBody] DatosHabitacion d)
    {
        var numero = (d.Numero ?? "").Trim();
        if (numero.Length == 0) return BadRequest(new { mensaje = "El número de la habitación es obligatorio." });
        if (d.Piso < 0) return BadRequest(new { mensaje = "El piso no puede ser negativo." });
        if (!EstadosHabitacion.Contains(d.Estado)) return BadRequest(new { mensaje = "Estado no válido. Use DISPONIBLE, OCUPADA o MANTENIMIENTO." });
        if (!await _db.TiposHabitacion.AnyAsync(t => t.IdTipo == d.IdTipo)) return BadRequest(new { mensaje = "El tipo de habitación no existe." });
        if (await _db.Habitaciones.AnyAsync(h => h.Numero == numero)) return Conflict(new { mensaje = $"Ya existe la habitación {numero}." });

        var h2 = new Habitacion { Numero = numero, Piso = d.Piso, IdTipo = d.IdTipo, Estado = d.Estado };
        _db.Habitaciones.Add(h2);
        await _db.SaveChangesAsync();
        return Created("/api/admin/habitaciones", new { h2.IdHabitacion, mensaje = $"Habitación {h2.Numero} creada." });
    }

    /// <summary>PUT /api/admin/habitaciones/{id}: cambia número, piso, tipo o estado.</summary>
    [HttpPut("habitaciones/{id:int}")]
    public async Task<ActionResult> ModificarHabitacion(int id, [FromBody] DatosHabitacion d)
    {
        var h = await _db.Habitaciones.FirstOrDefaultAsync(x => x.IdHabitacion == id);
        if (h == null) return NotFound(new { mensaje = "La habitación no existe." });
        var numero = (d.Numero ?? "").Trim();
        if (numero.Length == 0) return BadRequest(new { mensaje = "El número de la habitación es obligatorio." });
        if (!EstadosHabitacion.Contains(d.Estado)) return BadRequest(new { mensaje = "Estado no válido. Use DISPONIBLE, OCUPADA o MANTENIMIENTO." });
        if (!await _db.TiposHabitacion.AnyAsync(t => t.IdTipo == d.IdTipo)) return BadRequest(new { mensaje = "El tipo de habitación no existe." });
        if (await _db.Habitaciones.AnyAsync(x => x.Numero == numero && x.IdHabitacion != id))
            return Conflict(new { mensaje = $"Ya existe otra habitación con el número {numero}." });

        var activas = await _db.Reservas.CountAsync(r => r.IdHabitacion == id && EstadosActivos.Contains(r.Estado));
        if (d.IdTipo != h.IdTipo && activas > 0)
            return Conflict(new { mensaje = $"No se puede cambiar el tipo: la habitación tiene {activas} reserva(s) activa(s) vendidas con la tarifa del tipo actual." });
        if (d.Estado == "MANTENIMIENTO" && activas > 0)
            return Conflict(new { mensaje = $"No se puede pasar a mantenimiento: la habitación tiene {activas} reserva(s) activa(s)." });

        h.Numero = numero; h.Piso = d.Piso; h.IdTipo = d.IdTipo; h.Estado = d.Estado;
        await _db.SaveChangesAsync();
        return Ok(new { h.IdHabitacion, h.Numero, h.Estado, mensaje = $"Habitación {h.Numero} actualizada." });
    }

    /// <summary>
    /// DELETE /api/admin/habitaciones/{id}: elimina la habitación si nunca se reservó. Si tiene historial
    /// la clave foránea FK_RESERVA_HABITACION impide borrarla, de modo que pasa a MANTENIMIENTO y deja de ofrecerse.
    /// </summary>
    [HttpDelete("habitaciones/{id:int}")]
    public async Task<ActionResult> EliminarHabitacion(int id)
    {
        var h = await _db.Habitaciones.FirstOrDefaultAsync(x => x.IdHabitacion == id);
        if (h == null) return NotFound(new { mensaje = "La habitación no existe." });

        var activas = await _db.Reservas.CountAsync(r => r.IdHabitacion == id && EstadosActivos.Contains(r.Estado));
        if (activas > 0) return Conflict(new { mensaje = $"La habitación {h.Numero} tiene {activas} reserva(s) activa(s). Cancélelas o espere el check-out." });

        var historial = await _db.Reservas.CountAsync(r => r.IdHabitacion == id);
        if (historial > 0)
        {
            h.Estado = "MANTENIMIENTO";
            await _db.SaveChangesAsync();
            return Ok(new { eliminada = false, desactivada = true, mensaje = $"La habitación {h.Numero} tiene {historial} reserva(s) en el histórico: se pasó a MANTENIMIENTO para conservar la trazabilidad y ya no se ofrece." });
        }
        _db.Habitaciones.Remove(h);
        await _db.SaveChangesAsync();
        return Ok(new { eliminada = true, desactivada = false, mensaje = $"La habitación {h.Numero} fue eliminada definitivamente." });
    }

    // =====================================================================
    //  HUÉSPEDES
    // =====================================================================

    /// <summary>GET /api/admin/huespedes?buscar=texto: lista de huéspedes con su número de reservas.</summary>
    [HttpGet("huespedes")]
    public async Task<ActionResult> Huespedes([FromQuery] string? buscar)
    {
        var q = _db.Huespedes.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var b = buscar.Trim();
            q = q.Where(h => h.NumeroDocumento.Contains(b) || h.Nombres.Contains(b) || h.Apellidos.Contains(b));
        }
        var lista = await q.OrderBy(h => h.Apellidos).ThenBy(h => h.Nombres).Take(200).Select(h => new
        {
            h.IdHuesped, h.TipoDocumento, h.NumeroDocumento, h.Nombres, h.Apellidos, h.Telefono, h.Correo, h.ExtranjeroNoResidente,
            reservas = _db.Reservas.Count(r => r.IdHuesped == h.IdHuesped),
            reservasActivas = _db.Reservas.Count(r => r.IdHuesped == h.IdHuesped && EstadosActivos.Contains(r.Estado)),
        }).ToListAsync();
        return Ok(lista);
    }

    /// <summary>PUT /api/admin/huespedes/{id}: corrige los datos del huésped, incluido el documento.</summary>
    [HttpPut("huespedes/{id:int}")]
    public async Task<ActionResult> ModificarHuesped(int id, [FromBody] DatosHuesped d)
    {
        var h = await _db.Huespedes.FirstOrDefaultAsync(x => x.IdHuesped == id);
        if (h == null) return NotFound(new { mensaje = "El huésped no existe." });
        var doc = (d.NumeroDocumento ?? "").Trim();
        if (doc.Length == 0 || string.IsNullOrWhiteSpace(d.Nombres) || string.IsNullOrWhiteSpace(d.Apellidos))
            return BadRequest(new { mensaje = "Documento, nombres y apellidos son obligatorios." });
        if (!TiposDocumento.Contains(d.TipoDocumento)) return BadRequest(new { mensaje = "Tipo de documento no válido. Use CC, CE, PA o TI." });
        if (await _db.Huespedes.AnyAsync(x => x.NumeroDocumento == doc && x.IdHuesped != id))
            return Conflict(new { mensaje = $"Ya existe otro huésped con el documento {doc}." });

        h.TipoDocumento = d.TipoDocumento;
        h.NumeroDocumento = doc;
        h.Nombres = d.Nombres.Trim();
        h.Apellidos = d.Apellidos.Trim();
        h.Telefono = string.IsNullOrWhiteSpace(d.Telefono) ? null : d.Telefono.Trim();
        h.Correo = string.IsNullOrWhiteSpace(d.Correo) ? null : d.Correo.Trim();
        h.ExtranjeroNoResidente = d.ExtranjeroNoResidente;
        await _db.SaveChangesAsync();
        return Ok(new { h.IdHuesped, mensaje = $"Datos de {h.Nombres} {h.Apellidos} actualizados." });
    }

    /// <summary>
    /// DELETE /api/admin/huespedes/{id}: elimina el huésped solo si nunca tuvo reservas.
    /// Con historial, FK_RESERVA_HUESPED lo impide y la operación se rechaza con una explicación.
    /// </summary>
    [HttpDelete("huespedes/{id:int}")]
    public async Task<ActionResult> EliminarHuesped(int id)
    {
        var h = await _db.Huespedes.FirstOrDefaultAsync(x => x.IdHuesped == id);
        if (h == null) return NotFound(new { mensaje = "El huésped no existe." });
        var reservas = await _db.Reservas.CountAsync(r => r.IdHuesped == id);
        if (reservas > 0)
            return Conflict(new { mensaje = $"{h.Nombres} {h.Apellidos} tiene {reservas} reserva(s) registrada(s). No se elimina para conservar la trazabilidad contable y de auditoría." });

        _db.Huespedes.Remove(h);
        await _db.SaveChangesAsync();
        return Ok(new { eliminado = true, mensaje = $"{h.Nombres} {h.Apellidos} fue eliminado definitivamente." });
    }

    // =====================================================================
    //  RESERVAS
    // =====================================================================

    /// <summary>
    /// PUT /api/admin/reservas/{codigo}: cambia fechas, número de personas o habitación de una reserva vigente.
    /// Revalida la disponibilidad y la capacidad, recalcula el precio con la tarifa vigente, ajusta el pago y
    /// el comprobante, y deja la huella en la bitácora. Usa concurrencia optimista con ROWVERSION.
    /// </summary>
    [HttpPut("reservas/{codigo}")]
    public async Task<ActionResult> ModificarReserva(string codigo, [FromBody] CambioReserva c)
    {
        var r = await _db.Reservas.FirstOrDefaultAsync(x => x.Codigo == codigo);
        if (r == null) return NotFound(new { mensaje = "La reserva no existe." });
        if (r.Estado != "CONFIRMADA" && r.Estado != "EN_CASA")
            return BadRequest(new { mensaje = $"Solo se modifican reservas CONFIRMADA o EN_CASA. Esta está {r.Estado}." });

        if (!string.IsNullOrEmpty(c.VersionFila))
        {
            byte[] esperada;
            try { esperada = Convert.FromBase64String(c.VersionFila); }
            catch (FormatException) { return BadRequest(new { mensaje = "La marca de versión enviada no es válida." }); }
            if (!esperada.SequenceEqual(r.VersionFila))
                return Conflict(new { codigo = 50012, mensaje = "La reserva fue modificada por otra sesión. Vuelva a consultarla." });
        }

        if (c.FechaSalida <= c.FechaEntrada) return BadRequest(new { codigo = 50000, mensaje = "La fecha de salida debe ser posterior a la fecha de entrada." });
        if (c.NumeroPersonas < 1) return BadRequest(new { mensaje = "El número de personas debe ser al menos uno." });

        var hab = await _db.Habitaciones.AsNoTracking().Where(h => h.IdHabitacion == c.IdHabitacion)
            .Select(h => new { h.IdHabitacion, h.Numero, h.Estado, h.IdTipo, Capacidad = h.Tipo.Capacidad, Tipo = h.Tipo.Nombre })
            .FirstOrDefaultAsync();
        if (hab == null) return BadRequest(new { mensaje = "La habitación no existe." });
        if (hab.Estado == "MANTENIMIENTO" && hab.IdHabitacion != r.IdHabitacion)
            return BadRequest(new { codigo = 50002, mensaje = $"La habitación {hab.Numero} está en mantenimiento." });
        if (c.NumeroPersonas > hab.Capacidad)
            return BadRequest(new { codigo = 50005, mensaje = $"La habitación {hab.Numero} admite máximo {hab.Capacidad} persona(s)." });

        bool ocupada = await _db.Reservas.AnyAsync(x => x.IdHabitacion == c.IdHabitacion && x.IdReserva != r.IdReserva
            && EstadosActivos.Contains(x.Estado) && x.FechaEntrada < c.FechaSalida && x.FechaSalida > c.FechaEntrada);
        if (ocupada) return Conflict(new { codigo = 50001, mensaje = $"La habitación {hab.Numero} ya está reservada en ese rango de fechas." });

        var tarifa = await _svc.TarifaVigenteAsync(hab.IdTipo, c.FechaEntrada);
        if (tarifa == null) return BadRequest(new { codigo = 50003, mensaje = $"No hay tarifa vigente para el tipo {hab.Tipo} en la fecha de entrada." });

        var noches = c.FechaSalida.DayNumber - c.FechaEntrada.DayNumber;
        var subtotal = Math.Round(noches * tarifa.Value, 2);
        var impuesto = ServicioReservas.CalcularIva(subtotal, r.ExentoIva);
        var total = subtotal + impuesto;

        var numAnterior = await _db.Habitaciones.AsNoTracking().Where(h => h.IdHabitacion == r.IdHabitacion)
            .Select(h => h.Numero).FirstOrDefaultAsync() ?? "-";
        var antes = $"hab {numAnterior} {r.FechaEntrada:yyyy-MM-dd}/{r.FechaSalida:yyyy-MM-dd} {r.NumeroPersonas}p {r.ValorTotal:N0}";
        var despues = $"hab {hab.Numero} {c.FechaEntrada:yyyy-MM-dd}/{c.FechaSalida:yyyy-MM-dd} {c.NumeroPersonas}p {total:N0}";

        await using var tx = await _db.Database.BeginTransactionAsync();
        r.IdHabitacion = hab.IdHabitacion;
        r.FechaEntrada = c.FechaEntrada;
        r.FechaSalida = c.FechaSalida;
        r.NumeroPersonas = c.NumeroPersonas;
        r.Subtotal = subtotal;
        r.TasaIva = r.ExentoIva ? 0m : ServicioReservas.TasaIva;
        r.Impuesto = impuesto;
        r.ValorTotal = total;                      // respeta CK_RESERVA_precio_neto: total = subtotal + impuesto

        var pagos = await _db.Pagos.Where(p => p.IdReserva == r.IdReserva && p.Estado == "APROBADO").ToListAsync();
        foreach (var p in pagos) p.Valor = total;
        var comp = await _db.Comprobantes.FirstOrDefaultAsync(x => x.IdReserva == r.IdReserva);
        if (comp != null) comp.Valor = total;

        var detalle = Recortar($"Modificación administrativa: {antes} → {despues}. {c.Motivo}".Trim(), 255);
        _db.Bitacora.Add(new BitacoraReserva
        {
            IdReserva = r.IdReserva, IdUsuario = User.IdUsuario(), Fecha = DateTime.Now,
            EstadoAnterior = r.Estado, EstadoNuevo = r.Estado, Detalle = detalle,
        });

        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { codigo = 50012, mensaje = "La reserva fue modificada por otra sesión. Vuelva a consultarla." }); }
        await tx.CommitAsync();

        return Ok(new
        {
            r.Codigo, r.Estado, habitacion = hab.Numero, r.FechaEntrada, r.FechaSalida, r.NumeroPersonas,
            r.Subtotal, r.Impuesto, precioNeto = r.ValorTotal, noches, tarifaNoche = tarifa.Value,
            versionFila = Convert.ToBase64String(r.VersionFila),
            mensaje = $"Reserva {r.Codigo} actualizada. Nuevo precio neto {total:N0}.",
        });
    }

    /// <summary>
    /// DELETE /api/admin/reservas/{codigo}: borra definitivamente una reserva ya cerrada, con sus pagos,
    /// su comprobante y su bitácora, en una sola transacción. Una reserva vigente debe cancelarse primero
    /// con PUT /api/reservas/{codigo}/cancelar, que conserva el registro.
    /// </summary>
    [HttpDelete("reservas/{codigo}")]
    public async Task<ActionResult> EliminarReserva(string codigo)
    {
        var r = await _db.Reservas.FirstOrDefaultAsync(x => x.Codigo == codigo);
        if (r == null) return NotFound(new { mensaje = "La reserva no existe." });
        if (r.Estado != "CANCELADA" && r.Estado != "FINALIZADA")
            return Conflict(new { mensaje = $"La reserva está {r.Estado}. Cancélela primero y luego podrá eliminarla; así queda la huella del cambio." });

        await using var tx = await _db.Database.BeginTransactionAsync();
        var bitacora = await _db.Bitacora.Where(b => b.IdReserva == r.IdReserva).ToListAsync();
        if (bitacora.Count > 0) _db.Bitacora.RemoveRange(bitacora);
        var pagos = await _db.Pagos.Where(p => p.IdReserva == r.IdReserva).ToListAsync();
        if (pagos.Count > 0) _db.Pagos.RemoveRange(pagos);
        var comp = await _db.Comprobantes.FirstOrDefaultAsync(x => x.IdReserva == r.IdReserva);
        if (comp != null) _db.Comprobantes.Remove(comp);
        _db.Reservas.Remove(r);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(new
        {
            eliminada = true,
            mensaje = $"Reserva {codigo} eliminada definitivamente, junto con {pagos.Count} pago(s), {(comp != null ? 1 : 0)} comprobante(s) y {bitacora.Count} movimiento(s) de bitácora.",
        });
    }
}
