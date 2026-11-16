using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using HotelReservas.Api.Data;
using HotelReservas.Api.Servicios;
using Microsoft.EntityFrameworkCore;

namespace HotelReservas.Api.Asistente;

/// <summary>Filtro de datos personales: lo que sale hacia el servicio externo no lleva documentos, teléfonos ni correos (Ley 1581 de 2012).</summary>
public static class FiltroDatosPersonales
{
    public static string Aplicar(string texto)
    {
        texto = Regex.Replace(texto, @"[\w.+-]+@[\w-]+\.[\w.]+", "[correo]");
        texto = Regex.Replace(texto, @"\b(?:cc|ce|ti|pa|nit|pasaporte|cedula|cédula)\s*[:#]?\s*[a-z]{0,2}\d{5,}\b", "[documento]", RegexOptions.IgnoreCase);
        texto = Regex.Replace(texto, @"(?<![\d-])(?:\+?57\s?)?3\d{2}[\s-]?\d{3}[\s-]?\d{4}(?!\d)", "[telefono]");   // celulares colombianos
        texto = Regex.Replace(texto, @"(?<!rsv)(?<![\d-])\d{7,12}(?![\d-])", "[numero]", RegexOptions.IgnoreCase);     // documentos sueltos (no códigos RSV ni fechas)
        return texto;
    }
}

/// <summary>
/// Lógica de integración (sección 13.3.3): recibe la consulta con el usuario autenticado, aplica el filtro de
/// datos personales, pide al intérprete (modelo de IA o reglas) la función a ejecutar, valida que pertenezca
/// al catálogo de solo lectura, la ejecuta sobre el repositorio central, registra la consulta en
/// CONSULTA_ASISTENTE y devuelve la respuesta redactada junto con los datos estructurados.
/// </summary>
public class OrquestadorAsistente
{
    private readonly CatalogoFunciones _catalogo;
    private readonly InterpreteAzureOpenAI _ia;
    private readonly InterpreteCompatibleOpenAI _modelo;
    private readonly InterpreteReglas _reglas;
    private readonly HotelReservasContext _db;
    private readonly ILogger<OrquestadorAsistente> _log;

    public OrquestadorAsistente(CatalogoFunciones catalogo, InterpreteAzureOpenAI ia, InterpreteCompatibleOpenAI modelo,
                                InterpreteReglas reglas, HotelReservasContext db, ILogger<OrquestadorAsistente> log)
    { _catalogo = catalogo; _ia = ia; _modelo = modelo; _reglas = reglas; _db = db; _log = log; }

    /// <summary>Ajusta el texto al ancho de su columna en CONSULTA_ASISTENTE antes de guardarlo.</summary>
    private static string? Recortar(string? t, int maximo)
        => t == null || t.Length <= maximo ? t : t[..(maximo - 1)] + "…";

    /// <summary>
    /// Intérprete activo, en orden: el proveedor configurado en la sección Asistente, el de Azure
    /// si quedara configurado de antes, y como último recurso el de reglas, que no necesita
    /// internet ni cuenta y por eso garantiza que la pantalla nunca quede muda.
    /// </summary>
    private IInterpreteConsultas Activo => _modelo.Disponible ? _modelo : _ia.Disponible ? _ia : _reglas;

    public string NombreInterpreteActivo => Activo.Nombre;

