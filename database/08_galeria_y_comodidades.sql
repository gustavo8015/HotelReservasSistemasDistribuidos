/* =====================================================================
   Hotel Reservas - Script 08
   Ficha de la habitacion: galeria de fotos y comodidades por tipo.
   Requiere 01, 03, 05, 06 y 07. Se puede ejecutar varias veces.
   ===================================================================== */
USE HotelReservas;
GO

/* ---------- 1. Comodidades del tipo de habitacion ----------
   Lista separada por punto y coma. La ficha publica la muestra como
   viñetas y el administrador la edita desde la pestaña Fotos. */
IF COL_LENGTH('TIPO_HABITACION', 'comodidades') IS NULL
    ALTER TABLE TIPO_HABITACION ADD comodidades NVARCHAR(400) NULL;
GO

UPDATE TIPO_HABITACION SET comodidades = N'Cama sencilla;Baño privado con ducha;Escritorio de trabajo;Wifi sin costo;Televisor;Aire acondicionado;Caja de seguridad'
 WHERE nombre = N'Sencilla' AND comodidades IS NULL;
UPDATE TIPO_HABITACION SET comodidades = N'Cama doble o dos sencillas;Baño privado con ducha;Escritorio de trabajo;Wifi sin costo;Televisor;Aire acondicionado;Minibar;Caja de seguridad'
 WHERE nombre = N'Doble' AND comodidades IS NULL;
UPDATE TIPO_HABITACION SET comodidades = N'Cama king;Sala independiente;Balcón con vista;Baño privado con ducha;Wifi sin costo;Televisor;Aire acondicionado;Minibar;Caja de seguridad;Servicio a la habitación'
 WHERE nombre = N'Suite' AND comodidades IS NULL;
GO

/* ---------- 2. Galeria de fotos ----------
   Una fila por foto. ruta admite una ruta dentro de la aplicacion web
   (img/habitaciones/doble.svg) o una direccion http completa. */
IF OBJECT_ID('FOTO_HABITACION') IS NULL
BEGIN
    CREATE TABLE FOTO_HABITACION (
        id_foto     INT IDENTITY(1,1) NOT NULL,
        id_tipo     INT           NOT NULL,
        ruta        NVARCHAR(200) NOT NULL,
        descripcion NVARCHAR(120) NULL,
        orden       INT           NOT NULL CONSTRAINT DF_FOTO_orden DEFAULT (1),
        CONSTRAINT PK_FOTO_HABITACION PRIMARY KEY (id_foto),
        CONSTRAINT FK_FOTO_TIPO FOREIGN KEY (id_tipo) REFERENCES TIPO_HABITACION (id_tipo),
        CONSTRAINT UQ_FOTO_tipo_ruta UNIQUE (id_tipo, ruta)
    );
    CREATE INDEX IX_FOTO_tipo_orden ON FOTO_HABITACION (id_tipo, orden);
END
GO

/* ---------- 3. Fotos que vienen con el sistema ----------
   Tres vistas por tipo. Para reemplazarlas por fotografias propias basta
   con dejar los archivos en HotelReservas.Web/wwwroot/img/habitaciones y
   registrarlos desde Administracion, pestaña Fotos. */
DECLARE @fotos TABLE (tipo NVARCHAR(50), ruta NVARCHAR(200), descripcion NVARCHAR(120), orden INT);
INSERT INTO @fotos VALUES
 (N'Sencilla', N'img/habitaciones/sencilla.svg',   N'Vista general de la habitación', 1),
 (N'Sencilla', N'img/habitaciones/sencilla-2.svg', N'Zona de trabajo junto a la ventana', 2),
 (N'Sencilla', N'img/habitaciones/sencilla-3.svg', N'Baño privado', 3),
 (N'Doble',    N'img/habitaciones/doble.svg',      N'Vista general de la habitación', 1),
 (N'Doble',    N'img/habitaciones/doble-2.svg',    N'Zona de trabajo y descanso', 2),
 (N'Doble',    N'img/habitaciones/doble-3.svg',    N'Baño privado con ducha', 3),
 (N'Suite',    N'img/habitaciones/suite.svg',      N'Dormitorio principal', 1),
 (N'Suite',    N'img/habitaciones/suite-2.svg',    N'Sala independiente', 2),
 (N'Suite',    N'img/habitaciones/suite-3.svg',    N'Baño privado con ducha', 3),
 (N'Sencilla', N'img/habitaciones/plano-sencilla.svg', N'Plano isométrico de la habitación', 9),
 (N'Doble',    N'img/habitaciones/plano-doble.svg',    N'Plano isométrico de la habitación', 9),
 (N'Suite',    N'img/habitaciones/plano-suite.svg',    N'Plano isométrico de la habitación', 9);

INSERT INTO FOTO_HABITACION (id_tipo, ruta, descripcion, orden)
SELECT t.id_tipo, f.ruta, f.descripcion, f.orden
  FROM @fotos f
  JOIN TIPO_HABITACION t ON t.nombre = f.tipo
 WHERE NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = f.ruta);
GO

/* La foto de portada del catalogo es la primera de la galeria */
UPDATE t SET t.imagen = f.ruta
  FROM TIPO_HABITACION t
  CROSS APPLY (SELECT TOP 1 ruta FROM FOTO_HABITACION WHERE id_tipo = t.id_tipo ORDER BY orden, id_foto) f
 WHERE t.imagen IS NULL OR t.imagen = N'';
GO

/* ---------- 4. Verificacion ---------- */
SELECT t.nombre, t.capacidad, t.imagen, COUNT(f.id_foto) AS fotos, t.comodidades
  FROM TIPO_HABITACION t LEFT JOIN FOTO_HABITACION f ON f.id_tipo = t.id_tipo
 GROUP BY t.nombre, t.capacidad, t.imagen, t.comodidades
 ORDER BY t.nombre;
GO

PRINT 'Script 08 aplicado: galeria de fotos y comodidades por tipo de habitacion.';
GO
