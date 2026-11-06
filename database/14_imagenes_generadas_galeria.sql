/* =====================================================================
   Hotel Reservas - Script 14
   Las ilustraciones de la galeria se reemplazan por imagenes generadas.
   Requiere 01, 03, 05, 06, 07, 08, 09, 10, 11, 12 y 13.
   Se puede ejecutar varias veces.

   Salen de la galeria los dibujos vectoriales del bano, de las dos camas
   de la Doble, de la distribucion de la Cuadruple y del plano de la
   Cuadruple. Entran en su lugar tres imagenes nuevas:

     bano.jpg              bano privado con ducha, compartido por los tres
                           tipos porque el hotel tiene un unico modelo
     doble-camas.jpg       las dos camas sencillas de la Doble
     plano-cuadruple.jpg   plano en volumen de la Cuadruple

   Los planos de la Sencilla y de la Doble siguen siendo vectoriales: no
   hay todavia una imagen equivalente para esos dos tipos.

   Los archivos .svg que salen de la galeria se quedan en
   HotelReservas.Web/wwwroot/img/habitaciones. No estorban y permiten
   volver atras si hace falta.
   ===================================================================== */
USE HotelReservas;
GO

/* ---------- 1. Fuera las ilustraciones que se reemplazan ---------- */
DELETE FROM FOTO_HABITACION
 WHERE ruta IN (N'img/habitaciones/sencilla-3.svg',
                N'img/habitaciones/doble-3.svg',
                N'img/habitaciones/cuadruple-3.svg',
                N'img/habitaciones/doble-4.svg',
                N'img/habitaciones/cuadruple.svg',
                N'img/habitaciones/plano-cuadruple.svg');
GO

/* ---------- 2. Entran las imagenes nuevas ---------- */
DECLARE @fotos TABLE (tipo NVARCHAR(50), ruta NVARCHAR(200), descripcion NVARCHAR(120), orden INT);
INSERT INTO @fotos VALUES
 (N'Sencilla',  N'img/habitaciones/bano.jpg',             N'Baño privado con ducha', 4),
 (N'Doble',     N'img/habitaciones/doble-camas.jpg',      N'Las dos camas sencillas', 2),
 (N'Doble',     N'img/habitaciones/bano.jpg',             N'Baño privado con ducha', 4),
 (N'Cuádruple', N'img/habitaciones/bano.jpg',             N'Baño privado con ducha', 4),
 (N'Cuádruple', N'img/habitaciones/plano-cuadruple.jpg',  N'Plano en volumen de la habitación', 9);

INSERT INTO FOTO_HABITACION (id_tipo, ruta, descripcion, orden)
SELECT t.id_tipo, f.ruta, f.descripcion, f.orden
  FROM @fotos f
  JOIN TIPO_HABITACION t ON t.nombre = f.tipo
 WHERE NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = f.ruta);
GO

/* ---------- 3. Portada ----------
   Solo se recalcula si quedo vacia o si apunta a una foto que ya no esta. */
UPDATE t SET t.imagen = f.ruta
  FROM TIPO_HABITACION t
 CROSS APPLY (SELECT TOP 1 ruta FROM FOTO_HABITACION WHERE id_tipo = t.id_tipo ORDER BY orden, id_foto) f
 WHERE t.imagen IS NULL OR t.imagen = N''
    OR NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = t.imagen);
GO

/* ---------- 4. Verificacion ----------
   La galeria debe quedar asi:
     Sencilla   foto, bano, plano vectorial
     Doble      foto, las dos camas, bano, plano vectorial
     Cuadruple  foto, bano, plano en volumen
   La ficha reconoce el plano por el prefijo "plano-" de la ruta, de modo
   que el cambio de .svg a .jpg no la afecta. */
SELECT t.nombre, f.orden, f.ruta, f.descripcion
  FROM FOTO_HABITACION f JOIN TIPO_HABITACION t ON t.id_tipo = f.id_tipo
 ORDER BY t.capacidad, t.nombre, f.orden, f.id_foto;
GO

PRINT 'Script 14 aplicado: galeria con las imagenes generadas.';
GO
