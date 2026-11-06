/* =====================================================================
   Hotel Reservas - Script 07
   Portal del usuario final: imagenes de las habitaciones, dos perfiles
   (ADMINISTRADOR y CLIENTE), reserva con pago simulado en efectivo o
   tarjeta y canal de envio del comprobante.
   Requiere 01, 03, 05 y 06. Se puede ejecutar varias veces sin dañar nada.
   ===================================================================== */
USE HotelReservas;
GO

/* ---------- 1. Imagen del tipo de habitacion y de la habitacion ----------
   Se guarda una ruta relativa a la aplicacion web (wwwroot) o una URL completa.
   La habitacion puede tener su propia foto; si no la tiene, hereda la del tipo. */
IF COL_LENGTH('TIPO_HABITACION', 'imagen') IS NULL
    ALTER TABLE TIPO_HABITACION ADD imagen NVARCHAR(200) NULL;
GO
IF COL_LENGTH('HABITACION', 'imagen') IS NULL
    ALTER TABLE HABITACION ADD imagen NVARCHAR(200) NULL;
GO

UPDATE TIPO_HABITACION SET imagen = N'img/habitaciones/sencilla.svg' WHERE nombre = N'Sencilla' AND imagen IS NULL;
UPDATE TIPO_HABITACION SET imagen = N'img/habitaciones/doble.svg'    WHERE nombre = N'Doble'    AND imagen IS NULL;
UPDATE TIPO_HABITACION SET imagen = N'img/habitaciones/suite.svg'    WHERE nombre = N'Suite'    AND imagen IS NULL;
GO

/* Descripcion comercial para el catalogo del portal (solo si quedo la de prueba) */
UPDATE TIPO_HABITACION SET descripcion = N'Cama sencilla, baño privado, escritorio y wifi. Ideal para viaje de trabajo.'
 WHERE nombre = N'Sencilla' AND descripcion = N'Una cama sencilla, baño privado';
UPDATE TIPO_HABITACION SET descripcion = N'Cama doble o dos sencillas, televisor, baño privado y wifi.'
 WHERE nombre = N'Doble' AND descripcion = N'Una cama doble o dos sencillas';
UPDATE TIPO_HABITACION SET descripcion = N'Sala independiente, cama king, balcón con vista y wifi.'
 WHERE nombre = N'Suite' AND descripcion = N'Sala, cama king y balcón';
GO

/* ---------- 2. Dos perfiles: ADMINISTRADOR y CLIENTE ----------
   El perfil RECEPCIONISTA desaparece. Las cuentas que lo tenian pasan a CLIENTE y
   quedan inactivas: sus reservas historicas se conservan porque RESERVA.id_usuario
   sigue apuntando a ellas (FK_RESERVA_USUARIO).

   El orden importa: primero se quita la restriccion vieja, que solo admite
   RECEPCIONISTA y ADMINISTRADOR, despues se cambian las filas y al final se vuelve
   a crear la restriccion con los dos perfiles definitivos. Si se intenta al reves,
   el motor rechaza el UPDATE con el error 547. */
IF OBJECT_ID('CK_USUARIO_rol', 'C') IS NOT NULL
    ALTER TABLE USUARIO DROP CONSTRAINT CK_USUARIO_rol;
GO

UPDATE USUARIO SET rol = N'CLIENTE', activo = 0 WHERE rol = N'RECEPCIONISTA';
GO

ALTER TABLE USUARIO ADD CONSTRAINT CK_USUARIO_rol CHECK (rol IN (N'ADMINISTRADOR', N'CLIENTE'));
GO

/* ---------- 3. Vinculo entre la cuenta del cliente y su ficha de huesped ----------
   Un cliente que crea cuenta queda unido a un unico HUESPED; asi "Mis reservas"
   sabe cuales son suyas sin exponer el resto. */
IF COL_LENGTH('USUARIO', 'id_huesped') IS NULL
    ALTER TABLE USUARIO ADD id_huesped INT NULL;
