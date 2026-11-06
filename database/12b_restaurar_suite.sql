/* =====================================================================
   Hotel Reservas - Script 12b
   Restaurar el tipo Suite despues de haberlo eliminado con el guion 12.
   Se puede ejecutar varias veces.

   Vuelve a crear el tipo con capacidad 2, su tarifa de 350.000, sus cinco
   habitaciones (105, 205, 305, 405 y 505) y su galeria. Las reservas que
   se hayan borrado NO se recuperan: esto restaura el catalogo, no la
   historia.
   ===================================================================== */
USE HotelReservas;
GO

IF NOT EXISTS (SELECT 1 FROM TIPO_HABITACION WHERE nombre = N'Suite')
    INSERT INTO TIPO_HABITACION (nombre, capacidad, descripcion, comodidades)
    VALUES (N'Suite', 2,
            N'Cama king, sala de estar y balcón con vista al mar. La categoría más alta, para dos huéspedes.',
            N'Cama king;Sala independiente;Balcón con vista;Baño privado con ducha;Wifi sin costo;Televisor;Aire acondicionado;Minibar;Caja de seguridad;Servicio a la habitación');
GO

INSERT INTO TARIFA (id_tipo, valor_noche, vigencia_desde, vigencia_hasta)
SELECT t.id_tipo, 350000.00, '2026-01-01', NULL
  FROM TIPO_HABITACION t
 WHERE t.nombre = N'Suite'
   AND NOT EXISTS (SELECT 1 FROM TARIFA r WHERE r.id_tipo = t.id_tipo AND r.vigencia_hasta IS NULL);
GO

DECLARE @suites TABLE (numero NVARCHAR(10), piso INT);
INSERT INTO @suites VALUES (N'105',1), (N'205',2), (N'305',3), (N'405',4), (N'505',5);

INSERT INTO HABITACION (numero, piso, id_tipo, estado)
SELECT s.numero, s.piso, t.id_tipo, 'DISPONIBLE'
  FROM @suites s
 CROSS JOIN TIPO_HABITACION t
 WHERE t.nombre = N'Suite'
   AND NOT EXISTS (SELECT 1 FROM HABITACION h WHERE h.numero = s.numero);
GO

DECLARE @fotos TABLE (ruta NVARCHAR(200), descripcion NVARCHAR(120), orden INT);
INSERT INTO @fotos VALUES
 (N'img/habitaciones/suite-foto.jpg',   N'Cama king, sala y balcón con vista al mar', 1),
 (N'img/habitaciones/suite-2.svg',      N'Sala independiente', 2),
 (N'img/habitaciones/suite-3.svg',      N'Baño privado con ducha', 3),
 (N'img/habitaciones/plano-suite.svg',  N'Plano isométrico de la habitación', 9);

INSERT INTO FOTO_HABITACION (id_tipo, ruta, descripcion, orden)
SELECT t.id_tipo, f.ruta, f.descripcion, f.orden
  FROM @fotos f
 CROSS JOIN TIPO_HABITACION t
 WHERE t.nombre = N'Suite'
   AND NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = f.ruta);
GO

UPDATE t SET t.imagen = N'img/habitaciones/suite-foto.jpg'
  FROM TIPO_HABITACION t
 WHERE t.nombre = N'Suite' AND (t.imagen IS NULL OR t.imagen = N'');
GO

SELECT t.nombre, t.capacidad, t.imagen AS portada,
       (SELECT COUNT(*) FROM HABITACION h WHERE h.id_tipo = t.id_tipo) AS habitaciones
  FROM TIPO_HABITACION t
 ORDER BY t.capacidad, t.nombre;
GO

PRINT 'Script 12b aplicado: el tipo Suite quedo restaurado en el catalogo.';
GO
