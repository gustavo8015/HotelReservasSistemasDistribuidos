/* =====================================================================
   Hotel Reservas - Script 17
   Tipo de habitacion Triple, para tres huespedes.
   Requiere los guiones anteriores. Se puede ejecutar varias veces.

   Como queda el catalogo:
     1 persona    -> Sencilla
     2 personas   -> Doble
     3 personas   -> Triple
     4 personas   -> Cuadruple

   Hasta ahora la Cuadruple cubria tres y cuatro huespedes. Con la Triple
   cada cantidad tiene su habitacion y no se le vende a un grupo de tres
   una habitacion de cuatro camas.

   Las comodidades salen de lo que muestra la fotografia, igual que en los
   demas tipos: tres camas, el aire acondicionado de la pared, las
   lamparas colgantes y el telefono de la mesa de noche. Se agregan el
   bano privado, que tiene su propia vista en la galeria, y el wifi, que
   ninguna fotografia puede mostrar.
   ===================================================================== */
USE HotelReservas;
GO

/* ---------- 1. El tipo ---------- */
IF NOT EXISTS (SELECT 1 FROM TIPO_HABITACION WHERE nombre = N'Triple')
    INSERT INTO TIPO_HABITACION (nombre, capacidad, descripcion)
    VALUES (N'Triple', 3, N'Tres camas sencillas, aire acondicionado y baño privado. Para tres huéspedes.');
GO

UPDATE TIPO_HABITACION
   SET comodidades = N'Tres camas sencillas;Aire acondicionado;Lámparas colgantes de lectura;Mesa de noche con teléfono;Baño privado con ducha;Wifi sin costo'
 WHERE nombre = N'Triple';
GO

/* La Cuadruple deja de cubrir el tramo de tres. */
UPDATE TIPO_HABITACION
   SET descripcion = N'Cuatro camas sencillas, aire acondicionado y baño privado. Para cuatro huéspedes.'
 WHERE nombre = N'Cuádruple';
GO

/* ---------- 2. Tarifa ----------
   220.000, entre la Doble de 180.000 y la Cuadruple de 260.000. */
INSERT INTO TARIFA (id_tipo, valor_noche, vigencia_desde, vigencia_hasta)
SELECT t.id_tipo, 220000.00, '2026-01-01', NULL
  FROM TIPO_HABITACION t
 WHERE t.nombre = N'Triple'
   AND NOT EXISTS (SELECT 1 FROM TARIFA r WHERE r.id_tipo = t.id_tipo);
GO

/* ---------- 3. Habitaciones: la numero 7 de cada piso ---------- */
DECLARE @triples TABLE (numero NVARCHAR(10), piso INT);
INSERT INTO @triples VALUES (N'107',1), (N'207',2), (N'307',3), (N'407',4), (N'507',5);

INSERT INTO HABITACION (numero, piso, id_tipo, estado)
SELECT x.numero, x.piso, t.id_tipo, 'DISPONIBLE'
  FROM @triples x
 CROSS JOIN TIPO_HABITACION t
 WHERE t.nombre = N'Triple'
   AND NOT EXISTS (SELECT 1 FROM HABITACION h WHERE h.numero = x.numero);
GO

/* ---------- 4. Galeria: su fotografia y el mismo bano que los demas ---------- */
DECLARE @fotos TABLE (ruta NVARCHAR(200), descripcion NVARCHAR(120), orden INT);
INSERT INTO @fotos VALUES
 (N'img/habitaciones/triple-foto.jpg', N'Las tres camas sencillas', 1),
 (N'img/habitaciones/bano.jpg',        N'Baño privado con ducha',   4);

INSERT INTO FOTO_HABITACION (id_tipo, ruta, descripcion, orden)
SELECT t.id_tipo, f.ruta, f.descripcion, f.orden
  FROM @fotos f
 CROSS JOIN TIPO_HABITACION t
 WHERE t.nombre = N'Triple'
   AND NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = f.ruta);
GO

/* ---------- 5. Portada ---------- */
UPDATE t SET t.imagen = f.ruta
  FROM TIPO_HABITACION t
 CROSS APPLY (SELECT TOP 1 ruta FROM FOTO_HABITACION WHERE id_tipo = t.id_tipo ORDER BY orden, id_foto) f
 WHERE t.imagen IS NULL OR t.imagen = N''
    OR NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = t.imagen);
GO

/* ---------- 6. Verificacion ----------
   Cuatro tipos: Sencilla 1, Doble 2, Triple 3 y Cuadruple 4, los cuatro
   con fotografia de portada, tarifa vigente y cinco o mas habitaciones. */
SELECT t.nombre, t.capacidad, t.imagen AS portada,
       (SELECT COUNT(*) FROM HABITACION h WHERE h.id_tipo = t.id_tipo) AS habitaciones,
       (SELECT TOP 1 valor_noche FROM TARIFA r WHERE r.id_tipo = t.id_tipo
         AND r.vigencia_desde <= CAST(GETDATE() AS DATE)
         AND (r.vigencia_hasta IS NULL OR r.vigencia_hasta >= CAST(GETDATE() AS DATE))
         ORDER BY r.vigencia_desde DESC)                               AS tarifa_vigente,
       t.comodidades
  FROM TIPO_HABITACION t
 ORDER BY t.capacidad;
GO

PRINT 'Script 17 aplicado: tipo Triple para tres huespedes.';
GO
