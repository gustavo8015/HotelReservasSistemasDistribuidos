using System.Text;
using HotelReservas.Api.Asistente;
using HotelReservas.Api.Data;
using HotelReservas.Api.Seguridad;
using HotelReservas.Api.Servicios;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// Hotel Reservas - API REST (ASP.NET Core 8). Capa de lógica de negocio y de acceso a datos de la
// arquitectura en capas (sección 4): la app .NET MAUI solo habla con esta API por HTTPS + JWT y la
// API es el único punto que toca la base de datos SQL Server.
var builder = WebApplication.CreateBuilder(args);

// Capa de acceso a datos
builder.Services.AddDbContext<HotelReservasContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("HotelReservas")));

// Seguridad (RNF01): JWT firmado con clave simétrica, autorización por perfil
var clave = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Falta Jwt:Key en la configuración.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1),
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave)),
    };
});
builder.Services.AddAuthorization();
builder.Services.AddScoped<ServicioToken>();

// Lógica de negocio
builder.Services.AddScoped<ServicioReservas>();
builder.Services.AddScoped<PasarelaSimulada>();   // pasarela de pagos simulada del portal (RF21)

// Servicio de IA (sección 13): catálogo de funciones de lectura, intérpretes y orquestador
builder.Services.AddScoped<CatalogoFunciones>();
builder.Services.AddScoped<InterpreteReglas>();
builder.Services.AddHttpClient<InterpreteAzureOpenAI>(c => c.Timeout = TimeSpan.FromSeconds(30));
// Intérprete contra cualquier servicio con interfaz compatible con OpenAI (sección Asistente de
// appsettings.json). Si no hay clave configurada, se declara no disponible y manda el de reglas.
builder.Services.AddHttpClient<InterpreteCompatibleOpenAI>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<OrquestadorAsistente>();

builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Hotel Reservas API", Version = "v0.3", Description = "Sistema transaccional de reservas de habitaciones. Incluye el portal del usuario final con pago simulado y el asistente de consultas en lenguaje natural (solo lectura)." });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Description = "Pegue aquí el token devuelto por POST /api/auth/login" });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() } });
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ---------------------------------------------------------------------------
// Verificacion del esquema al arrancar.
//
// Cada entrega nueva suele traer un guion de base de datos. Si no se ejecuta, la
// API arranca bien y revienta despues, en la primera consulta, con un error de
// columna o de procedimiento que no dice cual guion falta. Esta comprobacion lo
// dice en una linea al iniciar, antes de que nadie abra el portal.
//
// Solo lee los catalogos del sistema y nunca impide que la API arranque.
// ---------------------------------------------------------------------------
using (var ambito = app.Services.CreateScope())
{
    var registro = ambito.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Esquema");
    try
    {
        var contexto = ambito.ServiceProvider.GetRequiredService<HotelReservasContext>();
        var cn = contexto.Database.GetDbConnection();
        await cn.OpenAsync();

        async Task<bool> ExisteAsync(string sql)
        {
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = sql;
            var v = await cmd.ExecuteScalarAsync();
            return v != null && v != DBNull.Value;
        }

        var faltantes = new List<string>();
        if (!await ExisteAsync("SELECT COL_LENGTH('TIPO_HABITACION','imagen')"))      faltantes.Add("07_portal_cliente_imagenes_pago.sql");
        if (!await ExisteAsync("SELECT COL_LENGTH('TIPO_HABITACION','comodidades')")) faltantes.Add("08_galeria_y_comodidades.sql");
        if (!await ExisteAsync("SELECT OBJECT_ID('FOTO_HABITACION')"))                faltantes.Add("08_galeria_y_comodidades.sql");
        if (!await ExisteAsync("SELECT COL_LENGTH('RESERVA','limite_pago')"))         faltantes.Add("18_plazo_de_pago_y_cancelacion.sql");
        if (!await ExisteAsync("SELECT OBJECT_ID('sp_LiberarReservasVencidas')"))     faltantes.Add("18_plazo_de_pago_y_cancelacion.sql");
        // La bitácora del asistente valida el nombre de la función contra una lista fija: si todavía
        // tiene la del guion 06, las funciones nuevas no se pueden registrar.
        if (!await ExisteAsync("SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CONSULTA_funcion' AND definition LIKE '%consultarPoliticas%'"))
            faltantes.Add("19_catalogo_asistente_ampliado.sql");

        await cn.CloseAsync();

        if (faltantes.Count > 0)
        {
            foreach (var g in faltantes.Distinct())
                registro.LogError("FALTA EJECUTAR EL GUION database/{Guion} sobre la base HotelReservas. "
                                + "Hasta que no se ejecute, las pantallas que dependen de el van a fallar.", g);
        }
        else
        {
            registro.LogInformation("Esquema verificado: la base tiene todas las columnas y procedimientos que esta version necesita.");
        }
    }
    catch (Exception ex)
    {
        registro.LogWarning("No fue posible verificar el esquema al arrancar: {Mensaje}", ex.Message);
    }
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseDefaultFiles();   // sirve la aplicación web (wwwroot/index.html) en la raíz
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/api/salud", () => Results.Ok(new { estado = "ok", fecha = DateTime.Now }));

// La aplicación web publicada dentro de wwwroot usa rutas propias (/reservar/2, /mis-reservas...).
// Sin este respaldo el servidor respondería 404 a esas direcciones al recargar la página o al
// entrar por un enlace directo: aquí se devuelve el documento de la aplicación y ella resuelve
// la ruta.
//
// El respaldo excluye de forma explícita todo lo que empiece por /api. Si no lo hiciera, una
// petición a un recurso del servicio que no coincide con ninguna ruta (por ejemplo
// /api/portal/comprobantes/?documento= cuando el número viene vacío) recibiría la página HTML
// de la aplicación, y el cliente intentaría leerla como JSON. Ese es el origen del mensaje
// "'<' is an invalid start of a value": la respuesta empezaba por el signo menor que de una
// etiqueta HTML. Ahora esas rutas devuelven un 404 en formato JSON, que el cliente sí entiende.
app.MapFallback(async contexto =>
{
    if (contexto.Request.Path.StartsWithSegments("/api"))
    {
        contexto.Response.StatusCode = StatusCodes.Status404NotFound;
        await contexto.Response.WriteAsJsonAsync(new { mensaje = "La ruta solicitada no existe en la API. Revise que la consulta lleve todos sus datos." });
        return;
    }

    var raiz = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    var documento = Path.Combine(raiz, "index.html");
    if (!File.Exists(documento)) { contexto.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    contexto.Response.ContentType = "text/html; charset=utf-8";
    await contexto.Response.SendFileAsync(documento);
});

app.Run();
