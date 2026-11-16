using HotelReservas.Api.Data;
using HotelReservas.Api.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HotelReservas.Api.Controllers;

public record SolicitudLogin(string Correo, string Clave);
public record RespuestaLogin(string Token, DateTime Expira, string Nombre, string Rol, int? IdHuesped = null, string? Documento = null);
public record SolicitudRegistro(string TipoDocumento, string NumeroDocumento, string Nombres, string Apellidos,
    string? Telefono, string Correo, string Clave, bool ExtranjeroNoResidente);

/// <summary>CU-01 / RF01: inicio de sesión y emisión del token JWT.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly HotelReservasContext _db;
    private readonly ServicioToken _token;
    public AuthController(HotelReservasContext db, ServicioToken token) { _db = db; _token = token; }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<RespuestaLogin>> Login([FromBody] SolicitudLogin s)
    {
        var u = await _db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Correo == s.Correo && x.Activo);
        if (u == null || !HashClave.Verificar(s.Clave, u.ClaveHash))
            return Unauthorized(new { mensaje = "Correo o contraseña incorrectos." });
        var (token, expira) = _token.Emitir(u);
        var doc = u.IdHuesped == null ? null : await _db.Huespedes.Where(h => h.IdHuesped == u.IdHuesped).Select(h => h.NumeroDocumento).FirstOrDefaultAsync();
        return Ok(new RespuestaLogin(token, expira, u.Nombre, u.Rol, u.IdHuesped, doc));
    }

    /// <summary>
    /// POST /api/auth/registro (CU-16 / RF21): el usuario final crea su cuenta de CLIENTE.
    /// La cuenta queda unida a su ficha de HUESPED por el número de documento, de modo que
    /// "Mis reservas" solo muestra lo suyo. Si ya existía la ficha, se reutiliza en lugar de
    /// duplicarla. La contraseña se guarda con PBKDF2 (RNF01); nunca en texto claro.
    /// </summary>
    [HttpPost("registro")]
    [AllowAnonymous]
    public async Task<ActionResult<RespuestaLogin>> Registro([FromBody] SolicitudRegistro s)
    {
        var correo = (s.Correo ?? "").Trim().ToLowerInvariant();
        var doc = (s.NumeroDocumento ?? "").Trim();
        var nombres = (s.Nombres ?? "").Trim();
        if (!correo.Contains('@') || correo.Length < 6) return BadRequest(new { mensaje = "Escriba un correo válido." });
        if ((s.Clave ?? "").Length < 8) return BadRequest(new { mensaje = "La contraseña debe tener al menos ocho caracteres." });
        if (doc.Length < 5) return BadRequest(new { mensaje = "Escriba su número de documento." });
        if (nombres.Length < 2) return BadRequest(new { mensaje = "Escriba su nombre." });
        if (await _db.Usuarios.AnyAsync(u => u.Correo == correo)) return Conflict(new { mensaje = "Ya existe una cuenta con ese correo. Inicie sesión." });

        var h = await _db.Huespedes.FirstOrDefaultAsync(x => x.NumeroDocumento == doc);
        if (h == null)
        {
            h = new Huesped
            {
                TipoDocumento = string.IsNullOrWhiteSpace(s.TipoDocumento) ? "CC" : s.TipoDocumento.Trim().ToUpperInvariant(),
                NumeroDocumento = doc, Nombres = nombres, Apellidos = (s.Apellidos ?? "").Trim(),
                Telefono = string.IsNullOrWhiteSpace(s.Telefono) ? null : s.Telefono.Trim(),
                Correo = correo, ExtranjeroNoResidente = s.ExtranjeroNoResidente,
            };
            _db.Huespedes.Add(h);
            await _db.SaveChangesAsync();
        }
        else
        {
            if (await _db.Usuarios.AnyAsync(u => u.IdHuesped == h.IdHuesped))
                return Conflict(new { mensaje = "Ese documento ya tiene una cuenta. Inicie sesión o recupere el acceso con el administrador." });
            if (string.IsNullOrWhiteSpace(h.Correo)) h.Correo = correo;
            if (string.IsNullOrWhiteSpace(h.Telefono) && !string.IsNullOrWhiteSpace(s.Telefono)) h.Telefono = s.Telefono.Trim();
        }

        var u2 = new Usuario
        {
            Nombre = $"{h.Nombres} {h.Apellidos}".Trim(), Correo = correo,
            ClaveHash = HashClave.Generar(s.Clave!), Rol = "CLIENTE", Activo = true, IdHuesped = h.IdHuesped,
        };
        _db.Usuarios.Add(u2);
        await _db.SaveChangesAsync();

        var (t, exp) = _token.Emitir(u2);
        return Ok(new RespuestaLogin(t, exp, u2.Nombre, u2.Rol, h.IdHuesped, h.NumeroDocumento));
    }

    /// <summary>Utilidad para el administrador: genera el hash PBKDF2 de una contraseña (para actualizar USUARIO.clave_hash).</summary>
    [HttpPost("hash")]
    [Authorize(Roles = "ADMINISTRADOR")]
    public ActionResult Hash([FromBody] string clave) => Ok(new { hash = HashClave.Generar(clave) });
}
