/* =====================================================================
   Hotel Reservas - Script 10
   Fotografia de la Suite.
   Requiere 01, 03, 05, 06, 07, 08 y 09. Se puede ejecutar varias veces.

   Deja img/habitaciones/suite-foto.jpg como primera foto y portada del
   tipo Suite, y retira de la galeria la ilustracion que hacia de vista
   general. Las demas ilustraciones de la Suite se conservan: muestran la
   sala, el bano y el plano, que la fotografia no cubre.
   ===================================================================== */
USE HotelReservas;
GO

/* ---------- 1. La vista general deja de ser el dibujo ---------- */
DELETE FROM FOTO_HABITACION WHERE ruta = N'img/habitaciones/suite.svg';
GO

/* ---------- 2. La fotografia entra de primera en la galeria ---------- */
INSERT INTO FOTO_HABITACION (id_tipo, ruta, descripcion, orden)
SELECT t.id_tipo, N'img/habitaciones/suite-foto.jpg', N'Cama king, sala y balcón con vista al mar', 1
  FROM TIPO_HABITACION t
 WHERE t.nombre = N'Suite'
   AND NOT EXISTS (SELECT 1 FROM FOTO_HABITACION f
                    WHERE f.id_tipo = t.id_tipo AND f.ruta = N'img/habitaciones/suite-foto.jpg');
GO

/* ---------- 3. Portada del catalogo ----------
   Solo se toca si quedo vacia o si apunta a una foto que ya no esta en la
   galeria, para no pisar la portada que el administrador haya elegido. */
UPDATE t SET t.imagen = f.ruta
  FROM TIPO_HABITACION t
 CROSS APPLY (SELECT TOP 1 ruta FROM FOTO_HABITACION WHERE id_tipo = t.id_tipo ORDER BY orden, id_foto) f
 WHERE t.imagen IS NULL OR t.imagen = N''
    OR NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = t.imagen);
GO

/* ---------- 4. Verificacion ----------
   Los cuatro tipos deben quedar con una fotografia .jpg como portada. */
SELECT t.nombre, t.capacidad, t.imagen AS portada,
       (SELECT COUNT(*) FROM FOTO_HABITACION f WHERE f.id_tipo = t.id_tipo) AS fotos
  FROM TIPO_HABITACION t
 ORDER BY t.capacidad, t.nombre;
GO

PRINT 'Script 10 aplicado: fotografia de la Suite.';
GO
