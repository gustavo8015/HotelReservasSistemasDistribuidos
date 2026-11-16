using HotelReservas.Api.Asistente;
using HotelReservas.Api.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelReservas.Api.Controllers;

/// <summary>
/// Recurso del servicio de IA (sección 13). Solo lectura: nunca crea, modifica ni cancela reservas.
///
/// Abierto a los tres perfiles, con un catálogo de funciones distinto para cada uno: el personal
/// consulta la operación del hotel, el cliente solo sus propias reservas, y el visitante del portal
/// sin cuenta solo lo que ya es público en el catálogo de habitaciones. El recorte lo hace
/// CatalogoFunciones.ParaRol a partir del rol del token, no de lo que diga la pantalla.
/// </summary>
[ApiController]
[Route("api/asistente")]
[AllowAnonymous]
public class AsistenteController : ControllerBase
{
    /// <summary>
    /// Versión del asistente que trae esta compilación de la API. Se devuelve en /estado y se
    /// muestra en pantalla: así se ve de inmediato si la API que está corriendo es la que se
    /// acabó de copiar o una compilación anterior, que es el error más fácil de cometer.
    /// </summary>
    public const string Version = "v0.16.1";

    private readonly OrquestadorAsistente _orq;
    public AsistenteController(OrquestadorAsistente orq) => _orq = orq;

    /// <summary>Rol del token; cadena vacía cuando la consulta viene del portal sin iniciar sesión.</summary>
    private string RolActual => User?.Identity?.IsAuthenticated == true
        ? (User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "")
        : "";

    private int IdActual => User?.Identity?.IsAuthenticated == true ? User.IdUsuario() : 0;

    /// <summary>POST /api/asistente  { "consulta": "¿qué dobles hay libres del 16 al 18 de octubre?" }</summary>
    [HttpPost]
    public async Task<ActionResult<RespuestaAsistente>> Consultar([FromBody] SolicitudAsistente s, CancellationToken ct)
        => Ok(await _orq.ProcesarAsync(s.Consulta, IdActual, RolActual, ct));

    /// <summary>GET /api/asistente/funciones: catálogo de lectura del perfil que hace la consulta.</summary>
    [HttpGet("funciones")]
    public ActionResult<IEnumerable<DescripcionFuncion>> Funciones() => Ok(CatalogoFunciones.ParaRol(RolActual));

    /// <summary>GET /api/asistente/estado: qué intérprete está activo y cuántas funciones ve este perfil.</summary>
    [HttpGet("estado")]
    public ActionResult Estado([FromServices] InterpreteAzureOpenAI ia, [FromServices] InterpreteCompatibleOpenAI modelo)
        => Ok(new
        {
            interprete = _orq.NombreInterpreteActivo,
            modeloConfigurado = modelo.Disponible || ia.Disponible,
            proveedor = modelo.Disponible ? modelo.Nombre : ia.Disponible ? ia.Nombre : "REGLAS",
            perfil = string.IsNullOrEmpty(RolActual) ? "VISITANTE" : RolActual,
            funciones = CatalogoFunciones.ParaRol(RolActual).Count,
            version = Version,
        });
}