GO
IF OBJECT_ID('FK_USUARIO_HUESPED', 'F') IS NULL
    ALTER TABLE USUARIO ADD CONSTRAINT FK_USUARIO_HUESPED FOREIGN KEY (id_huesped) REFERENCES HUESPED (id_huesped);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_USUARIO_huesped')
    CREATE UNIQUE INDEX UQ_USUARIO_huesped ON USUARIO (id_huesped) WHERE id_huesped IS NOT NULL;
GO

/* ---------- 4. Usuario tecnico del portal ----------
   Las reservas hechas sin cuenta necesitan un responsable para FK_RESERVA_USUARIO.
   Queda inactivo y con un hash invalido, de modo que nadie puede iniciar sesion con el. */
IF NOT EXISTS (SELECT 1 FROM USUARIO WHERE correo = N'portal@hotel.com')
    INSERT INTO USUARIO (nombre, correo, clave_hash, rol, activo)
    VALUES (N'Portal de reservas en línea', N'portal@hotel.com', N'SIN_ACCESO', N'CLIENTE', 0);
GO

/* ---------- 5. Canal y destino del comprobante en la reserva ----------
   El usuario final elige a donde quiere que le llegue el comprobante. */
IF COL_LENGTH('RESERVA', 'canal_comprobante') IS NULL
    ALTER TABLE RESERVA ADD
        canal_comprobante   NVARCHAR(10)  NULL,
        destino_comprobante NVARCHAR(120) NULL;
GO
IF OBJECT_ID('CK_RESERVA_canal', 'C') IS NULL
    ALTER TABLE RESERVA ADD CONSTRAINT CK_RESERVA_canal
        CHECK (canal_comprobante IS NULL OR canal_comprobante IN (N'CORREO', N'WHATSAPP'));
GO

/* ---------- 6. Referencia del pago simulado ----------
   Guarda el numero de autorizacion de la pasarela simulada y los ultimos cuatro
   digitos de la tarjeta, o la marca del pago en efectivo. Nunca el numero completo
   ni el codigo de seguridad (Ley 1581 de 2012, principio de minimizacion). */
IF COL_LENGTH('PAGO', 'referencia') IS NULL
    ALTER TABLE PAGO ADD referencia NVARCHAR(40) NULL;
GO

/* ---------- 7. Cuenta de cliente para la sustentacion ----------
   laura@correo.com / Cliente2026*, vinculada al huesped 1010101010 de los datos de prueba. */
DECLARE @idHuesped INT = (SELECT TOP 1 id_huesped FROM HUESPED WHERE numero_documento = N'1010101010');

IF @idHuesped IS NOT NULL AND NOT EXISTS (SELECT 1 FROM USUARIO WHERE correo = N'laura@correo.com')
    INSERT INTO USUARIO (nombre, correo, clave_hash, rol, activo, id_huesped)
    VALUES (N'Laura Martínez Pérez', N'laura@correo.com',
            N'PBKDF2$100000$NzIWlJzo28337OyO6GO19g==$i1FlxyCTwo86LzNW0BVJtZZuRl5KMouMzWS5yP/HK4o=',
            N'CLIENTE', 1, @idHuesped);

/* El huesped de prueba necesita correo para recibir el comprobante */
UPDATE HUESPED SET correo = N'laura.martinez@correo.com'
 WHERE numero_documento = N'1010101010' AND (correo IS NULL OR correo = N'');
GO

/* ---------- 8. Verificacion ----------
   Despues de ejecutar el script deben verse tres tipos con imagen, un unico
   ADMINISTRADOR activo y las cuentas de cliente. */
SELECT nombre, capacidad, imagen FROM TIPO_HABITACION ORDER BY id_tipo;
SELECT rol, activo, COUNT(*) AS cuentas FROM USUARIO GROUP BY rol, activo ORDER BY rol, activo;
SELECT u.correo, u.rol, u.activo, h.numero_documento
  FROM USUARIO u LEFT JOIN HUESPED h ON h.id_huesped = u.id_huesped ORDER BY u.id_usuario;
GO

PRINT 'Script 07 aplicado: imagenes, perfiles ADMINISTRADOR y CLIENTE, canal del comprobante y referencia de pago.';
GO
