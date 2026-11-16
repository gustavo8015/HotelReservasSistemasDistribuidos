using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotelReservas.Api.Asistente;

/// <summary>
/// Intérprete de consultas contra cualquier servicio de modelo que exponga la interfaz
/// compatible con OpenAI, es decir un POST a /chat/completions que acepte el parámetro
/// <c>tools</c> para la llamada a funciones.
///
/// Se escribió así, y no contra un proveedor concreto, porque el hotel no debe quedar
/// amarrado a uno: cambiar de servicio es cambiar tres líneas de appsettings.json, sin
/// tocar el código ni el catálogo de funciones. Hoy cumplen esa interfaz, entre otros,
/// Google Gemini (https://generativelanguage.googleapis.com/v1beta/openai), los servicios
/// que replican la API de OpenAI y los modelos que se ejecutan en la propia máquina con
/// Ollama (http://localhost:11434/v1).
///
/// Configuración esperada:
///   "Asistente": {
///     "Proveedor": "GEMINI",
///     "BaseUrl":   "https://generativelanguage.googleapis.com/v1beta/openai",
///     "ApiKey":    "",
///     "Modelo":    "gemini-3.8-flash"
///   }
///
/// Si falta la clave o la dirección, el componente se declara no disponible y el
/// orquestador usa el intérprete de reglas, de modo que el asistente nunca queda mudo.
/// El modelo no se entrena con datos del hotel y la consulta sale ya filtrada de
/// documentos, teléfonos y correos por FiltroDatosPersonales.
/// </summary>
public class InterpreteCompatibleOpenAI : IInterpreteConsultas
{
    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;
    private readonly ILogger<InterpreteCompatibleOpenAI> _log;

    public InterpreteCompatibleOpenAI(HttpClient http, IConfiguration cfg, ILogger<InterpreteCompatibleOpenAI> log)
    { _http = http; _cfg = cfg; _log = log; }

    private string? BaseUrl => _cfg["Asistente:BaseUrl"];
    private string? ApiKey => _cfg["Asistente:ApiKey"];
    private string Modelo => _cfg["Asistente:Modelo"] ?? "gemini-3.8-flash";

    /// <summary>Nombre que se muestra en la pantalla y se guarda en CONSULTA_ASISTENTE.</summary>
    public string Nombre => (_cfg["Asistente:Proveedor"] ?? "MODELO_EXTERNO").Trim().ToUpperInvariant();

    public bool Disponible => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);

    private string Url => $"{BaseUrl!.TrimEnd('/')}/chat/completions";

    private static readonly JsonSerializerOptions Json =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private async Task<JsonDocument> LlamarAsync(object cuerpo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(cuerpo, options: Json) };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
        using var resp = await _http.SendAsync(req, ct);
        var texto = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("{Proveedor} respondió {Codigo}: {Cuerpo}", Nombre, (int)resp.StatusCode, texto);
            throw new HttpRequestException($"{Nombre} respondió {(int)resp.StatusCode}.");
        }
        return JsonDocument.Parse(texto);
    }

    private static string Contexto() => $"Fecha actual: {DateTime.Now:yyyy-MM-dd} ({DateTime.Now:dddd}).";

    public async Task<Interpretacion> InterpretarAsync(string consulta, IReadOnlyList<DescripcionFuncion> catalogo, CancellationToken ct)
    {
        var cuerpo = new
        {
            model = Modelo,
            messages = new object[]
            {
                new { role = "system", content = MensajesAsistente.Sistema + " " + Contexto() },
                new { role = "user", content = consulta },
            },
            tools = catalogo.Select(f => new { type = "function", function = new { name = f.Nombre, description = f.Descripcion, parameters = f.Parametros } }).ToArray(),
            tool_choice = "auto",
            temperature = 0,
        };
        using var doc = await LlamarAsync(cuerpo, ct);
        var msg = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        if (msg.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array && calls.GetArrayLength() > 0)
        {
            var fn = calls[0].GetProperty("function");
            var nombre = fn.GetProperty("name").GetString() ?? "";
            var args = fn.TryGetProperty("arguments", out var a) ? (a.GetString() ?? "{}") : "{}";
            JsonElement parametros;
            try { parametros = JsonDocument.Parse(args).RootElement.Clone(); }
            catch { parametros = JsonDocument.Parse("{}").RootElement.Clone(); }
            return new Interpretacion(nombre, parametros, null);
        }
        var contenido = msg.TryGetProperty("content", out var c) ? c.GetString() : null;
        return Interpretacion.Aclaracion(string.IsNullOrWhiteSpace(contenido) ? "¿Podría indicarme el tipo de habitación y las fechas?" : contenido!);
    }

    public async Task<string> RedactarAsync(string consulta, ResultadoFuncion resultado, CancellationToken ct)
    {
        // Segunda llamada: el modelo solo redacta a partir del resultado real de la función.
        var cuerpo = new
        {
            model = Modelo,
            messages = new object[]
            {
                new { role = "system", content = MensajesAsistente.Sistema + " " + Contexto() },
                new { role = "user", content = consulta },
                new { role = "assistant", content = (string?)null, tool_calls = new object[] { new { id = "call_1", type = "function", function = new { name = resultado.Funcion, arguments = "{}" } } } },
                new { role = "tool", tool_call_id = "call_1", content = resultado.ResumenParaModelo },
            },
            temperature = 0.2,
        };
        using var doc = await LlamarAsync(cuerpo, ct);
        var contenido = doc.RootElement.GetProperty("choices")[0].GetProperty("message").TryGetProperty("content", out var c) ? c.GetString() : null;
        return string.IsNullOrWhiteSpace(contenido) ? resultado.ResumenParaModelo : contenido!;
    }
}

/// <summary>
/// Instrucción de sistema compartida por los intérpretes que hablan con un modelo. Está aquí,
/// y no dentro de uno de ellos, porque define los límites del asistente y esos límites no
/// dependen del proveedor que se contrate.
/// </summary>
public static class MensajesAsistente
{
    public const string Sistema =
        "Eres el asistente de consultas de Hotel Reservas, un sistema de reservas de un hotel en Colombia. " +
        "Respondes en español, de forma breve y cordial. " +
        "Solo puedes responder con base en el resultado de las funciones disponibles; nunca inventes habitaciones, tarifas, reservas ni fechas. " +
        "Si faltan datos necesarios (tipo de habitación o fechas), pide la aclaración en una sola pregunta en lugar de llamar una función. " +
        "Interpreta fechas relativas (hoy, mañana, este fin de semana) con la fecha actual indicada. Los tipos de habitación son Sencilla, Doble, Triple y Cuádruple. " +
        "Los precios se informan como precio neto con IVA del 19 % incluido; el huésped extranjero no residente está exento de IVA. " +
        "No puedes crear, modificar ni cancelar reservas, ni registrar pagos o check-in: si te lo piden, indica dónde se hace esa operación en la aplicación. " +
        "Nunca solicites ni repitas documentos de identidad, teléfonos o correos de huéspedes.";
}
