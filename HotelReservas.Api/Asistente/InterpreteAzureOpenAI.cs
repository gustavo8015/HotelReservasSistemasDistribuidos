using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotelReservas.Api.Asistente;

/// <summary>
/// Modelo de IA (sección 13.3.2): GPT-4o consumido como servicio a través de Azure OpenAI Service,
/// en modo de llamada a funciones. Se usa la API REST de chat completions directamente, sin SDK,
/// para que el componente sea pequeño y fácil de auditar. El modelo no se entrena con datos del hotel.
/// </summary>
public class InterpreteAzureOpenAI : IInterpreteConsultas
{
    public const string MensajeSistema =
        "Eres el asistente de consultas de Hotel Reservas, un sistema transaccional de reservas de un hotel en Colombia. " +
        "Respondes en español, de forma breve y cordial, al personal de recepción. " +
        "Solo puedes responder con base en el resultado de las funciones disponibles; nunca inventes habitaciones, tarifas, reservas ni fechas. " +
        "Si faltan datos necesarios (tipo de habitación o fechas), pide la aclaración en una sola pregunta en lugar de llamar una función. " +
        "Interpreta fechas relativas (hoy, mañana, este fin de semana) con la fecha actual indicada. Los tipos de habitación son Sencilla, Doble, Triple y Cuádruple. " +
        "Los precios se informan como precio neto con IVA del 19 % incluido; el huésped extranjero no residente está exento de IVA. " +
        "No puedes crear, modificar ni cancelar reservas, ni registrar pagos o check-in: si te lo piden, indica que esas operaciones se realizan desde la pantalla Reservas de la aplicación, donde se verifica la disponibilidad con bloqueo y se emite el comprobante. " +
        "Nunca solicites ni repitas documentos de identidad, teléfonos o correos de huéspedes.";

    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;
    private readonly ILogger<InterpreteAzureOpenAI> _log;

    public InterpreteAzureOpenAI(HttpClient http, IConfiguration cfg, ILogger<InterpreteAzureOpenAI> log)
    { _http = http; _cfg = cfg; _log = log; }

    public string Nombre => "AZURE_OPENAI";
    public bool Disponible => !string.IsNullOrWhiteSpace(_cfg["AzureOpenAI:Endpoint"]) && !string.IsNullOrWhiteSpace(_cfg["AzureOpenAI:ApiKey"]);

    private string Url => $"{_cfg["AzureOpenAI:Endpoint"]!.TrimEnd('/')}/openai/deployments/{_cfg["AzureOpenAI:Deployment"]}/chat/completions?api-version={_cfg["AzureOpenAI:ApiVersion"] ?? "2024-10-21"}";

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private async Task<JsonDocument> LlamarAsync(object cuerpo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(cuerpo, options: Json) };
        req.Headers.Add("api-key", _cfg["AzureOpenAI:ApiKey"]);
        using var resp = await _http.SendAsync(req, ct);
        var texto = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("Azure OpenAI respondió {Codigo}: {Cuerpo}", (int)resp.StatusCode, texto);
            throw new HttpRequestException($"Azure OpenAI respondió {(int)resp.StatusCode}.");
        }
        return JsonDocument.Parse(texto);
    }

    private static string Contexto() => $"Fecha actual: {DateTime.Now:yyyy-MM-dd} ({DateTime.Now:dddd}).";

    public async Task<Interpretacion> InterpretarAsync(string consulta, IReadOnlyList<DescripcionFuncion> catalogo, CancellationToken ct)
    {
        var cuerpo = new
        {
            messages = new object[]
            {
                new { role = "system", content = MensajeSistema + " " + Contexto() },
                new { role = "user", content = consulta },
            },
            tools = catalogo.Select(f => new { type = "function", function = new { name = f.Nombre, description = f.Descripcion, parameters = f.Parametros } }).ToArray(),
            tool_choice = "auto",
            temperature = 0,
            max_tokens = 300,
        };
        using var doc = await LlamarAsync(cuerpo, ct);
        var msg = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        if (msg.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array && calls.GetArrayLength() > 0)
        {
            var fn = calls[0].GetProperty("function");
            var nombre = fn.GetProperty("name").GetString() ?? "";
            var args = fn.GetProperty("arguments").GetString() ?? "{}";
            JsonElement parametros;
            try { parametros = JsonDocument.Parse(args).RootElement.Clone(); } catch { parametros = JsonDocument.Parse("{}").RootElement.Clone(); }
            return new Interpretacion(nombre, parametros, null);
        }
        // Sin llamada a función: el modelo pidió una aclaración o rechazó una operación de escritura.
        var contenido = msg.TryGetProperty("content", out var c) ? c.GetString() : null;
        return Interpretacion.Aclaracion(string.IsNullOrWhiteSpace(contenido) ? "¿Podría indicarme el tipo de habitación y las fechas?" : contenido!);
    }

    public async Task<string> RedactarAsync(string consulta, ResultadoFuncion resultado, CancellationToken ct)
    {
        // Segunda llamada: el modelo redacta únicamente a partir del resultado real devuelto por la API.
        var cuerpo = new
        {
            messages = new object[]
            {
                new { role = "system", content = MensajeSistema + " " + Contexto() },
                new { role = "user", content = consulta },
                new { role = "assistant", content = (string?)null, tool_calls = new object[] { new { id = "call_1", type = "function", function = new { name = resultado.Funcion, arguments = "{}" } } } },
                new { role = "tool", tool_call_id = "call_1", content = resultado.ResumenParaModelo },
            },
            temperature = 0.2,
            max_tokens = 300,
        };
        using var doc = await LlamarAsync(cuerpo, ct);
        var contenido = doc.RootElement.GetProperty("choices")[0].GetProperty("message").TryGetProperty("content", out var c) ? c.GetString() : null;
        return string.IsNullOrWhiteSpace(contenido) ? resultado.ResumenParaModelo : contenido!;
    }
}
