using HotelReservas.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HotelReservas.Api.Servicios;

/// <summary>Excepción de negocio que el controlador traduce a un código HTTP (409 para 50001).</summary>
public class ErrorNegocio : Exception
{
    public int Codigo { get; }
    public ErrorNegocio(int codigo, string mensaje) : base(mensaje) => Codigo = codigo;
}

public record HabitacionDisponible(int IdHabitacion, string Numero, int Piso, string Tipo, int Capacidad,
    string Estado, bool Disponible, decimal TarifaNoche, int Noches, decimal Subtotal, decimal Iva, decimal PrecioNeto);

public record ResultadoConfirmacion(int IdReserva, string Codigo, string Comprobante, decimal Subtotal,
    decimal Impuesto, bool ExentoIva, decimal PrecioNeto);

/// <summary>
/// Lógica de negocio de reservas (ServicioReservas de la Fase II). Las consultas usan EF Core;
/// la confirmación delega en sp_ConfirmarReserva, que verifica la disponibilidad con bloqueo
/// (SERIALIZABLE + UPDLOCK/HOLDLOCK) y registra reserva, pago, comprobante y bitácora en una transacción.
/// </summary>
public class ServicioReservas
{
    public const decimal TasaIva = 0.19m;
    private readonly HotelReservasContext _db;
    public ServicioReservas(HotelReservasContext db) => _db = db;

    public static decimal CalcularIva(decimal subtotal, bool exento) => exento ? 0m : Math.Round(subtotal * TasaIva, 0);

    public async Task<decimal?> TarifaVigenteAsync(int idTipo, DateOnly fecha) =>
        await _db.Tarifas.Where(t => t.IdTipo == idTipo && t.VigenciaDesde <= fecha && (t.VigenciaHasta == null || t.VigenciaHasta >= fecha))
            .OrderByDescending(t => t.VigenciaDesde).Select(t => (decimal?)t.ValorNoche).FirstOrDefaultAsync();

    /// <summary>CU-03 / RF03: habitaciones de un tipo con su estado para el intervalo, sin reservas activas solapadas.</summary>
    public async Task<List<HabitacionDisponible>> ConsultarDisponibilidadAsync(string tipoNombre, DateOnly entrada, DateOnly salida, bool exentoIva = false)
    {
        if (salida <= entrada) throw new ErrorNegocio(50000, "La fecha de salida debe ser posterior a la fecha de entrada.");
        var tipo = await _db.TiposHabitacion.FirstOrDefaultAsync(t => t.Nombre == tipoNombre)
                   ?? throw new ErrorNegocio(50004, $"No existe el tipo de habitación '{tipoNombre}'.");
        var tarifa = await TarifaVigenteAsync(tipo.IdTipo, entrada) ?? throw new ErrorNegocio(50003, "No existe una tarifa vigente para el tipo de habitación.");
        var noches = salida.DayNumber - entrada.DayNumber;
        var subtotal = noches * tarifa;
        var iva = CalcularIva(subtotal, exentoIva);

        var ocupadas = await _db.Reservas
            .Where(r => (r.Estado == "CONFIRMADA" || r.Estado == "EN_CASA") && r.FechaEntrada < salida && r.FechaSalida > entrada)
            .Select(r => r.IdHabitacion).Distinct().ToListAsync();

        var habs = await _db.Habitaciones.Where(h => h.IdTipo == tipo.IdTipo).OrderBy(h => h.Numero).ToListAsync();
        return habs.Select(h =>
        {
            var estado = h.Estado == "MANTENIMIENTO" ? "MANTENIMIENTO" : ocupadas.Contains(h.IdHabitacion) ? "RESERVADA" : "DISPONIBLE";
            return new HabitacionDisponible(h.IdHabitacion, h.Numero, h.Piso, tipo.Nombre, tipo.Capacidad, estado,
                estado == "DISPONIBLE", tarifa, noches, subtotal, iva, subtotal + iva);
        }).ToList();
    }

