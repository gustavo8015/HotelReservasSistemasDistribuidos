using System.Net;

namespace HotelReservas.Web.Services;

/// <summary>
/// Parámetros de la política de resiliencia del cliente. Se dejan en un solo sitio para que el
/// documento técnico pueda citarlos y para poder ajustarlos sin tocar la lógica.
/// </summary>
public sealed class PoliticaResiliencia
{
    /// <summary>Tiempo máximo que se espera una respuesta antes de darla por perdida.</summary>
    public TimeSpan TiempoDeEspera { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Número de intentos adicionales tras el primero. Solo para operaciones idempotentes.</summary>
    public int Reintentos { get; init; } = 2;

    /// <summary>Espera base de la retirada exponencial: 400 ms, 800 ms, 1600 ms.</summary>
    public TimeSpan EsperaBase { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Fallos consecutivos que abren el interruptor de circuito.</summary>
    public int UmbralDeApertura { get; init; } = 4;

    /// <summary>Tiempo que el interruptor permanece abierto antes de dejar pasar una prueba.</summary>
    public TimeSpan DuracionAbierto { get; init; } = TimeSpan.FromSeconds(15);
}

/// <summary>Estados del interruptor de circuito.</summary>
public enum EstadoCircuito { Cerrado, Abierto, SemiAbierto }

/// <summary>
/// Interruptor de circuito del cliente web.
///
/// Sin él, cuando la API deja de responder cada pantalla sigue intentando y el usuario espera el
/// tiempo de espera completo una y otra vez. Al acumularse fallos consecutivos el interruptor se
/// abre y las peticiones siguientes fallan de inmediato con un mensaje claro, lo que evita
/// castigar al usuario y deja de presionar a un servicio que ya está caído. Pasado el periodo de
/// reposo deja pasar una sola petición de prueba: si responde, el circuito se cierra; si falla,
/// vuelve a abrirse.
///
/// Es un componente compartido por toda la aplicación, de modo que se registra como singleton.
/// </summary>
public sealed class InterruptorCircuito
{
    private readonly PoliticaResiliencia _politica;
    private int _fallosConsecutivos;
    private DateTime _abiertoHasta = DateTime.MinValue;
    private bool _pruebaEnCurso;

    public InterruptorCircuito(PoliticaResiliencia politica) => _politica = politica;

    /// <summary>Se dispara en cada cambio de estado para que la interfaz pueda avisar al usuario.</summary>
    public event Action? Cambio;

    public int FallosConsecutivos => _fallosConsecutivos;
    public int TotalAperturas { get; private set; }
    public DateTime AbiertoHasta => _abiertoHasta;

    public EstadoCircuito Estado
    {
        get
        {
            if (DateTime.UtcNow < _abiertoHasta) return EstadoCircuito.Abierto;
            if (_abiertoHasta != DateTime.MinValue && _fallosConsecutivos >= _politica.UmbralDeApertura)
                return EstadoCircuito.SemiAbierto;
            return EstadoCircuito.Cerrado;
        }
    }

    public int SegundosRestantes => Estado == EstadoCircuito.Abierto
        ? Math.Max(1, (int)Math.Ceiling((_abiertoHasta - DateTime.UtcNow).TotalSeconds))
        : 0;

    /// <summary>true cuando hay que dejar pasar la petición; false cuando el circuito está abierto.</summary>
    public bool PermitePasar()
    {
        if (Estado != EstadoCircuito.Abierto) { _pruebaEnCurso = Estado == EstadoCircuito.SemiAbierto; return true; }
        return false;
    }

    public void RegistrarExito()
    {
        var habiaFallos = _fallosConsecutivos > 0;
        _fallosConsecutivos = 0;
        _abiertoHasta = DateTime.MinValue;
        _pruebaEnCurso = false;
        if (habiaFallos) Cambio?.Invoke();
    }

    public void RegistrarFallo()
    {
        _fallosConsecutivos++;
        if (_pruebaEnCurso || _fallosConsecutivos >= _politica.UmbralDeApertura)
        {
            var yaEstaba = DateTime.UtcNow < _abiertoHasta;
            _abiertoHasta = DateTime.UtcNow + _politica.DuracionAbierto;
            _pruebaEnCurso = false;
            if (!yaEstaba) TotalAperturas++;
        }
        Cambio?.Invoke();
    }
}

/// <summary>
/// Error que devuelve el manejador cuando el interruptor está abierto. Se traduce a un 503 para
/// que el resto del cliente lo trate como cualquier otra indisponibilidad del servicio.
/// </summary>
public sealed class CircuitoAbiertoException : Exception
{
    public int SegundosRestantes { get; }
    public CircuitoAbiertoException(int segundos)
        : base($"El servicio no está respondiendo. Se reintentará en {segundos} segundos.")
        => SegundosRestantes = segundos;
}

/// <summary>
/// Manejador que envuelve cada petición HTTP del cliente con tiempo de espera, reintentos con
/// retirada exponencial e interruptor de circuito.
///
/// Decisión importante: los reintentos se aplican únicamente a operaciones idempotentes, es decir
/// GET, HEAD, OPTIONS, PUT y DELETE. Un POST que crea una reserva no se reintenta nunca de forma
/// automática, porque si la primera petición llegó al servidor y se perdió solo la respuesta, el
/// reintento crearía una segunda reserva. En ese caso el reintento lo decide la persona, que ve
/// el mensaje y puede consultar si su reserva quedó registrada. Esta es la razón por la que la
/// confirmación de reserva se resuelve además dentro de una transacción serializable en la base
/// de datos: dos intentos sobre la misma habitación y las mismas fechas chocan con el error
/// 50001 en lugar de duplicar la reserva.
/// </summary>
public sealed class ManejadorResiliencia : DelegatingHandler
{
    private readonly PoliticaResiliencia _politica;
    private readonly InterruptorCircuito _circuito;

    /// <summary>Contadores que la pantalla de diagnóstico y las pruebas pueden leer.</summary>
    public static int TotalPeticiones;
    public static int TotalReintentos;
    public static int TotalRechazadasPorCircuito;

    public ManejadorResiliencia(PoliticaResiliencia politica, InterruptorCircuito circuito)
        : base(new HttpClientHandler())
    { _politica = politica; _circuito = circuito; }

    private bool EsIdempotente(HttpMethod m)
        => m == HttpMethod.Get || m == HttpMethod.Head || m == HttpMethod.Options
        || m == HttpMethod.Put || m == HttpMethod.Delete;

    /// <summary>Un 5xx, un 408 o un 429 son fallos transitorios; un 4xx normal no lo es.</summary>
    private static bool EsTransitorio(HttpStatusCode c)
        => (int)c >= 500 || c == HttpStatusCode.RequestTimeout || c == (HttpStatusCode)429;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        if (!_circuito.PermitePasar())
        {
            TotalRechazadasPorCircuito++;
            throw new CircuitoAbiertoException(_circuito.SegundosRestantes);
        }

        TotalPeticiones++;
        var intentosMaximos = EsIdempotente(solicitud.Method) ? _politica.Reintentos + 1 : 1;
        Exception? ultimoError = null;

        // El cuerpo se guarda en memoria una sola vez: un HttpRequestMessage no se puede enviar
        // dos veces, de modo que cada intento necesita su propia copia.
        byte[]? cuerpo = solicitud.Content == null ? null : await solicitud.Content.ReadAsByteArrayAsync(ct);

        for (var intento = 1; intento <= intentosMaximos; intento++)
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(_politica.TiempoDeEspera);

            try
            {
                using var copia = Clonar(solicitud, cuerpo);
                var respuesta = await base.SendAsync(copia, limite.Token);

                if (EsTransitorio(respuesta.StatusCode) && intento < intentosMaximos)
                {
                    respuesta.Dispose();
                    TotalReintentos++;
                    await EsperarAsync(intento, ct);
                    continue;
                }

                // Un 4xx es una respuesta del servicio, no una caída: el circuito no se abre por
                // una contraseña equivocada ni por una reserva que choca con otra.
                if (EsTransitorio(respuesta.StatusCode)) _circuito.RegistrarFallo();
                else _circuito.RegistrarExito();
                return respuesta;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                ultimoError = new TimeoutException($"La API no respondió en {_politica.TiempoDeEspera.TotalSeconds:0} segundos.");
            }
            catch (HttpRequestException ex)
            {
                ultimoError = ex;
            }

            if (intento < intentosMaximos) { TotalReintentos++; await EsperarAsync(intento, ct); }
        }

        _circuito.RegistrarFallo();
        throw ultimoError ?? new HttpRequestException("No fue posible contactar la API.");
    }

    /// <summary>
    /// Retirada exponencial con una fracción aleatoria. Lo aleatorio evita que varios clientes
    /// que fallaron a la vez vuelvan a intentar en el mismo instante y tumben el servicio justo
    /// cuando se está levantando.
    /// </summary>
    private Task EsperarAsync(int intento, CancellationToken ct)
    {
        var baseMs = _politica.EsperaBase.TotalMilliseconds * Math.Pow(2, intento - 1);
        var conRuido = baseMs * (0.75 + Random.Shared.NextDouble() * 0.5);
        return Task.Delay(TimeSpan.FromMilliseconds(conRuido), ct);
    }

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
