using HotelReservas.Api.Data;
using HotelReservas.Api.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HotelReservas.Api.Controllers;

public record NuevoUsuario(string Nombre, string Correo, string Clave);

/// <summary>
/// CU-15 Gestionar usuarios. Solo el ADMINISTRADOR puede listar, crear, eliminar o reactivar
/// recepcionistas. El recepcionista recibe 403 Forbidden en todos estos recursos.
/// </summary>
[ApiController]
[Route("api/usuarios")]
[Authorize(Roles = "ADMINISTRADOR")]
public class UsuariosController : ControllerBase
{
    private readonly HotelReservasContext _db;
    public UsuariosController(HotelReservasContext db) => _db = db;

    /// <summary>Cuenta técnica que el script 07 crea para las reservas hechas desde el portal sin iniciar sesión.</summary>
    private const string CorreoPortal = "portal@hotel.com";

    /// <summary>GET /api/usuarios: todos los usuarios con el número de reservas registradas por cada uno.</summary>
    [HttpGet]
    public async Task<ActionResult> Listar()
    {
        var lista = await _db.Usuarios.AsNoTracking().OrderBy(u => u.Rol).ThenBy(u => u.Nombre)
            .Select(u => new
            {
                u.IdUsuario, u.Nombre, u.Correo, u.Rol, u.Activo,
                reservas = _db.Reservas.Count(r => r.IdUsuario == u.IdUsuario),
                movimientos = _db.Bitacora.Count(b => b.IdUsuario == u.IdUsuario),
            }).ToListAsync();
        return Ok(lista);
    }

    /// <summary>POST /api/usuarios: crea una cuenta de CLIENTE con contraseña protegida con PBKDF2. Correo duplicado → 409.</summary>
    [HttpPost]
    public async Task<ActionResult> Crear([FromBody] NuevoUsuario n)
    {
        var correo = (n.Correo ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(n.Nombre) || correo.Length == 0 || !correo.Contains('@'))
            return BadRequest(new { mensaje = "Nombre y correo válido son obligatorios." });
        if ((n.Clave ?? "").Length < 8)
            return BadRequest(new { mensaje = "La contraseña debe tener al menos 8 caracteres." });
        if (await _db.Usuarios.AnyAsync(u => u.Correo == correo))
            return Conflict(new { mensaje = "Ya existe un usuario con ese correo." });

        var u = new Usuario { Nombre = n.Nombre.Trim(), Correo = correo, ClaveHash = HashClave.Generar(n.Clave!), Rol = "CLIENTE", Activo = true };
        _db.Usuarios.Add(u);
        await _db.SaveChangesAsync();
        return Created($"/api/usuarios/{u.IdUsuario}", new { u.IdUsuario, u.Nombre, u.Correo, u.Rol, u.Activo });
    }

    /// <summary>
    /// DELETE /api/usuarios/{id}: elimina una cuenta de cliente. Si el usuario ya registró reservas o movimientos
    /// de bitácora, las claves foráneas (FK_RESERVA_USUARIO, FK_BITACORA_USUARIO) impiden borrarlo sin perder
    /// trazabilidad (RNF05); en ese caso se desactiva (activo = 0) y ya no puede iniciar sesión.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Eliminar(int id)
    {
        var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.IdUsuario == id);
        if (u == null) return NotFound(new { mensaje = "El usuario no existe." });
        if (u.Rol == "ADMINISTRADOR") return BadRequest(new { mensaje = "No se puede eliminar la cuenta del administrador." });
        if (u.Correo == CorreoPortal) return BadRequest(new { mensaje = "La cuenta técnica del portal no se elimina: es la responsable de las reservas que hacen los visitantes sin cuenta (FK_RESERVA_USUARIO)." });
        if (u.IdUsuario == User.IdUsuario()) return BadRequest(new { mensaje = "No puede eliminar su propia cuenta." });

        bool tieneHistorial = await _db.Reservas.AnyAsync(r => r.IdUsuario == id)
                              || await _db.Bitacora.AnyAsync(b => b.IdUsuario == id)
                              || await _db.ConsultasAsistente.AnyAsync(c => c.IdUsuario == id);
        if (tieneHistorial)
        {
            u.Activo = false;
            await _db.SaveChangesAsync();
            return Ok(new { eliminado = false, desactivado = true, mensaje = $"{u.Nombre} tiene reservas o movimientos registrados: se desactivó para conservar la trazabilidad y ya no puede iniciar sesión." });
        }
        _db.Usuarios.Remove(u);
        await _db.SaveChangesAsync();
        return Ok(new { eliminado = true, desactivado = false, mensaje = $"{u.Nombre} fue eliminado definitivamente." });
    }

    /// <summary>PUT /api/usuarios/{id}/reactivar: vuelve a habilitar el inicio de sesión de una cuenta desactivada.</summary>
    [HttpPut("{id:int}/reactivar")]
    public async Task<ActionResult> Reactivar(int id)
    {
        var u = await _db.Usuarios.FirstOrDefaultAsync(x => x.IdUsuario == id);
        if (u == null) return NotFound(new { mensaje = "El usuario no existe." });
        if (u.Correo == CorreoPortal) return BadRequest(new { mensaje = "La cuenta técnica del portal permanece inactiva a propósito: nadie debe poder iniciar sesión con ella." });
        u.Activo = true;
        await _db.SaveChangesAsync();
        return Ok(new { u.IdUsuario, u.Activo, mensaje = $"{u.Nombre} fue reactivado." });
    }
}
