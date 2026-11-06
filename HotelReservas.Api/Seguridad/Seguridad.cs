using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HotelReservas.Api.Data;
using Microsoft.IdentityModel.Tokens;

namespace HotelReservas.Api.Seguridad;

/// <summary>
/// Hash de contraseñas con PBKDF2-SHA256 (RNF01: ninguna contraseña en texto claro).
/// Formato almacenado en USUARIO.clave_hash: PBKDF2$iteraciones$salBase64$hashBase64
/// </summary>
public static class HashClave
{
    private const int Iteraciones = 100_000;

    public static string Generar(string clave)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(clave, sal, Iteraciones, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string clave, string almacenado)
    {
        var partes = almacenado.Split('$');
        if (partes.Length != 4 || partes[0] != "PBKDF2") return false;
        var iter = int.Parse(partes[1]);
        var sal = Convert.FromBase64String(partes[2]);
        var esperado = Convert.FromBase64String(partes[3]);
        var hash = Rfc2898DeriveBytes.Pbkdf2(clave, sal, iter, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(hash, esperado);
    }
}

/// <summary>Emite el token JWT que la aplicación móvil envía en cada solicitud (RNF01).</summary>
public class ServicioToken
{
    private readonly IConfiguration _cfg;
    public ServicioToken(IConfiguration cfg) => _cfg = cfg;

    public (string token, DateTime expira) Emitir(Usuario u)
    {
        var expira = DateTime.UtcNow.AddMinutes(_cfg.GetValue<int>("Jwt:MinutosVigencia", 480));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, u.IdUsuario.ToString()),
            new Claim(ClaimTypes.NameIdentifier, u.IdUsuario.ToString()),
            new Claim(ClaimTypes.Name, u.Nombre),
            new Claim(ClaimTypes.Email, u.Correo),
            new Claim(ClaimTypes.Role, u.Rol),
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
        var jwt = new JwtSecurityToken(
            issuer: _cfg["Jwt:Issuer"], audience: _cfg["Jwt:Audience"], claims: claims,
            expires: expira, signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expira);
    }
}

public static class ClaimsExtensiones
{
    /// <summary>Identificador del usuario autenticado, tomado del token.</summary>
    public static int IdUsuario(this ClaimsPrincipal p) =>
        int.Parse(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "0");
}
