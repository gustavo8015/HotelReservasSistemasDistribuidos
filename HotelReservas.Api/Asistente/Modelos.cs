using System.Text.Json;

namespace HotelReservas.Api.Asistente;

/// <summary>Solicitud de la pantalla Asistente de la aplicación móvil.</summary>
public record SolicitudAsistente(string Consulta);

/// <summary>Respuesta al usuario: texto redactado + datos estructurados + función consultada (origen de la información).</summary>
public record RespuestaAsistente(string Respuesta, string? Funcion, JsonElement? Datos, string Interprete, bool Exitosa, int DuracionMs);

/// <summary>Decisión del intérprete (modelo de IA o reglas): qué función del catálogo ejecutar y con qué parámetros.</summary>
public record Interpretacion(string? Funcion, JsonElement? Parametros, string? MensajeDirecto)
{
    public static Interpretacion Aclaracion(string mensaje) => new(null, null, mensaje);
}

/// <summary>Resultado de ejecutar una función de lectura del catálogo.</summary>
public record ResultadoFuncion(string Funcion, object Datos, string ResumenParaModelo);

/// <summary>Descripción de una función del catálogo (nombre, propósito y esquema JSON de parámetros) tal como se envía al modelo.</summary>
public record DescripcionFuncion(string Nombre, string Descripcion, object Parametros);

public interface IInterpreteConsultas
{
    string Nombre { get; }
    bool Disponible { get; }
    /// <summary>Paso 1: traducir la pregunta en una llamada a función del catálogo (o en una aclaración).</summary>
    Task<Interpretacion> InterpretarAsync(string consulta, IReadOnlyList<DescripcionFuncion> catalogo, CancellationToken ct);
    /// <summary>Paso 2: redactar la respuesta final en español a partir del resultado real de la función.</summary>
    Task<string> RedactarAsync(string consulta, ResultadoFuncion resultado, CancellationToken ct);
}
