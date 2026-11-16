/* =====================================================================
   Hotel Reservas - Script 11
   La oferta queda en cuatro tramos claros de ocupacion.
   Requiere 01, 03, 05, 06, 07, 08, 09 y 10. Se puede ejecutar varias veces.

   Como queda el catalogo:
     1 persona        -> Sencilla
     2 personas       -> Doble, y la Suite como opcion superior
     3 o 4 personas   -> Cuadruple
     Suite            -> opcion de mayor categoria para dos huespedes

   El unico cambio de datos es la capacidad de la Suite, que pasa de 4 a 2.
   La Suite es una habitacion de cama king con sala y balcon: su fotografia
   y su descripcion siempre fueron de dos huespedes, y con capacidad 4
   competia con la Cuadruple en el mismo tramo y duplicaba la oferta.

   Para deshacerlo basta con:
     UPDATE TIPO_HABITACION SET capacidad = 4 WHERE nombre = N'Suite';
   ===================================================================== */
USE HotelReservas;
GO

/* ---------- 1. Aviso previo ----------
   Si hay reservas activas de Suite con mas de dos huespedes, se listan aqui
   para que queden a la vista. Bajar la capacidad no las altera: siguen
   vigentes tal como se confirmaron. Solo afecta a las reservas nuevas. */
SELECT r.codigo, r.numero_personas, r.fecha_entrada, r.fecha_salida, r.estado, h.numero AS habitacion
  FROM RESERVA r
  JOIN HABITACION h       ON h.id_habitacion = r.id_habitacion
  JOIN TIPO_HABITACION t  ON t.id_tipo = h.id_tipo
 WHERE t.nombre = N'Suite'
   AND r.numero_personas > 2
   AND r.estado IN ('CONFIRMADA', 'CHECKIN');
GO

/* ---------- 2. La Suite pasa a ser de dos huespedes ---------- */
UPDATE TIPO_HABITACION
   SET capacidad = 2
 WHERE nombre = N'Suite' AND capacidad <> 2;
GO

/* ---------- 3. Descripciones acordes con el tramo que cubre cada tipo ---------- */
UPDATE TIPO_HABITACION SET descripcion = N'Una cama sencilla, baño privado, escritorio y wifi. Para un huésped.'
 WHERE nombre = N'Sencilla';
UPDATE TIPO_HABITACION SET descripcion = N'Cama doble o dos sencillas, televisor, baño privado y wifi. Para dos huéspedes.'
 WHERE nombre = N'Doble';
UPDATE TIPO_HABITACION SET descripcion = N'Cuatro camas sencillas, baño privado, televisor y aire acondicionado. Para tres o cuatro huéspedes.'
 WHERE nombre = N'Cuádruple';
UPDATE TIPO_HABITACION SET descripcion = N'Cama king, sala de estar y balcón con vista al mar. La categoría más alta, para dos huéspedes.'
 WHERE nombre = N'Suite';
GO

/* ---------- 4. Verificacion ----------
   Deben quedar cuatro tipos: Sencilla 1, Doble 2, Suite 2 y Cuadruple 4,
   los cuatro con una fotografia .jpg como portada. */
SELECT t.nombre, t.capacidad, t.imagen AS portada,
       (SELECT COUNT(*) FROM HABITACION h WHERE h.id_tipo = t.id_tipo) AS habitaciones,
       (SELECT TOP 1 valor_noche FROM TARIFA r WHERE r.id_tipo = t.id_tipo
         AND r.vigencia_desde <= CAST(GETDATE() AS DATE)
         AND (r.vigencia_hasta IS NULL OR r.vigencia_hasta >= CAST(GETDATE() AS DATE))
         ORDER BY r.vigencia_desde DESC)                                AS tarifa_vigente,
       t.descripcion
  FROM TIPO_HABITACION t
 ORDER BY t.capacidad, t.nombre;
GO

PRINT 'Script 11 aplicado: 1, 2, 3 o 4 personas, y la Suite como categoria superior de dos huespedes.';
GO