    /// <summary>CU-04/05/06 + RF16: ejecuta sp_ConfirmarReserva (script 05). Error 50001 → habitación no disponible.</summary>
    /// <summary>
    /// CU-04/05/06. <paramref name="estado"/> permite crear la reserva ya confirmada (pago hecho)
    /// o retenida en PENDIENTE con un plazo en horas para pagar en recepción (script 18).
    /// </summary>
    public async Task<ResultadoConfirmacion> ConfirmarAsync(int idHuesped, int idHabitacion, int idUsuario, DateOnly entrada,
        DateOnly salida, int personas, string medioPago, bool? exentoIva, string estado = "CONFIRMADA", int? horasPlazo = null)
    {
        var cap = await _db.Habitaciones.Where(h => h.IdHabitacion == idHabitacion).Select(h => h.Tipo.Capacidad).FirstOrDefaultAsync();
        if (cap > 0 && personas > cap) throw new ErrorNegocio(50005, $"La habitación admite máximo {cap} persona(s).");
        var pId = new SqlParameter("@id_reserva_out", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        var pCod = new SqlParameter("@codigo_out", System.Data.SqlDbType.NVarChar, 12) { Direction = System.Data.ParameterDirection.Output };
        var pEx = new SqlParameter("@exento_iva", System.Data.SqlDbType.Bit) { Value = exentoIva.HasValue ? (object)exentoIva.Value : DBNull.Value };
        try
        {
            await _db.Database.ExecuteSqlRawAsync(
                "EXEC sp_ConfirmarReserva @id_huesped, @id_habitacion, @id_usuario, @fecha_entrada, @fecha_salida, @numero_personas, @medio_pago, @id_reserva_out OUTPUT, @codigo_out OUTPUT, @exento_iva, @estado, @horas_plazo",
                new SqlParameter("@id_huesped", idHuesped), new SqlParameter("@id_habitacion", idHabitacion),
                new SqlParameter("@id_usuario", idUsuario), new SqlParameter("@fecha_entrada", entrada.ToDateTime(TimeOnly.MinValue)),
                new SqlParameter("@fecha_salida", salida.ToDateTime(TimeOnly.MinValue)), new SqlParameter("@numero_personas", personas),
                new SqlParameter("@medio_pago", medioPago), pId, pCod, pEx,
                new SqlParameter("@estado", estado),
                new SqlParameter("@horas_plazo", horasPlazo.HasValue ? (object)horasPlazo.Value : DBNull.Value));
        }
        catch (SqlException ex) when (ex.Number >= 50000 && ex.Number < 50100)
        {
            throw new ErrorNegocio(ex.Number, ex.Message);   // el motor hizo ROLLBACK: no quedan datos parciales
        }
        var id = (int)pId.Value;
        var r = await _db.Reservas.Include(x => x.Comprobante).AsNoTracking().FirstAsync(x => x.IdReserva == id);
        return new ResultadoConfirmacion(id, (string)pCod.Value, r.Comprobante!.Numero, r.Subtotal, r.Impuesto, r.ExentoIva, r.ValorTotal);
    }

    /// <summary>
    /// Cancela las reservas retenidas cuyo plazo de pago ya venció y deja constancia en bitácora
    /// (sp_LiberarReservasVencidas, script 18). Se llama antes de consultar disponibilidad y antes
    /// de confirmar, de modo que una habitación retenida por quien no pagó vuelve a ofrecerse sola.
    /// Nunca interrumpe la operación en curso: si falla, la consulta sigue su camino.
    /// </summary>
    public async Task<int> LiberarVencidasAsync()
    {
        try
        {
            var p = new SqlParameter("@liberadas", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
            await _db.Database.ExecuteSqlRawAsync("EXEC sp_LiberarReservasVencidas @liberadas OUTPUT", p);
            return p.Value is int n ? n : 0;
        }
        catch (SqlException) { return 0; }
    }

    /// <summary>CU-08/09/10: cambio de estado con concurrencia optimista (ROWVERSION) y anotación en bitácora.</summary>
    public async Task<Reserva> CambiarEstadoAsync(string codigo, string nuevoEstado, int idUsuario, byte[]? versionEsperada, string? detalle)
    {
        var r = await _db.Reservas.Include(x => x.Habitacion).FirstOrDefaultAsync(x => x.Codigo == codigo)
                ?? throw new ErrorNegocio(50010, "La reserva no existe.");
        var permitidas = new Dictionary<string, string[]>
        {
            // PENDIENTE es la reserva retenida en efectivo: recepción la confirma al cobrar,
            // o se cancela, sea por el huésped o porque venció el plazo (script 18).
            ["PENDIENTE"] = new[] { "CONFIRMADA", "CANCELADA" },
            ["CONFIRMADA"] = new[] { "EN_CASA", "CANCELADA" },
            ["EN_CASA"] = new[] { "FINALIZADA" },
        };
        if (!permitidas.TryGetValue(r.Estado, out var destinos) || !destinos.Contains(nuevoEstado))
            throw new ErrorNegocio(50011, $"No es posible pasar de {r.Estado} a {nuevoEstado}.");
        if (versionEsperada != null && !versionEsperada.SequenceEqual(r.VersionFila))
            throw new ErrorNegocio(50012, "La reserva fue modificada por otra sesión. Vuelva a consultarla.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        var anterior = r.Estado;
        r.Estado = nuevoEstado;
        if (nuevoEstado == "EN_CASA") r.Habitacion.Estado = "OCUPADA";
        if (nuevoEstado == "FINALIZADA") r.Habitacion.Estado = "DISPONIBLE";
        if (nuevoEstado == "CANCELADA")
            foreach (var p in await _db.Pagos.Where(p => p.IdReserva == r.IdReserva).ToListAsync()) p.Estado = "ANULADO";
        if (anterior == "PENDIENTE")
        {
            // La retención termina: ya no hay plazo que vencer.
            r.LimitePago = null;
            // Al confirmarla, recepción cobró de verdad: ahí nace la fila de PAGO.
            if (nuevoEstado == "CONFIRMADA" && !await _db.Pagos.AnyAsync(p => p.IdReserva == r.IdReserva))
                _db.Pagos.Add(new Pago { IdReserva = r.IdReserva, Valor = r.ValorTotal, Medio = "EFECTIVO", Estado = "APROBADO", Fecha = DateTime.Now });
        }
        _db.Bitacora.Add(new BitacoraReserva { IdReserva = r.IdReserva, IdUsuario = idUsuario, Fecha = DateTime.Now, EstadoAnterior = anterior, EstadoNuevo = nuevoEstado, Detalle = detalle });
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new ErrorNegocio(50012, "La reserva fue modificada por otra sesión. Vuelva a consultarla."); }
        await tx.CommitAsync();
        return r;
    }
}
