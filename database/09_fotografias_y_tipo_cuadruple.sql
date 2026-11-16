/* =====================================================================
   Hotel Reservas - Script 09
   Fotografias reales en el catalogo y tipo de habitacion Cuadruple.
   Requiere 01, 03, 05, 06, 07 y 08. Se puede ejecutar varias veces.

   Que hace:
     1. Crea el tipo Cuadruple (cuatro camas sencillas, hasta 4 personas)
        con su tarifa y cinco habitaciones, una por piso.
     2. Reemplaza la vista general ilustrada de Sencilla, Doble y Cuadruple
        por la fotografia correspondiente, que queda como portada.
     3. Agrega a la galeria de Doble la vista de las dos camas sencillas
        bajo un mismo cabecero, y la galeria completa de Cuadruple.

   Los archivos de imagen deben existir en
   HotelReservas.Web/wwwroot/img/habitaciones antes de abrir el portal.
   ===================================================================== */
USE HotelReservas;
GO

/* ---------- 1. Tipo Cuadruple ---------- */
IF NOT EXISTS (SELECT 1 FROM TIPO_HABITACION WHERE nombre = N'Cuádruple')
    INSERT INTO TIPO_HABITACION (nombre, capacidad, descripcion)
    VALUES (N'Cuádruple', 4, N'Cuatro camas sencillas, baño privado, televisor y aire acondicionado. Pensada para familias y grupos de trabajo.');
GO

UPDATE TIPO_HABITACION
   SET comodidades = N'Cuatro camas sencillas;Baño privado con ducha;Escritorio de trabajo;Wifi sin costo;Televisor de 40 pulgadas;Aire acondicionado;Caja de seguridad;Servicio de aseo diario'
 WHERE nombre = N'Cuádruple' AND (comodidades IS NULL OR comodidades = N'');
GO

/* Tarifa vigente del tipo nuevo. Solo se crea si no tiene ninguna abierta. */
INSERT INTO TARIFA (id_tipo, valor_noche, vigencia_desde, vigencia_hasta)
SELECT t.id_tipo, 260000.00, '2026-01-01', NULL
  FROM TIPO_HABITACION t
 WHERE t.nombre = N'Cuádruple'
   AND NOT EXISTS (SELECT 1 FROM TARIFA r WHERE r.id_tipo = t.id_tipo);
GO

/* Cinco habitaciones cuadruples, la numero 6 de cada piso. */
DECLARE @cuadruples TABLE (numero NVARCHAR(10), piso INT);
INSERT INTO @cuadruples VALUES (N'106',1), (N'206',2), (N'306',3), (N'406',4), (N'506',5);

INSERT INTO HABITACION (numero, piso, id_tipo, estado)
SELECT c.numero, c.piso, t.id_tipo, 'DISPONIBLE'
  FROM @cuadruples c
 CROSS JOIN TIPO_HABITACION t
 WHERE t.nombre = N'Cuádruple'
   AND NOT EXISTS (SELECT 1 FROM HABITACION h WHERE h.numero = c.numero);
GO

/* ---------- 2. La vista general pasa a ser la fotografia ----------
   Se retira de la galeria la ilustracion que hacia de vista general en los
   tipos que ya tienen fotografia. Las demas ilustraciones se conservan:
   muestran la zona de trabajo, el baño y el plano, que las fotografias no
   cubren. */
DELETE FROM FOTO_HABITACION
 WHERE ruta IN (N'img/habitaciones/sencilla.svg', N'img/habitaciones/doble.svg');
GO

/* ---------- 3. Galeria ---------- */
DECLARE @fotos TABLE (tipo NVARCHAR(50), ruta NVARCHAR(200), descripcion NVARCHAR(120), orden INT);
INSERT INTO @fotos VALUES
 (N'Sencilla',  N'img/habitaciones/sencilla-foto.jpg',   N'La habitación vista desde la puerta', 1),
 (N'Doble',     N'img/habitaciones/doble-foto.jpg',      N'Las dos camas y el televisor', 1),
 (N'Doble',     N'img/habitaciones/doble-4.svg',         N'Dos camas sencillas con un mismo cabecero', 2),
 (N'Cuádruple', N'img/habitaciones/cuadruple-foto.jpg',  N'Las cuatro camas sencillas', 1),
 (N'Cuádruple', N'img/habitaciones/cuadruple.svg',       N'Distribución de las camas en dos parejas', 2),
 (N'Cuádruple', N'img/habitaciones/cuadruple-2.svg',     N'Zona de trabajo y televisor', 3),
 (N'Cuádruple', N'img/habitaciones/cuadruple-3.svg',     N'Baño privado con ducha', 4),
 (N'Cuádruple', N'img/habitaciones/plano-cuadruple.svg', N'Plano isométrico de la habitación', 9);

INSERT INTO FOTO_HABITACION (id_tipo, ruta, descripcion, orden)
SELECT t.id_tipo, f.ruta, f.descripcion, f.orden
  FROM @fotos f
  JOIN TIPO_HABITACION t ON t.nombre = f.tipo
 WHERE NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = f.ruta);
GO

/* La zona de trabajo y el baño pasan detras de las vistas nuevas. */
UPDATE f SET f.orden = 3
  FROM FOTO_HABITACION f
 WHERE f.ruta = N'img/habitaciones/doble-2.svg';
UPDATE f SET f.orden = 4
  FROM FOTO_HABITACION f
 WHERE f.ruta = N'img/habitaciones/doble-3.svg';
GO

/* ---------- 4. Portada del catalogo ----------
   Es siempre la primera foto de la galeria del tipo. */
UPDATE t SET t.imagen = f.ruta
  FROM TIPO_HABITACION t
 CROSS APPLY (SELECT TOP 1 ruta FROM FOTO_HABITACION WHERE id_tipo = t.id_tipo ORDER BY orden, id_foto) f
 WHERE t.imagen IS NULL OR t.imagen = N''
    OR NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = t.imagen);
GO

/* ---------- 5. Verificacion ----------
   Deben aparecer cuatro tipos. Sencilla, Doble y Cuadruple con una
   fotografia .jpg como portada, y Cuadruple con capacidad 4, tarifa
   vigente y cinco habitaciones. */
SELECT t.nombre, t.capacidad, t.imagen,
       (SELECT COUNT(*) FROM FOTO_HABITACION f WHERE f.id_tipo = t.id_tipo)  AS fotos,
       (SELECT COUNT(*) FROM HABITACION h      WHERE h.id_tipo = t.id_tipo)  AS habitaciones,
       (SELECT TOP 1 valor_noche FROM TARIFA r WHERE r.id_tipo = t.id_tipo
         AND r.vigencia_desde <= CAST(GETDATE() AS DATE)
         AND (r.vigencia_hasta IS NULL OR r.vigencia_hasta >= CAST(GETDATE() AS DATE))
         ORDER BY r.vigencia_desde DESC)                                     AS tarifa_vigente
  FROM TIPO_HABITACION t
 ORDER BY t.capacidad, t.nombre;
GO

SELECT t.nombre, f.orden, f.ruta, f.descripcion
  FROM FOTO_HABITACION f JOIN TIPO_HABITACION t ON t.id_tipo = f.id_tipo
 ORDER BY t.nombre, f.orden, f.id_foto;
GO

PRINT 'Script 09 aplicado: fotografias reales en el catalogo y tipo Cuadruple.';
GO
