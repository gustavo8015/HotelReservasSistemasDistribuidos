/* =====================================================================
   Hotel Reservas - Script 12
   Eliminar el tipo de habitacion Suite.
   Requiere 01, 03, 05, 06, 07, 08, 09, 10 y 11.
   Se puede ejecutar varias veces: si la Suite ya no existe, no hace nada.

   El catalogo queda en tres tipos:
     1 persona       -> Sencilla
     2 personas      -> Doble
     3 o 4 personas  -> Cuadruple

   IMPORTANTE. La Suite tiene cinco habitaciones (105, 205, 305, 405 y 505).
   Si alguna de ellas tiene reservas, no se puede borrar el tipo sin borrar
   antes esas reservas, y esas reservas son la evidencia de los casos de uso
   CU-04, CU-05 y CU-06. Por eso el guion no las toca por su cuenta.

   Decida usted con el interruptor de la linea siguiente:
     0 = no borrar reservas. Si hay reservas de Suite, el guion no elimina
         el tipo: solo lo deja fuera de servicio (tarifa cerrada y
         habitaciones en MANTENIMIENTO) y le avisa.
     1 = borrar tambien las reservas de Suite y eliminar el tipo por
         completo. Esto no se puede deshacer.

   Para volver a crear la Suite esta el guion 12b_restaurar_suite.sql.
   ===================================================================== */
USE HotelReservas;
GO

DECLARE @borrar_reservas BIT = 0;   /* <<<<<< cambie a 1 si acepta perder las reservas de Suite */

DECLARE @id_tipo INT = (SELECT id_tipo FROM TIPO_HABITACION WHERE nombre = N'Suite');

IF @id_tipo IS NULL
BEGIN
    PRINT 'El tipo Suite no existe. No hay nada que eliminar.';
    RETURN;
END

DECLARE @reservas INT = (
    SELECT COUNT(*)
      FROM RESERVA r
      JOIN HABITACION h ON h.id_habitacion = r.id_habitacion
     WHERE h.id_tipo = @id_tipo);

PRINT CONCAT('Reservas asociadas a habitaciones Suite: ', @reservas);

IF @reservas > 0 AND @borrar_reservas = 0
BEGIN
    /* No se elimina el tipo: se deja fuera de servicio y se conserva la historia. */
    UPDATE TARIFA
       SET vigencia_hasta = DATEADD(DAY, -1, CAST(GETDATE() AS DATE))
     WHERE id_tipo = @id_tipo
       AND (vigencia_hasta IS NULL OR vigencia_hasta >= CAST(GETDATE() AS DATE));

    UPDATE HABITACION SET estado = 'MANTENIMIENTO'
     WHERE id_tipo = @id_tipo AND estado <> 'MANTENIMIENTO';

    PRINT 'La Suite tiene reservas, asi que NO se elimino.';
    PRINT 'Quedo fuera de servicio: tarifa cerrada y habitaciones en MANTENIMIENTO.';
    PRINT 'Ya no se puede reservar y sale del catalogo por falta de tarifa vigente.';
    PRINT 'Si de todos modos quiere borrarla, ponga @borrar_reservas = 1 y vuelva a ejecutar.';
    RETURN;
END

/* ---------- Eliminacion completa ----------
   El orden respeta las claves foraneas: primero lo que apunta a la
   habitacion, luego lo que apunta al tipo, y al final el tipo. */
IF @reservas > 0
BEGIN
    DELETE b
      FROM BITACORA b
      JOIN RESERVA r      ON r.id_reserva = b.id_reserva
      JOIN HABITACION h   ON h.id_habitacion = r.id_habitacion
     WHERE h.id_tipo = @id_tipo;

    DELETE p
      FROM PAGO p
      JOIN RESERVA r      ON r.id_reserva = p.id_reserva
      JOIN HABITACION h   ON h.id_habitacion = r.id_habitacion
     WHERE h.id_tipo = @id_tipo;

    DELETE c
      FROM COMPROBANTE c
      JOIN RESERVA r      ON r.id_reserva = c.id_reserva
      JOIN HABITACION h   ON h.id_habitacion = r.id_habitacion
     WHERE h.id_tipo = @id_tipo;

    DELETE r
      FROM RESERVA r
      JOIN HABITACION h   ON h.id_habitacion = r.id_habitacion
     WHERE h.id_tipo = @id_tipo;

    PRINT CONCAT('Reservas de Suite eliminadas: ', @reservas);
END

DELETE FROM FOTO_HABITACION WHERE id_tipo = @id_tipo;
DELETE FROM HABITACION      WHERE id_tipo = @id_tipo;
DELETE FROM TARIFA          WHERE id_tipo = @id_tipo;
DELETE FROM TIPO_HABITACION WHERE id_tipo = @id_tipo;

PRINT 'Tipo Suite eliminado: fotos, habitaciones, tarifas y el tipo.';
GO

/* ---------- Verificacion ----------
   Deben quedar tres tipos: Sencilla 1, Doble 2 y Cuadruple 4. */
SELECT t.nombre, t.capacidad, t.imagen AS portada,
       (SELECT COUNT(*) FROM HABITACION h WHERE h.id_tipo = t.id_tipo) AS habitaciones,
       (SELECT TOP 1 valor_noche FROM TARIFA r WHERE r.id_tipo = t.id_tipo
         AND r.vigencia_desde <= CAST(GETDATE() AS DATE)
         AND (r.vigencia_hasta IS NULL OR r.vigencia_hasta >= CAST(GETDATE() AS DATE))
         ORDER BY r.vigencia_desde DESC)                               AS tarifa_vigente
  FROM TIPO_HABITACION t
 ORDER BY t.capacidad, t.nombre;
GO

PRINT 'Script 12 terminado.';
GO
