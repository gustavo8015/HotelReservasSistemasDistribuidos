using System.Diagnostics;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using HotelReservas.Api.Data;

namespace HotelReservas.Api.Servicios;

/// <summary>
/// Comprobación de disponibilidad de la base de datos para el recurso /health/ready.
///
/// Distingue dos preguntas que suelen confundirse. "¿Está vivo el proceso?" la responde /health
/// sin tocar nada externo, y sirve para que un orquestador decida si hay que reiniciar el
/// contenedor. "¿Está listo para atender?" la responde este control, que abre una conexión real
/// contra SQL Server con un tiempo de espera corto, y sirve para que el balanceador deje de
/// enviarle tráfico mientras la base no esté arriba.
/// </summary>
public sealed class ComprobacionBaseDeDatos : IHealthCheck
{
    private readonly HotelReservasContext _db;
    public ComprobacionBaseDeDatos(HotelReservasContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext contexto, CancellationToken ct = default)
    {
        var reloj = Stopwatch.StartNew();
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(3));
            await _db.Database.ExecuteSqlRawAsync("SELECT 1", limite.Token);
            return HealthCheckResult.Healthy("La base de datos responde.",
                new Dictionary<string, object> { ["milisegundos"] = reloj.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("La base de datos no responde.", ex,
                new Dictionary<string, object> { ["milisegundos"] = reloj.ElapsedMilliseconds });
        }
    }
}

/// <summary>
/// Reintentos con retirada exponencial para las llamadas que la API hace a servicios externos,
/// hoy el proveedor del modelo del asistente.
///
/// Se aplica solo a fallos transitorios, es decir errores de red, tiempos de espera agotados y
/// respuestas 5xx, 408 o 429. Un 401 por clave equivocada no se reintenta, porque reintentarlo
/// no cambia el resultado y solo retrasa la respuesta al huésped. Cuando los reintentos se
/// agotan, el orquestador cae al intérprete de reglas, de modo que la pantalla responde igual.
/// </summary>
public sealed class ManejadorReintentos : DelegatingHandler
{
    private readonly ILogger<ManejadorReintentos> _log;
    public ManejadorReintentos(ILogger<ManejadorReintentos> log) => _log = log;

    public const int Intentos = 3;
    private static readonly TimeSpan EsperaBase = TimeSpan.FromMilliseconds(300);

    public static int TotalReintentos;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        byte[]? cuerpo = solicitud.Content == null ? null : await solicitud.Content.ReadAsByteArrayAsync(ct);

        for (var intento = 1; ; intento++)
        {
            try
            {
                using var copia = Clonar(solicitud, cuerpo);
                var respuesta = await base.SendAsync(copia, ct);
                if (intento >= Intentos || !EsTransitorio(respuesta.StatusCode)) return respuesta;
                _log.LogWarning("El proveedor del modelo respondió {Codigo}; reintento {Intento} de {Total}.",
                                (int)respuesta.StatusCode, intento, Intentos);
                respuesta.Dispose();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && intento < Intentos && !ct.IsCancellationRequested)
            {
                _log.LogWarning("Fallo de red con el proveedor del modelo ({Mensaje}); reintento {Intento} de {Total}.",
                                ex.Message, intento, Intentos);
            }

            TotalReintentos++;
            var espera = EsperaBase.TotalMilliseconds * Math.Pow(2, intento - 1) * (0.75 + Random.Shared.NextDouble() * 0.5);
            await Task.Delay(TimeSpan.FromMilliseconds(espera), ct);
        }
    }

    private static bool EsTransitorio(HttpStatusCode c)
        => (int)c >= 500 || c == HttpStatusCode.RequestTimeout || c == (HttpStatusCode)429;

    private static HttpRequestMessage Clonar(HttpRequestMessage original, byte[]? cuerpo)
    {
        var copia = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var c in original.Headers) copia.Headers.TryAddWithoutValidation(c.Key, c.Value);
        if (cuerpo != null)
        {
            copia.Content = new ByteArrayContent(cuerpo);
            if (original.Content != null)
                foreach (var c in original.Content.Headers) copia.Content.Headers.TryAddWithoutValidation(c.Key, c.Value);
        }
        return copia;
    }
}