    /// <param name="rol">Perfil de quien pregunta. Decide qué funciones ve el modelo y cuáles se
    /// aceptan al validar. Vacío o nulo es un visitante del portal sin cuenta.</param>
    public async Task<RespuestaAsistente> ProcesarAsync(string consulta, int idUsuario, string? rol, CancellationToken ct)
    {
        var reloj = Stopwatch.StartNew();
        consulta = (consulta ?? "").Trim();
        if (consulta.Length == 0) return new RespuestaAsistente("Escriba su consulta.", null, null, "NINGUNO", false, 0);
        if (consulta.Length > 500) consulta = consulta[..500];

        var consultaFiltrada = FiltroDatosPersonales.Aplicar(consulta);
        var catalogo = CatalogoFunciones.ParaRol(rol);
        IInterpreteConsultas interprete = Activo;
        Interpretacion interp;
        try
        {
            interp = await interprete.InterpretarAsync(consultaFiltrada, catalogo, ct);
        }
        catch (Exception ex) when (interprete != _reglas)
        {
            _log.LogWarning(ex, "El modelo externo no respondió; se usa el intérprete de reglas.");
            interprete = _reglas;
            interp = await _reglas.InterpretarAsync(consultaFiltrada, catalogo, ct);
        }

        string? funcion = interp.Funcion; string respuesta; bool exitosa = true; object? datos = null;
        if (funcion == null)
        {
            respuesta = interp.MensajeDirecto ?? "¿Podría precisar su consulta?";
        }
        else if (catalogo.All(d => d.Nombre != funcion))
        {
            // Regla 1: solo funciones de lectura, y solo las del perfil de quien pregunta.
            // Se valida de nuevo aquí aunque el modelo haya recibido el catálogo recortado,
            // porque la respuesta del modelo no es de fiar por sí sola.
            _log.LogWarning("El intérprete devolvió una función fuera del catálogo del perfil {Rol}: {Funcion}", rol ?? "(sin cuenta)", funcion);
            respuesta = CatalogoFunciones.Descripciones.Any(d => d.Nombre == funcion)
                ? "Esa consulta es del personal del hotel. Puedo ayudarle con disponibilidad, tarifas, sus propias reservas y las condiciones de pago."
                : "Esa operación no está disponible desde el asistente.";
            exitosa = false;
        }
        else
        {
            try
            {
                var resultado = await _catalogo.EjecutarAsync(funcion, interp.Parametros, ct, idUsuario);
                datos = resultado.Datos;
                try { respuesta = await interprete.RedactarAsync(consultaFiltrada, resultado, ct); }
                catch (Exception ex) { _log.LogWarning(ex, "Fallo al redactar con IA; se devuelve el resumen de la función."); respuesta = resultado.ResumenParaModelo; }
            }
            catch (ErrorNegocio ex) { respuesta = ex.Message; exitosa = false; }
        }

        reloj.Stop();
        // CONSULTA_ASISTENTE exige un usuario. El visitante del portal no tiene cuenta, de modo
        // que su consulta se atribuye al usuario del portal, igual que sus reservas sin cuenta.
        var idRegistro = idUsuario > 0
            ? idUsuario
            : await _db.Usuarios.AsNoTracking().Where(u => u.Correo == "portal@hotel.com").Select(u => u.IdUsuario).FirstOrDefaultAsync(ct);
        if (idRegistro > 0)
        {
            _db.ConsultasAsistente.Add(new ConsultaAsistente
            {
                IdUsuario = idRegistro, Fecha = DateTime.Now,
                TextoConsulta = Recortar(consultaFiltrada, 500), FuncionInvocada = funcion,
                Parametros = Recortar(interp.Parametros?.GetRawText(), 400), Interprete = interprete.Nombre,
                Exitosa = exitosa, DuracionMs = (int)reloj.ElapsedMilliseconds,
            });
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // La bitácora no puede tumbar una consulta de solo lectura: el huésped recibiría un
                // error 500 por una fila de auditoría que no entró. Se deja constancia en el log de
                // la aplicación y se devuelve igual la respuesta. La causa habitual es que falte
                // ejecutar un guion que amplía CK_CONSULTA_funcion con las funciones nuevas.
                _db.ChangeTracker.Clear();
                _log.LogError(ex, "No se pudo registrar la consulta en CONSULTA_ASISTENTE (función {Funcion}). " +
                                  "Si el error menciona CK_CONSULTA_funcion, falta ejecutar database/19_catalogo_asistente_ampliado.sql.", funcion);
            }
        }

        JsonElement? datosJson = datos == null ? null : JsonSerializer.SerializeToElement(datos);
        return new RespuestaAsistente(respuesta, funcion, datosJson, interprete.Nombre, exitosa, (int)reloj.ElapsedMilliseconds);
    }
}
