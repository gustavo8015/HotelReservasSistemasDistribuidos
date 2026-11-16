using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace HotelReservas.Pruebas;

/// <summary>
/// Banco de pruebas del sistema Hotel Reservas.
///
/// Ejecuta cinco suites contra una instancia en marcha de la API y deja los resultados en
/// evidencias.md y evidencias.json, que son los archivos que se citan en el documento técnico.
///
///     dotnet run --project pruebas -- --api http://localhost:5080
///
/// Opciones: --api, --concurrencia (reservas simultáneas, 10 por omisión),
/// --rendimiento (peticiones de la prueba de carga, 200 por omisión), --salida (carpeta).
/// </summary>
public static class Programa
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo Es = new("es-CO");
    private static readonly List<Resultado> Resultados = new();
    private static string _api = "http://localhost:5080";

    /// <summary>Destino del comprobante en las pruebas. El canal solo admite CORREO o WHATSAPP.</summary>
    private const string CorreoDePrueba = "pruebas.automaticas@hotelreservas.co";
    private static string? _tokenAdmin;

    public record Resultado(string Id, string Suite, string Caso, string Esperado, string Obtenido, bool Paso, long Ms);

    public static async Task<int> Main(string[] args)
    {
        var concurrencia = 10;
        var rendimiento = 200;
        var salida = Directory.GetCurrentDirectory();

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--api") _api = args[i + 1].TrimEnd('/');
            if (args[i] == "--concurrencia") concurrencia = int.Parse(args[i + 1]);
            if (args[i] == "--rendimiento") rendimiento = int.Parse(args[i + 1]);
            if (args[i] == "--salida") salida = args[i + 1];
        }

        Titulo("Banco de pruebas - Hotel Reservas");
        Console.WriteLine($"API:          {_api}");
        Console.WriteLine($"Concurrencia: {concurrencia} reservas simultáneas sobre la misma habitación");
        Console.WriteLine($"Rendimiento:  {rendimiento} peticiones");
        Console.WriteLine($"Fecha:        {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        if (!await ApiRespondeAsync())
        {
            Console.WriteLine();
            Console.WriteLine("La API no responde. Inicie HotelReservas.Api y vuelva a ejecutar.");
            return 2;
        }

        await SuiteFuncionalesAsync();
        var reserva = await SuiteIntegracionAsync();
        var concurrenciaInforme = await SuiteConcurrenciaAsync(concurrencia);
        var rendimientoInforme = await SuiteRendimientoAsync(rendimiento);
        await SuiteToleranciaAsync();

        Resumen();
        await EscribirEvidenciasAsync(salida, concurrenciaInforme, rendimientoInforme, reserva);

        var fallidas = Resultados.Count(r => !r.Paso);
        Console.WriteLine();
        Console.WriteLine(fallidas == 0
            ? "Todas las pruebas pasaron."
            : $"{fallidas} prueba(s) no pasaron. Revise el detalle de arriba.");
        return fallidas == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- utilería

    private static HttpClient Cliente(string? token = null, TimeSpan? espera = null)
    {
        var c = new HttpClient { BaseAddress = new Uri(_api + "/"), Timeout = espera ?? TimeSpan.FromSeconds(30) };
        if (token != null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static void Titulo(string t)
    {
        Console.WriteLine();
        Console.WriteLine(t);
        Console.WriteLine(new string('-', t.Length));
    }

    private static void Registrar(string id, string suite, string caso, string esperado, string obtenido, bool paso, long ms)
    {
        Resultados.Add(new Resultado(id, suite, caso, esperado, obtenido, paso, ms));
        Console.WriteLine($"  {(paso ? "PASA  " : "FALLA ")} {id}  {caso}");
        if (!paso) Console.WriteLine($"          esperado: {esperado} | obtenido: {obtenido}");
    }

    private static async Task<bool> ApiRespondeAsync()
    {
        using var c = Cliente(espera: TimeSpan.FromSeconds(10));
        try { var r = await c.GetAsync("health"); return r.IsSuccessStatusCode; }
        catch { try { var r = await c.GetAsync("api/salud"); return r.IsSuccessStatusCode; } catch { return false; } }
    }

    private static async Task<(HttpStatusCode Codigo, string Cuerpo, long Ms)> PedirAsync(
        HttpClient c, HttpMethod m, string ruta, object? cuerpo = null)
    {
        var reloj = Stopwatch.StartNew();
        using var req = new HttpRequestMessage(m, ruta);
        if (cuerpo != null)
        {
            // StringContent en lugar de JsonContent: así la petición lleva Content-Length y no
            // se envía troceada, que es lo que esperan las pasarelas intermedias.
            var texto = JsonSerializer.Serialize(cuerpo, Json);
            req.Content = new StringContent(texto, System.Text.Encoding.UTF8, "application/json");
        }
        try
        {
            using var resp = await c.SendAsync(req);
            var texto = await resp.Content.ReadAsStringAsync();
            return (resp.StatusCode, texto, reloj.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            return (HttpStatusCode.ServiceUnavailable, ex.GetType().Name + ": " + ex.Message, reloj.ElapsedMilliseconds);
        }
    }

    // ------------------------------------------------------- 1. funcionales

    private static async Task SuiteFuncionalesAsync()
    {
        Titulo("Suite 1. Pruebas funcionales");
        using var anonimo = Cliente();

        var r = await PedirAsync(anonimo, HttpMethod.Post, "api/auth/login",
                                 new { correo = "admin@hotel.com", clave = "Hotel2026*" });
        var entro = r.Codigo == HttpStatusCode.OK && r.Cuerpo.Contains("token", StringComparison.OrdinalIgnoreCase);
        if (entro)
        {
            using var doc = JsonDocument.Parse(r.Cuerpo);
            _tokenAdmin = doc.RootElement.GetProperty("token").GetString();
        }
        Registrar("F-01", "Funcionales", "Inicio de sesión con credenciales válidas",
                  "200 con token JWT", $"{(int)r.Codigo}", entro, r.Ms);

        r = await PedirAsync(anonimo, HttpMethod.Post, "api/auth/login",
                             new { correo = "admin@hotel.com", clave = "clave-equivocada" });
        Registrar("F-02", "Funcionales", "Inicio de sesión con contraseña equivocada",
                  "401", $"{(int)r.Codigo}", r.Codigo == HttpStatusCode.Unauthorized, r.Ms);

        r = await PedirAsync(anonimo, HttpMethod.Get, "api/usuarios");
        Registrar("F-03", "Funcionales", "Recurso protegido sin token",
                  "401", $"{(int)r.Codigo}", r.Codigo == HttpStatusCode.Unauthorized, r.Ms);

        var entrada = DateTime.Today.AddDays(30);
        var salida = entrada.AddDays(2);
        r = await PedirAsync(anonimo, HttpMethod.Get,
            $"api/portal/catalogo?entrada={entrada:yyyy-MM-dd}&salida={salida:yyyy-MM-dd}&personas=2");
        Registrar("F-04", "Funcionales", "Catálogo público con fechas y número de personas",
                  "200 con tipos de habitación", $"{(int)r.Codigo}", r.Codigo == HttpStatusCode.OK, r.Ms);

        r = await PedirAsync(anonimo, HttpMethod.Get,
            $"api/portal/catalogo?entrada={salida:yyyy-MM-dd}&salida={entrada:yyyy-MM-dd}&personas=2");
        Registrar("F-05", "Funcionales", "Catálogo con la salida antes de la entrada",
                  "400 código 50000", $"{(int)r.Codigo}", r.Codigo == HttpStatusCode.BadRequest, r.Ms);

        // El recurso exige el documento como parámetro: sin él la validación del modelo
        // responde 400 antes de buscar, que no es lo que este caso quiere comprobar.
        r = await PedirAsync(anonimo, HttpMethod.Get, "api/portal/comprobantes/CMP999999999?documento=0000000000");
        Registrar("F-06", "Funcionales", "Comprobante inexistente",
                  "404", $"{(int)r.Codigo}", r.Codigo == HttpStatusCode.NotFound, r.Ms);

        r = await PedirAsync(anonimo, HttpMethod.Post, "api/asistente",
                             new { consulta = "¿Cuáles son las condiciones de pago y cancelación?" });
        var respondio = r.Codigo == HttpStatusCode.OK && r.Cuerpo.Contains("respuesta", StringComparison.OrdinalIgnoreCase);
        Registrar("F-07", "Funcionales", "Asistente responde a un visitante sin cuenta",
                  "200 con respuesta", $"{(int)r.Codigo}", respondio, r.Ms);

        if (_tokenAdmin != null)
        {
            using var admin = Cliente(_tokenAdmin);
            r = await PedirAsync(admin, HttpMethod.Get, "api/usuarios");
            Registrar("F-08", "Funcionales", "Recurso de administración con token de ADMINISTRADOR",
                      "200", $"{(int)r.Codigo}", r.Codigo == HttpStatusCode.OK, r.Ms);
        }
    }

    // ------------------------------------------------------- 2. integración

    private static async Task<string?> SuiteIntegracionAsync()
    {
        Titulo("Suite 2. Pruebas de integración");
        using var anonimo = Cliente();

        var entrada = DateTime.Today.AddDays(45);
        var salida = entrada.AddDays(2);
        var documento = "PRB" + DateTime.Now.ToString("yyMMddHHmmss");

        var elegido = await PrimerTipoDisponibleAsync(anonimo, entrada, salida);
        if (elegido == null)
        {
            Registrar("I-01", "Integración", "Reservar desde el portal",
                      "una reserva creada", "no hay tipos disponibles en esas fechas", false, 0);
            return null;
        }

        var r = await PedirAsync(anonimo, HttpMethod.Post, "api/portal/reservas", new
        {
            huesped = new
            {
                tipoDocumento = "CC", numeroDocumento = documento,
                nombres = "Prueba", apellidos = "Automatica",
                telefono = "3000000000", correo = CorreoDePrueba, extranjeroNoResidente = false,
            },
            idTipo = elegido.Value.IdTipo,
            idHabitacion = (int?)null,
            fechaEntrada = entrada.ToString("yyyy-MM-dd"),
            fechaSalida = salida.ToString("yyyy-MM-dd"),
            numeroPersonas = 1,
            medioPago = "EFECTIVO",
            tarjeta = (object?)null,
            canalComprobante = "CORREO",
            destinoComprobante = CorreoDePrueba,
        });

        string? codigo = null, comprobante = null;
        if (r.Codigo == HttpStatusCode.OK)
        {
            using var doc = JsonDocument.Parse(r.Cuerpo);
            if (doc.RootElement.TryGetProperty("codigo", out var c)) codigo = c.GetString();
            if (doc.RootElement.TryGetProperty("comprobante", out var n)) comprobante = n.GetString();
        }
        Registrar("I-01", "Integración", "Reservar en efectivo desde el portal, sin cuenta",
                  "200 con código de reserva",
                  $"{(int)r.Codigo} {(codigo ?? Recortar(r.Cuerpo, 90))}",
                  r.Codigo == HttpStatusCode.OK && codigo != null, r.Ms);

        if (comprobante != null)
        {
            r = await PedirAsync(anonimo, HttpMethod.Get, $"api/portal/comprobantes/{comprobante}?documento={documento}");
            Registrar("I-02", "Integración", "Consultar el comprobante con el documento del huésped",
                      "200", $"{(int)r.Codigo}", r.Codigo == HttpStatusCode.OK, r.Ms);

            r = await PedirAsync(anonimo, HttpMethod.Get, $"api/portal/comprobantes/{comprobante}?documento=0000000000");
            Registrar("I-03", "Integración", "Consultar ese comprobante con otro documento",
                      "404 o 403", $"{(int)r.Codigo}",
                      r.Codigo is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, r.Ms);
        }

        if (codigo != null)
        {
            r = await PedirAsync(anonimo, HttpMethod.Post, $"api/portal/reservas/{codigo}/cancelar",
                                 new { documento, motivo = "Prueba automática" });
            Registrar("I-04", "Integración", "Cancelación hecha por el propio huésped",
                      "200", $"{(int)r.Codigo}", r.Codigo == HttpStatusCode.OK, r.Ms);
        }

        return codigo;
    }

    /// <summary>
    /// Primer tipo con habitaciones libres en ese rango, tomado del catálogo público. Devuelve
    /// también la habitación que la API sugiere, que es la que usa la prueba de concurrencia
    /// para que todas las peticiones compitan por el mismo recurso.
    /// </summary>
    private static async Task<(int IdTipo, int? IdHabitacion)?> PrimerTipoDisponibleAsync(
        HttpClient c, DateTime entrada, DateTime salida, int personas = 1)
    {
        var r = await PedirAsync(c, HttpMethod.Get,
            $"api/portal/catalogo?entrada={entrada:yyyy-MM-dd}&salida={salida:yyyy-MM-dd}&personas={personas}");
        if (r.Codigo != HttpStatusCode.OK) return null;
        try
        {
            using var doc = JsonDocument.Parse(r.Cuerpo);
            if (!doc.RootElement.TryGetProperty("tipos", out var tipos) || tipos.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var tipo in tipos.EnumerateArray())
            {
                if (!tipo.TryGetProperty("disponible", out var d) || !d.GetBoolean()) continue;
                var idTipo = tipo.GetProperty("idTipo").GetInt32();
                int? idHab = tipo.TryGetProperty("idHabitacionSugerida", out var h) && h.ValueKind == JsonValueKind.Number
                    ? h.GetInt32() : null;
                return (idTipo, idHab);
            }
        }
        catch { }
        return null;
    }

    // ------------------------------------------------------ 3. concurrencia

    public record InformeConcurrencia(int Intentos, int Exitosas, int Conflictos, int Otros, long MsTotal, List<string> Codigos);

    private static async Task<InformeConcurrencia?> SuiteConcurrenciaAsync(int n)
    {
        Titulo("Suite 3. Prueba de concurrencia");

        var entrada = DateTime.Today.AddDays(60);
        var salida = entrada.AddDays(1);

        // Se elige una habitación concreta para que las N peticiones compitan por el mismo
        // recurso: ese es el punto de la prueba.
        int idTipo; int? idHabitacion;
        using (var buscador = Cliente())
        {
            var elegido = await PrimerTipoDisponibleAsync(buscador, entrada, salida);
            if (elegido == null || elegido.Value.IdHabitacion == null)
            {
                Registrar("C-01", "Concurrencia", "Reservas simultáneas sobre la misma habitación",
                          "una sola confirmada", "no se encontró una habitación libre en esas fechas", false, 0);
                return null;
            }
            idTipo = elegido.Value.IdTipo;
            idHabitacion = elegido.Value.IdHabitacion;
        }

        var documentoBase = "CNC" + DateTime.Now.ToString("yyMMddHHmmss");
        var codigos = new string[n];
        var reloj = Stopwatch.StartNew();

        // Todas las tareas se sueltan a la vez con una barrera, para que la competencia sea real
        // y no una fila ordenada por el momento en que se creó cada tarea.
        using var salidaSimultanea = new SemaphoreSlim(0, n);
        var tareas = Enumerable.Range(0, n).Select(async i =>
        {
            using var c = Cliente();
            await salidaSimultanea.WaitAsync();
            var r = await PedirAsync(c, HttpMethod.Post, "api/portal/reservas", new
            {
                huesped = new
                {
                    tipoDocumento = "CC", numeroDocumento = documentoBase + i,
                    nombres = "Concurrente", apellidos = "Numero" + i,
                    telefono = "3000000000", correo = CorreoDePrueba, extranjeroNoResidente = false,
                },
                idTipo,
                idHabitacion,
                fechaEntrada = entrada.ToString("yyyy-MM-dd"),
                fechaSalida = salida.ToString("yyyy-MM-dd"),
                numeroPersonas = 1,
                medioPago = "EFECTIVO",
                tarjeta = (object?)null,
                canalComprobante = "CORREO",
                destinoComprobante = CorreoDePrueba,
            });
            codigos[i] = (int)r.Codigo + (r.Cuerpo.Contains("50001") ? " (50001)" : "");
        }).ToArray();

        salidaSimultanea.Release(n);
        await Task.WhenAll(tareas);
        reloj.Stop();

        var exitosas = codigos.Count(x => x.StartsWith("200"));
        var conflictos = codigos.Count(x => x.StartsWith("409") || x.Contains("50001"));
        var otros = n - exitosas - conflictos;

        Registrar("C-01", "Concurrencia", $"{n} reservas simultáneas sobre la habitación {idHabitacion}",
                  "exactamente 1 confirmada y el resto rechazadas",
                  $"{exitosas} confirmada(s), {conflictos} conflicto(s), {otros} otro(s)",
                  exitosas == 1 && otros == 0, reloj.ElapsedMilliseconds);

        return new InformeConcurrencia(n, exitosas, conflictos, otros, reloj.ElapsedMilliseconds, codigos.ToList());
    }

    // ------------------------------------------------------ 4. rendimiento

    public record InformeRendimiento(int Peticiones, int Paralelas, double P50, double P95, double P99,
                                     double Media, double Maximo, double PorSegundo, int Errores);

    private static async Task<InformeRendimiento?> SuiteRendimientoAsync(int n)
    {
        Titulo("Suite 4. Prueba de rendimiento");

        const int paralelas = 8;
        var entrada = DateTime.Today.AddDays(15);
        var salida = entrada.AddDays(2);
        var ruta = $"api/portal/catalogo?entrada={entrada:yyyy-MM-dd}&salida={salida:yyyy-MM-dd}&personas=2";
        var tiempos = new double[n];
        var errores = 0;

        using var cliente = Cliente();
        // Calentamiento: las primeras peticiones pagan la compilación del plan de consulta y el
        // arranque en frío de Entity Framework, y distorsionarían los percentiles.
        for (var i = 0; i < 5; i++) await PedirAsync(cliente, HttpMethod.Get, ruta);

        var reloj = Stopwatch.StartNew();
        await Parallel.ForEachAsync(Enumerable.Range(0, n),
            new ParallelOptions { MaxDegreeOfParallelism = paralelas },
            async (i, _) =>
            {
                var r = await PedirAsync(cliente, HttpMethod.Get, ruta);
                tiempos[i] = r.Ms;
                if (r.Codigo != HttpStatusCode.OK) Interlocked.Increment(ref errores);
            });
        reloj.Stop();

        Array.Sort(tiempos);
        double Pct(double p) => tiempos[Math.Min(n - 1, (int)Math.Ceiling(p / 100.0 * n) - 1)];
        var porSegundo = n / Math.Max(0.001, reloj.Elapsed.TotalSeconds);

        var informe = new InformeRendimiento(n, paralelas, Pct(50), Pct(95), Pct(99),
                                             tiempos.Average(), tiempos[^1], porSegundo, errores);

        Console.WriteLine($"  Peticiones: {n} con {paralelas} en paralelo");
        Console.WriteLine($"  p50 {informe.P50:0} ms | p95 {informe.P95:0} ms | p99 {informe.P99:0} ms | máximo {informe.Maximo:0} ms");
        Console.WriteLine($"  Media {informe.Media:0.0} ms | {informe.PorSegundo:0.0} peticiones por segundo | {errores} error(es)");

        // RNF: el percentil 95 de una consulta de catálogo debe quedar por debajo de dos segundos.
        Registrar("R-01", "Rendimiento", $"Consulta de catálogo, {n} peticiones, {paralelas} en paralelo",
                  "p95 menor que 2000 ms y sin errores",
                  $"p95 {informe.P95:0} ms, {errores} error(es)",
                  informe.P95 < 2000 && errores == 0, reloj.ElapsedMilliseconds);

        return informe;
    }

    // ------------------------------------------- 5. tolerancia a fallos

    private static async Task SuiteToleranciaAsync()
    {
        Titulo("Suite 5. Tolerancia a fallos");
        using var c = Cliente();

        var r = await PedirAsync(c, HttpMethod.Get, "health");
        Registrar("T-01", "Tolerancia", "Recurso de vida del proceso",
                  "200", $"{(int)r.Codigo}", r.Codigo == HttpStatusCode.OK, r.Ms);

        r = await PedirAsync(c, HttpMethod.Get, "health/ready");
        var listo = r.Codigo == HttpStatusCode.OK && r.Cuerpo.Contains("Healthy");
        Registrar("T-02", "Tolerancia", "Recurso de disponibilidad, que consulta SQL Server",
                  "200 con estado Healthy", $"{(int)r.Codigo}", listo, r.Ms);

        // Degradación del asistente: sin proveedor de modelo configurado, el intérprete de reglas
        // responde la misma consulta con los mismos datos.
        r = await PedirAsync(c, HttpMethod.Get, "api/asistente/estado");
        var interprete = "desconocido";
        if (r.Codigo == HttpStatusCode.OK)
        {
            try { using var doc = JsonDocument.Parse(r.Cuerpo); interprete = doc.RootElement.GetProperty("interprete").GetString() ?? "?"; }
            catch { }
        }
        Registrar("T-03", "Tolerancia", "El asistente declara qué intérprete está activo",
                  "REGLAS o el proveedor configurado", interprete,
                  r.Codigo == HttpStatusCode.OK && interprete != "desconocido", r.Ms);

        r = await PedirAsync(c, HttpMethod.Post, "api/asistente",
                             new { consulta = "¿Qué habitaciones hay libres la próxima semana?" });
        Registrar("T-04", "Tolerancia", "El asistente responde aunque no haya modelo externo",
                  "200 con respuesta del intérprete de respaldo", $"{(int)r.Codigo}",
                  r.Codigo == HttpStatusCode.OK, r.Ms);

        // Tiempo de espera del cliente: se apunta a un puerto donde no hay nada escuchando y se
        // comprueba que el fallo llega acotado y no deja la aplicación colgada.
        using var muerto = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:59999/"), Timeout = TimeSpan.FromSeconds(8) };
        var reloj = Stopwatch.StartNew();
        var fallo = false;
        try { await muerto.GetAsync("health"); } catch { fallo = true; }
        reloj.Stop();
        Registrar("T-05", "Tolerancia", "Servicio inexistente: el cliente falla de forma acotada",
                  "error en menos de 9 segundos", $"{(fallo ? "error" : "respuesta")} en {reloj.ElapsedMilliseconds} ms",
                  fallo && reloj.ElapsedMilliseconds < 9000, reloj.ElapsedMilliseconds);
    }

    // ------------------------------------------------------------ salida

    private static string Recortar(string t, int n) => t.Length <= n ? t : t[..n] + "…";

    private static void Resumen()
    {
        Titulo("Resumen");
        foreach (var grupo in Resultados.GroupBy(x => x.Suite))
            Console.WriteLine($"  {grupo.Key,-14} {grupo.Count(x => x.Paso)} de {grupo.Count()} pasaron");
    }

    private static async Task EscribirEvidenciasAsync(string carpeta, InformeConcurrencia? conc,
                                                      InformeRendimiento? rend, string? reserva)
    {
        Directory.CreateDirectory(carpeta);
        var rutaJson = Path.Combine(carpeta, "evidencias.json");
        var rutaMd = Path.Combine(carpeta, "evidencias.md");

        await File.WriteAllTextAsync(rutaJson, JsonSerializer.Serialize(new
        {
            fecha = DateTime.Now,
            api = _api,
            equipo = new
            {
                sistema = Environment.OSVersion.VersionString,
                procesadores = Environment.ProcessorCount,
                dotnet = Environment.Version.ToString(),
            },
            resultados = Resultados,
            concurrencia = conc,
            rendimiento = rend,
            reservaDePrueba = reserva,
        }, new JsonSerializerOptions { WriteIndented = true }));

        var texto = new System.Text.StringBuilder();
        texto.AppendLine("# Evidencias de pruebas · Hotel Reservas");
        texto.AppendLine();
        texto.AppendLine($"Ejecución del {DateTime.Now.ToString("d 'de' MMMM 'de' yyyy, HH:mm", Es)} contra {_api}.");
        texto.AppendLine($"Equipo: {Environment.ProcessorCount} procesadores lógicos, {Environment.OSVersion.VersionString}, .NET {Environment.Version}.");
        texto.AppendLine();
        foreach (var grupo in Resultados.GroupBy(x => x.Suite))
        {
            texto.AppendLine($"## {grupo.Key}");
            texto.AppendLine();
            foreach (var x in grupo)
                texto.AppendLine($"{x.Id}. {x.Caso}. Esperado: {x.Esperado}. Obtenido: {x.Obtenido}. Resultado: {(x.Paso ? "pasa" : "falla")}. Duración: {x.Ms} ms.");
            texto.AppendLine();
        }
        if (conc != null)
        {
            texto.AppendLine("## Detalle de la prueba de concurrencia");
            texto.AppendLine();
            texto.AppendLine($"{conc.Intentos} peticiones simultáneas sobre la misma habitación y las mismas fechas. "
                           + $"Confirmadas {conc.Exitosas}, rechazadas por conflicto {conc.Conflictos}, otros resultados {conc.Otros}. "
                           + $"Duración total {conc.MsTotal} ms. Códigos devueltos: {string.Join(", ", conc.Codigos)}.");
            texto.AppendLine();
        }
        if (rend != null)
        {
            texto.AppendLine("## Detalle de la prueba de rendimiento");
            texto.AppendLine();
            texto.AppendLine($"{rend.Peticiones} consultas de catálogo con {rend.Paralelas} en paralelo. "
                           + $"Percentil 50 {rend.P50:0} ms, percentil 95 {rend.P95:0} ms, percentil 99 {rend.P99:0} ms, "
                           + $"media {rend.Media:0.0} ms, máximo {rend.Maximo:0} ms, "
                           + $"{rend.PorSegundo:0.0} peticiones por segundo, {rend.Errores} errores.");
            texto.AppendLine();
        }
        await File.WriteAllTextAsync(rutaMd, texto.ToString());

        Console.WriteLine();
        Console.WriteLine($"Evidencias escritas en:");
        Console.WriteLine($"  {rutaMd}");
        Console.WriteLine($"  {rutaJson}");
    }
}
