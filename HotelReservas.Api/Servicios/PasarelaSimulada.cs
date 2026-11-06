namespace HotelReservas.Api.Servicios;

/// <summary>Datos que el usuario final escribe en el formulario de la tarjeta. Nunca se almacenan.</summary>
public record DatosTarjeta(string Numero, string Titular, string Vencimiento, string Cvv);

/// <summary>Respuesta de la pasarela simulada. Referencia es lo único que se guarda en PAGO.</summary>
public record ResultadoPago(bool Aprobado, string Medio, string? Autorizacion, string Referencia,
    string Franquicia, string? UltimosCuatro, string Mensaje, string? CodigoRechazo);

/// <summary>
/// Pasarela de pagos simulada (RF21). No existe ningún cobro real: la clase valida el formato de la
/// tarjeta con el algoritmo de Luhn, verifica la fecha de vencimiento y el código de seguridad, espera
/// un instante para imitar la latencia de la red financiera y devuelve un número de autorización.
///
/// Reglas de prueba para la sustentación:
///   · cualquier tarjeta válida que NO termine en 0000 se aprueba;
///   · una tarjeta terminada en 0000 se rechaza con el código 51 (fondos insuficientes);
///   · una tarjeta vencida se rechaza con el código 54;
///   · un número que no pasa Luhn o un código de seguridad incompleto se rechazan con el código 14.
///
/// Protección de datos (Ley 1581 de 2012, principio de minimización del artículo 4 literal f):
/// el número completo, el titular y el código de seguridad no se guardan en ninguna tabla; de la
/// tarjeta solo persisten la franquicia y los cuatro últimos dígitos dentro de PAGO.referencia.
/// </summary>
public class PasarelaSimulada
{
    private readonly ILogger<PasarelaSimulada> _log;
    public PasarelaSimulada(ILogger<PasarelaSimulada> log) => _log = log;

    public static readonly string[] MediosAceptados = { "EFECTIVO", "TARJETA" };

    public async Task<ResultadoPago> CobrarAsync(string medio, decimal valor, DatosTarjeta? tarjeta)
    {
        medio = (medio ?? "").Trim().ToUpperInvariant();

        if (medio == "EFECTIVO")
        {
            await Task.Delay(250);
            var recibo = "EFE-" + Random.Shared.Next(100000, 999999);
            _log.LogInformation("Pago en efectivo registrado por {Valor}. Recibo {Recibo}.", valor, recibo);
            return new ResultadoPago(true, "EFECTIVO", recibo, $"EFECTIVO {recibo}", "EFECTIVO", null,
                "Pago en efectivo registrado. Preséntese en recepción con el documento con el que reservó.", null);
        }

        if (medio != "TARJETA")
            return new ResultadoPago(false, medio, null, "", "", null, "Medio de pago no admitido: use EFECTIVO o TARJETA.", "00");

        if (tarjeta == null)
            return new ResultadoPago(false, "TARJETA", null, "", "", null, "Faltan los datos de la tarjeta.", "14");

        var numero = new string((tarjeta.Numero ?? "").Where(char.IsDigit).ToArray());
        var cvv = new string((tarjeta.Cvv ?? "").Where(char.IsDigit).ToArray());
        var franquicia = Franquicia(numero);
        var ultimos = numero.Length >= 4 ? numero[^4..] : null;

        if (string.IsNullOrWhiteSpace(tarjeta.Titular) || tarjeta.Titular.Trim().Length < 5)
            return Rechazo("14", "Escriba el nombre del titular tal como aparece en la tarjeta.", franquicia, ultimos);

        if (numero.Length < 13 || numero.Length > 19 || !Luhn(numero))
            return Rechazo("14", "El número de la tarjeta no es válido. Verifique los dígitos.", franquicia, ultimos);

        var esperadoCvv = franquicia == "AMEX" ? 4 : 3;
        if (cvv.Length != esperadoCvv)
            return Rechazo("14", $"El código de seguridad debe tener {esperadoCvv} dígitos.", franquicia, ultimos);

        if (!VencimientoValido(tarjeta.Vencimiento, out var vencida))
            return Rechazo("14", "La fecha de vencimiento debe escribirse como MM/AA.", franquicia, ultimos);
        if (vencida)
            return Rechazo("54", "La tarjeta está vencida.", franquicia, ultimos);

        await Task.Delay(900);   // latencia simulada de la red financiera

        if (numero.EndsWith("0000"))
        {
            _log.LogWarning("Pago rechazado por la pasarela simulada: {Franquicia} ****{Ultimos}.", franquicia, ultimos);
            return Rechazo("51", "La entidad financiera rechazó la transacción por fondos insuficientes.", franquicia, ultimos);
        }

        var autorizacion = "AUT-" + Random.Shared.Next(100000, 999999);
        _log.LogInformation("Pago aprobado por {Valor}. Autorización {Aut}, {Franquicia} ****{Ultimos}.", valor, autorizacion, franquicia, ultimos);
        return new ResultadoPago(true, "TARJETA", autorizacion, $"{autorizacion} {franquicia} ****{ultimos}",
            franquicia, ultimos, $"Pago aprobado. Autorización {autorizacion}.", null);
    }

    /// <summary>
    /// Reverso de una autorización cuando la reserva no se pudo confirmar después de cobrar.
    /// Es la compensación del patrón Saga: el cobro ya ocurrió y se deshace con una operación
    /// inversa, porque la pasarela y la base de datos del hotel no comparten una transacción.
    /// </summary>
    public void Reversar(string? autorizacion, string motivo)
    {
        if (string.IsNullOrEmpty(autorizacion)) return;
        _log.LogWarning("Reverso de la autorización {Aut}. Motivo: {Motivo}.", autorizacion, motivo);
    }

    private static ResultadoPago Rechazo(string codigo, string mensaje, string franquicia, string? ultimos) =>
        new(false, "TARJETA", null, "", franquicia, ultimos, mensaje, codigo);

    public static string Franquicia(string numero)
    {
        if (numero.StartsWith("4")) return "VISA";
        if (numero.StartsWith("34") || numero.StartsWith("37")) return "AMEX";
        if (numero.StartsWith("5") || numero.StartsWith("2")) return "MASTERCARD";
        if (numero.StartsWith("6")) return "DINERS";
        return "TARJETA";
    }

    /// <summary>Algoritmo de Luhn (ISO/IEC 7812-1): dígito verificador de la tarjeta.</summary>
    public static bool Luhn(string numero)
    {
        var suma = 0; var alterna = false;
        for (var i = numero.Length - 1; i >= 0; i--)
        {
            var d = numero[i] - '0';
            if (alterna) { d *= 2; if (d > 9) d -= 9; }
            suma += d; alterna = !alterna;
        }
        return suma % 10 == 0;
    }

    /// <summary>Acepta MM/AA o MM/AAAA. Devuelve false si el formato es incorrecto.</summary>
    public static bool VencimientoValido(string? texto, out bool vencida)
    {
        vencida = false;
        var partes = (texto ?? "").Split('/', '-');
        if (partes.Length != 2) return false;
        if (!int.TryParse(partes[0].Trim(), out var mes) || mes < 1 || mes > 12) return false;
        if (!int.TryParse(partes[1].Trim(), out var anio)) return false;
        if (anio < 100) anio += 2000;
        if (anio < 2000 || anio > 2100) return false;
        var ultimoDia = new DateTime(anio, mes, DateTime.DaysInMonth(anio, mes));
        vencida = ultimoDia < DateTime.Today;
        return true;
    }
}
