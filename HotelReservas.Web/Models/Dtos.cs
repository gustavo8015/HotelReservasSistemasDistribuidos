namespace HotelReservas.Web.Models;

// Contratos JSON de HotelReservas.Api (camelCase). Se deserializan sin distinguir mayúsculas.

public record SolicitudLogin(string Correo, string Clave);
public record RespuestaLogin(string Token, DateTime Expira, string Nombre, string Rol, int? IdHuesped = null, string? Documento = null);

public class HabitacionDisponible
{
    public int IdHabitacion { get; set; }
    public string Numero { get; set; } = "";
    public int Piso { get; set; }
    public string Tipo { get; set; } = "";
    public int Capacidad { get; set; }
    public string Estado { get; set; } = "";
    public bool Disponible { get; set; }
    public decimal TarifaNoche { get; set; }
    public int Noches { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal PrecioNeto { get; set; }
}

public record SolicitudReserva(int IdHuesped, int IdHabitacion, DateOnly FechaEntrada, DateOnly FechaSalida, int NumeroPersonas, string MedioPago, bool? ExentoIva);
public record ResultadoConfirmacion(int IdReserva, string Codigo, string Comprobante, decimal Subtotal, decimal Impuesto, bool ExentoIva, decimal PrecioNeto);

public class ReservaResumen
{
    public string Codigo { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Habitacion { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Huesped { get; set; } = "";
    public DateOnly FechaEntrada { get; set; }
    public DateOnly FechaSalida { get; set; }
    public int NumeroPersonas { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Impuesto { get; set; }
    public bool ExentoIva { get; set; }
    public decimal PrecioNeto { get; set; }
    public string? Comprobante { get; set; }
    public string VersionFila { get; set; } = "";
}

public class HuespedDetalle
{
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public string Documento { get; set; } = "";
}
public class PagoDetalle { public decimal Valor { get; set; } public string Medio { get; set; } = ""; public DateTime Fecha { get; set; } public string Estado { get; set; } = ""; }
public class ComprobanteDetalle { public string Numero { get; set; } = ""; public DateTime FechaEmision { get; set; } public decimal Valor { get; set; } }
public class BitacoraDetalle { public DateTime Fecha { get; set; } public string Usuario { get; set; } = ""; public string? EstadoAnterior { get; set; } public string EstadoNuevo { get; set; } = ""; public string? Detalle { get; set; } }

public class ReservaDetalle
{
    public string Codigo { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Habitacion { get; set; } = "";
    public string Tipo { get; set; } = "";
    public HuespedDetalle Huesped { get; set; } = new();
    public DateOnly FechaEntrada { get; set; }
    public DateOnly FechaSalida { get; set; }
    public int NumeroPersonas { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TasaIva { get; set; }
    public decimal Impuesto { get; set; }
    public bool ExentoIva { get; set; }
    public decimal PrecioNeto { get; set; }
    public List<PagoDetalle> Pagos { get; set; } = new();
    public ComprobanteDetalle? Comprobante { get; set; }
    public List<BitacoraDetalle> Bitacora { get; set; } = new();
    public string VersionFila { get; set; } = "";
}

public class UltimaEstadia
{
    public string Tipo { get; set; } = "";
    public int IdTipo { get; set; }
    public string Habitacion { get; set; } = "";
    public int Personas { get; set; }
    public int Noches { get; set; }
    public string? MedioPago { get; set; }
    public DateOnly Fecha { get; set; }
}

public class HuespedPerfil
{
    public int IdHuesped { get; set; }
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public string TipoDocumento { get; set; } = "";
    public string NumeroDocumento { get; set; } = "";
    public string? Telefono { get; set; }
    public bool ExtranjeroNoResidente { get; set; }
    public int Estadias { get; set; }
    public bool EsFrecuente { get; set; }
    public UltimaEstadia? UltimaEstadia { get; set; }
    public string NombreCompleto => $"{Nombres} {Apellidos}";
}

public class NuevoHuesped
{
    public string TipoDocumento { get; set; } = "CC";
    public string NumeroDocumento { get; set; } = "";
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public bool ExtranjeroNoResidente { get; set; }
}

public class ComprobanteCompartir
{
    public string Comprobante { get; set; } = "";
    public string Reserva { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Texto { get; set; } = "";
    public string? EnlaceWhatsApp { get; set; }
}

public record SolicitudAsistente(string Consulta);
public class RespuestaAsistente
{
    public string Respuesta { get; set; } = "";
    public string? Funcion { get; set; }
    public string Interprete { get; set; } = "";
    public bool Exitosa { get; set; }
    public int DuracionMs { get; set; }
}
public class EstadoAsistente
{
    /// <summary>Intérprete activo: el proveedor configurado, o REGLAS si no hay ninguno.</summary>
    public string Interprete { get; set; } = "";
    public bool AzureOpenAIConfigurado { get; set; }
    /// <summary>true cuando hay un modelo externo configurado, sea cual sea el proveedor.</summary>
    public bool ModeloConfigurado { get; set; }
    public string Proveedor { get; set; } = "";
    /// <summary>Perfil con el que la API atendió la consulta: ADMINISTRADOR, CLIENTE o VISITANTE.</summary>
    public string Perfil { get; set; } = "";
    public int Funciones { get; set; }
    /// <summary>Versión del asistente que trae la API que está respondiendo.</summary>
    public string Version { get; set; } = "";
}

// CU-15 Gestionar usuarios
public class UsuarioResumen
{
    public int IdUsuario { get; set; }
    public string Nombre { get; set; } = "";
    public string Correo { get; set; } = "";
    public string Rol { get; set; } = "";
    public bool Activo { get; set; }
    public int Reservas { get; set; }
    public int Movimientos { get; set; }
}
public record NuevoUsuario(string Nombre, string Correo, string Clave);
public class ResultadoUsuario { public bool Eliminado { get; set; } public bool Desactivado { get; set; } public string Mensaje { get; set; } = ""; }

public class ErrorApi { public int Codigo { get; set; } public string Mensaje { get; set; } = ""; }

// =====================================================================
//  CU-11 / RF11 Administración: precios, habitaciones, huéspedes y reservas
//  (recursos bajo api/admin, reservados al rol ADMINISTRADOR)
// =====================================================================

public class TipoResumen
{
    public int IdTipo { get; set; }
    public string Nombre { get; set; } = "";
    public int Capacidad { get; set; }
    public string? Descripcion { get; set; }
    public int Habitaciones { get; set; }
    public decimal? TarifaVigente { get; set; }
    /// <summary>Foto que el portal muestra en el catálogo (script 07).</summary>
    public string? Imagen { get; set; }
}

public class TarifaResumen
{
    public int IdTarifa { get; set; }
    public int IdTipo { get; set; }
    public string Tipo { get; set; } = "";
    public decimal ValorNoche { get; set; }
    public DateOnly VigenciaDesde { get; set; }
    public DateOnly? VigenciaHasta { get; set; }
    public bool Vigente { get; set; }
    public bool Futura { get; set; }
}

public class NuevaTarifa
{
    public int IdTipo { get; set; } = 1;
    public decimal ValorNoche { get; set; }
    public DateOnly VigenciaDesde { get; set; } = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
}

public class HabitacionAdmin
{
    public int IdHabitacion { get; set; }
    public string Numero { get; set; } = "";
    public int Piso { get; set; }
    public int IdTipo { get; set; }
    public string Tipo { get; set; } = "";
    public int Capacidad { get; set; }
    public string Estado { get; set; } = "DISPONIBLE";
    public int Reservas { get; set; }
    public int ReservasActivas { get; set; }
}

public class DatosHabitacion
{
    public string Numero { get; set; } = "";
    public int Piso { get; set; } = 1;
    public int IdTipo { get; set; } = 1;
    public string Estado { get; set; } = "DISPONIBLE";
}

public class HuespedAdmin
{
    public int IdHuesped { get; set; }
    public string TipoDocumento { get; set; } = "CC";
    public string NumeroDocumento { get; set; } = "";
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public bool ExtranjeroNoResidente { get; set; }
    public int Reservas { get; set; }
    public int ReservasActivas { get; set; }
    public string NombreCompleto => $"{Nombres} {Apellidos}";
}

public class DatosHuesped
{
    public string TipoDocumento { get; set; } = "CC";
    public string NumeroDocumento { get; set; } = "";
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public bool ExtranjeroNoResidente { get; set; }
}

public class CambioReserva
{
    public DateOnly FechaEntrada { get; set; }
    public DateOnly FechaSalida { get; set; }
    public int NumeroPersonas { get; set; } = 1;
    public int IdHabitacion { get; set; }
    public string? VersionFila { get; set; }
    public string? Motivo { get; set; }
}

/// <summary>Respuesta común de los recursos de administración: todos devuelven al menos un mensaje.</summary>
public class ResultadoAdmin
{
    public string Mensaje { get; set; } = "";
    public bool Eliminada { get; set; }
    public bool Eliminado { get; set; }
    public bool Desactivada { get; set; }
    public decimal PrecioNeto { get; set; }
    public string? VersionFila { get; set; }
}

// =====================================================================
//  Portal del usuario final (CU-16 / RF21): catálogo con fotos, reserva
//  con documento y nombre, pago simulado y envío del comprobante.
// =====================================================================

public class TipoCatalogo
{
    public int IdTipo { get; set; }
    public string Tipo { get; set; } = "";
    public int Capacidad { get; set; }
    public string? Descripcion { get; set; }
    public string Imagen { get; set; } = "";
    public decimal? TarifaNoche { get; set; }
    public bool HayTarifa { get; set; }
    public int Noches { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal PrecioNeto { get; set; }
    public int Libres { get; set; }
    public bool Disponible { get; set; }
    public bool Cabe { get; set; }
    public int? IdHabitacionSugerida { get; set; }
    public string? HabitacionSugerida { get; set; }
}

public class Catalogo
{
    public DateOnly Entrada { get; set; }
    public DateOnly Salida { get; set; }
    public int Noches { get; set; }
    public int Personas { get; set; }
    public List<TipoCatalogo> Tipos { get; set; } = new();
}

public class HuespedPortal
{
    public int IdHuesped { get; set; }
    public string TipoDocumento { get; set; } = "CC";
    public string NumeroDocumento { get; set; } = "";
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public bool ExtranjeroNoResidente { get; set; }
    public int Estadias { get; set; }
    public bool EsFrecuente { get; set; }
}

public class DatosHuespedPortal
{
    public string TipoDocumento { get; set; } = "CC";
    public string NumeroDocumento { get; set; } = "";
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public bool ExtranjeroNoResidente { get; set; }
}

public class DatosTarjeta
{
    public string Numero { get; set; } = "";
    public string Titular { get; set; } = "";
    public string Vencimiento { get; set; } = "";
    public string Cvv { get; set; } = "";
}

public class SolicitudReservaPortal
{
    public DatosHuespedPortal Huesped { get; set; } = new();
    public int IdTipo { get; set; }
    public int? IdHabitacion { get; set; }
    public DateOnly FechaEntrada { get; set; }
    public DateOnly FechaSalida { get; set; }
    public int NumeroPersonas { get; set; } = 1;
    public string MedioPago { get; set; } = "TARJETA";
    public DatosTarjeta? Tarjeta { get; set; }
    public string CanalComprobante { get; set; } = "CORREO";
    public string? DestinoComprobante { get; set; }
}

public class ResultadoReservaPortal
{
    public bool Pagado { get; set; }
    /// <summary>true cuando la reserva quedó apartada en efectivo y falta pagar en recepción.</summary>
    public bool PendientePago { get; set; }
    public string Estado { get; set; } = "";
    /// <summary>Hora límite para pagar. Solo viene cuando la reserva quedó retenida.</summary>
    public DateTime? LimitePago { get; set; }
    public int? HorasPlazo { get; set; }
    /// <summary>Frase ya redactada por la API con el plazo exacto, para mostrarla tal cual.</summary>
    public string? AvisoPago { get; set; }
    public string Codigo { get; set; } = "";
    public string Comprobante { get; set; } = "";
    public string Habitacion { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string? Imagen { get; set; }
    public DateOnly Entrada { get; set; }
    public DateOnly Salida { get; set; }
    public int Noches { get; set; }
    public int Personas { get; set; }
    public string Huesped { get; set; } = "";
    public string Documento { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Impuesto { get; set; }
    public bool ExentoIva { get; set; }
    public decimal PrecioNeto { get; set; }
    public string MedioPago { get; set; } = "";
    public string? Autorizacion { get; set; }
    public string? Franquicia { get; set; }
    public string? UltimosCuatro { get; set; }
    public string? MensajePago { get; set; }
    public string Canal { get; set; } = "";
    public string Destino { get; set; } = "";
    public string Texto { get; set; } = "";
    public string? EnlaceWhatsApp { get; set; }
    public string? EnlaceCorreo { get; set; }
}

public class ReservaCliente
{
    public string Codigo { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Habitacion { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string? Imagen { get; set; }
    public DateOnly FechaEntrada { get; set; }
    public DateOnly FechaSalida { get; set; }
    public int NumeroPersonas { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Impuesto { get; set; }
    public bool ExentoIva { get; set; }
    public decimal PrecioNeto { get; set; }
    public string? Comprobante { get; set; }
    public string? Canal { get; set; }
    public string? Destino { get; set; }
    public string? MedioPago { get; set; }
    public string? Referencia { get; set; }
    /// <summary>Hora límite de pago cuando la reserva está retenida en efectivo (script 18).</summary>
    public DateTime? LimitePago { get; set; }
    /// <summary>La API decide si el huésped todavía puede cancelar, para que portal y API no discrepen.</summary>
    public bool PuedeCancelar { get; set; }
    public string? MotivoCancelacion { get; set; }
}

public class ComprobantePortal
{
    public string Comprobante { get; set; } = "";
    public string Codigo { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Habitacion { get; set; } = "";
    public string Tipo { get; set; } = "";
    public DateOnly Entrada { get; set; }
    public DateOnly Salida { get; set; }
    public decimal PrecioNeto { get; set; }
    public string? Canal { get; set; }
    public string? Destino { get; set; }
    public string Texto { get; set; } = "";
    public string? EnlaceWhatsApp { get; set; }
    public string? EnlaceCorreo { get; set; }
    public DateTime? LimitePago { get; set; }
    public bool PuedeCancelar { get; set; }
    public string? MotivoCancelacion { get; set; }
}

/// <summary>Respuesta de POST /api/portal/reservas/{codigo}/cancelar.</summary>
public class ResultadoCancelacion
{
    public string Codigo { get; set; } = "";
    public string Estado { get; set; } = "";
    public bool Devolucion { get; set; }
    public bool EstabaRetenida { get; set; }
    public string Mensaje { get; set; } = "";
}

public record SolicitudRegistro(string TipoDocumento, string NumeroDocumento, string Nombres, string Apellidos,
    string? Telefono, string Correo, string Clave, bool ExtranjeroNoResidente);

public record DatosTipo(string? Descripcion, string? Imagen, string? Comodidades = null);

// --- Ficha de la habitación (CU-17): galería, comodidades y precio del rango ---

public class FotoTipo
{
    public string Ruta { get; set; } = "";
    public string? Descripcion { get; set; }
}

public class TipoDetalle
{
    public int IdTipo { get; set; }
    public string Tipo { get; set; } = "";
    public string? Descripcion { get; set; }
    public int Capacidad { get; set; }
    public List<string> Comodidades { get; set; } = new();
    public List<FotoTipo> Fotos { get; set; } = new();
    public string? Imagen { get; set; }
    public DateOnly Entrada { get; set; }
    public DateOnly Salida { get; set; }
    public int Noches { get; set; }
    public int Personas { get; set; }
    public decimal? TarifaNoche { get; set; }
    public bool HayTarifa { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Iva { get; set; }
    public decimal PrecioNeto { get; set; }
    public int Libres { get; set; }
    public int TotalHabitaciones { get; set; }
    public bool Cabe { get; set; }
    public bool Disponible { get; set; }
    public int? IdHabitacionSugerida { get; set; }
    public string? HabitacionSugerida { get; set; }
    public List<int> Pisos { get; set; } = new();
}

public class FotoAdmin
{
    public int IdFoto { get; set; }
    public string Ruta { get; set; } = "";
    public string? Descripcion { get; set; }
    public int Orden { get; set; }
}

public record NuevaFoto(string Ruta, string? Descripcion);
