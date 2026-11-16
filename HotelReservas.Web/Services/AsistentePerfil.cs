namespace HotelReservas.Web.Services;

/// <summary>
/// Textos del asistente según el perfil de quien pregunta. Viven aquí y no en cada pantalla
/// porque la ventana flotante y la pantalla completa deben decir exactamente lo mismo: si se
/// duplicaran, tarde o temprano una de las dos ofrecería una consulta que la otra no tiene.
///
/// Estos textos son solo presentación. El corte real de lo que el asistente puede responder lo
/// hace la API al recortar el catálogo de funciones por el rol del token (CatalogoFunciones.ParaRol).
/// </summary>
public static class AsistentePerfil
{
    public static string Etiqueta(bool personal, bool conSesion)
        => personal ? "Operación del hotel" : conSesion ? "Sus reservas" : "Visitante";

    public static string Subtitulo(bool personal, bool conSesion) => personal
        ? "Consultas en lenguaje natural sobre la operación: disponibilidad, tarifas, llegadas, reservas sin pagar y cancelaciones."
        : conSesion
            ? "Pregunte por habitaciones, precios, sus propias reservas y las condiciones de pago y cancelación."
            : "Pregunte por habitaciones, precios y las condiciones del hotel. Para consultar sus reservas, inicie sesión.";

    public static string Saludo(bool personal, bool conSesion) => personal
        ? "Hola. Puedo consultar disponibilidad, tarifas, el estado de una reserva, las llegadas del día, las reservas sin pagar y las cancelaciones. No creo ni modifico reservas."
        : conSesion
            ? "Hola. Puedo decirle qué habitaciones hay libres, cuánto cuestan, cómo van sus reservas y cuáles son las condiciones de pago y cancelación. No reservo ni cancelo: eso lo hace usted desde sus pantallas."
            : "Hola. Puedo decirle qué habitaciones hay libres, cuánto cuestan y cuáles son las condiciones del hotel. Si inicia sesión, también le muestro cómo van sus reservas.";

    public static string[] Sugerencias(bool personal, bool conSesion) => personal
        ? new[]
        {
            "¿Qué habitaciones dobles hay libres del 16 al 18 de diciembre?",
            "¿Cuántas reservas hay confirmadas?",
            "¿Cuántas llegadas tenemos mañana?",
            "¿Qué reservas están sin pagar?",
            "¿A cuáles se les vence el plazo en las próximas 6 horas?",
            "¿Cuántas cancelaciones hubo hoy?",
        }
        : conSesion
            ? new[]
            {
                "¿Qué habitaciones triples hay libres del 16 al 18 de diciembre?",
                "¿Cuánto cuesta una cuádruple por 3 noches?",
                "¿Cómo van mis reservas?",
                "¿Cuánto tiempo tengo para pagar?",
                "¿Hasta cuándo puedo cancelar?",
            }
            : new[]
            {
                "¿Qué habitaciones dobles hay libres del 16 al 18 de diciembre?",
                "¿Cuánto cuesta una sencilla por 2 noches?",
                "¿Cuáles son las condiciones de pago y cancelación?",
                "¿A qué hora es la entrada y la salida?",
            };

    public static string[] Alcance(bool personal, bool conSesion) => personal
        ? new[]
        {
            "Disponibilidad por tipo y rango de fechas",
            "Tarifa vigente y desglose de IVA",
            "Estado de una reserva por su código",
            "Llegadas programadas de un día",
            "Conteo de reservas y ocupación",
            "Reservas retenidas sin pagar y sus plazos",
            "Cancelaciones de un día y quién las hizo",
        }
        : conSesion
            ? new[]
            {
                "Disponibilidad por tipo y rango de fechas",
                "Tarifa vigente y desglose de IVA",
                "Sus propias reservas, con el plazo de pago si falta pagarlas",
                "Condiciones de pago y de cancelación",
            }
            : new[]
            {
                "Disponibilidad por tipo y rango de fechas",
                "Tarifa vigente y desglose de IVA",
                "Condiciones de pago y de cancelación",
            };
}
