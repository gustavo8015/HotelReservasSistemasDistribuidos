/* =====================================================================
   Hotel Reservas - Script 05
   Impuestos (IVA 19 % y precio neto), comprobante compartible y
   reserva rápida para huésped frecuente.
   Requiere haber ejecutado 01_crear_base_de_datos.sql y
   02_transaccion_confirmar_reserva.sql (03 opcional para datos de prueba).
   ===================================================================== */
USE HotelReservas;
GO

/* ---------- 1. Diseño de datos: desglose del precio en RESERVA ----------
   subtotal    = noches x tarifa (alojamiento)
   tasa_iva    = 0.19 (tarifa general) o 0.00 si el huésped es extranjero
                 no residente (servicio hotelero exento, art. 481 E.T.)
   impuesto    = subtotal x tasa_iva
   valor_total = subtotal + impuesto  (precio neto que paga el huésped)   */
IF COL_LENGTH('RESERVA', 'subtotal') IS NULL
BEGIN
    ALTER TABLE RESERVA ADD
        subtotal   DECIMAL(12,2) NOT NULL CONSTRAINT DF_RESERVA_subtotal DEFAULT (0),
        tasa_iva   DECIMAL(5,4)  NOT NULL CONSTRAINT DF_RESERVA_tasa_iva DEFAULT (0.19),
        impuesto   DECIMAL(12,2) NOT NULL CONSTRAINT DF_RESERVA_impuesto DEFAULT (0),
        exento_iva BIT           NOT NULL CONSTRAINT DF_RESERVA_exento   DEFAULT (0);
END
GO
/* Las reservas existentes guardaban solo el total: se reconstruye el desglose */
UPDATE RESERVA
   SET subtotal = ROUND(valor_total / 1.19, 2),
       impuesto = valor_total - ROUND(valor_total / 1.19, 2)
 WHERE subtotal = 0 AND valor_total > 0;
GO
IF OBJECT_ID('CK_RESERVA_precio_neto') IS NULL
    ALTER TABLE RESERVA ADD CONSTRAINT CK_RESERVA_precio_neto CHECK (valor_total = subtotal + impuesto);
GO

/* Marca del huésped extranjero no residente (requiere pasaporte y tarjeta migratoria) */
IF COL_LENGTH('HUESPED', 'extranjero_no_residente') IS NULL
    ALTER TABLE HUESPED ADD extranjero_no_residente BIT NOT NULL CONSTRAINT DF_HUESPED_ext DEFAULT (0);
GO

/* ---------- 2. Transacción de confirmación con cálculo de impuestos ----------
   Misma lógica de 02 (SERIALIZABLE + UPDLOCK/HOLDLOCK), ahora con el desglose */
CREATE OR ALTER PROCEDURE sp_ConfirmarReserva
    @id_huesped       INT,
    @id_habitacion    INT,
    @id_usuario       INT,
    @fecha_entrada    DATE,
    @fecha_salida     DATE,
    @numero_personas  INT,
    @medio_pago       NVARCHAR(20),
    @id_reserva_out   INT OUTPUT,
    @codigo_out       NVARCHAR(12) OUTPUT,
    @exento_iva       BIT = NULL          -- NULL: se toma de HUESPED.extranjero_no_residente
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    IF @fecha_salida <= @fecha_entrada
        THROW 50000, 'La fecha de salida debe ser posterior a la fecha de entrada.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM RESERVA WITH (UPDLOCK, HOLDLOCK)
                   WHERE id_habitacion = @id_habitacion
                     AND estado IN ('CONFIRMADA', 'EN_CASA')
                     AND fecha_entrada < @fecha_salida AND fecha_salida > @fecha_entrada)
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50001, 'Habitación no disponible para el rango de fechas solicitado.', 1;
        END

        IF NOT EXISTS (SELECT 1 FROM HABITACION WHERE id_habitacion = @id_habitacion AND estado <> 'MANTENIMIENTO')
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50002, 'La habitación no existe o está en mantenimiento.', 1;
        END

        DECLARE @noches INT = DATEDIFF(DAY, @fecha_entrada, @fecha_salida);
        DECLARE @valor_noche DECIMAL(12,2);
        SELECT TOP 1 @valor_noche = t.valor_noche
        FROM TARIFA t INNER JOIN HABITACION h ON h.id_tipo = t.id_tipo
        WHERE h.id_habitacion = @id_habitacion
          AND t.vigencia_desde <= @fecha_entrada
          AND (t.vigencia_hasta IS NULL OR t.vigencia_hasta >= @fecha_entrada)
        ORDER BY t.vigencia_desde DESC;
        IF @valor_noche IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50003, 'No existe una tarifa vigente para el tipo de habitación.', 1;
        END

        /* Cálculo de impuestos y precio neto */
        IF @exento_iva IS NULL
            SELECT @exento_iva = extranjero_no_residente FROM HUESPED WHERE id_huesped = @id_huesped;
        SET @exento_iva = ISNULL(@exento_iva, 0);
        DECLARE @subtotal  DECIMAL(12,2) = @noches * @valor_noche;
        DECLARE @tasa      DECIMAL(5,4)  = CASE WHEN @exento_iva = 1 THEN 0.00 ELSE 0.19 END;
        DECLARE @impuesto  DECIMAL(12,2) = ROUND(@subtotal * @tasa, 0);
        DECLARE @valor_total DECIMAL(12,2) = @subtotal + @impuesto;

        SET @codigo_out = 'RSV' + RIGHT('000000000' + CAST(NEXT VALUE FOR seq_reserva AS NVARCHAR(9)), 9);

        INSERT INTO RESERVA (codigo, id_huesped, id_habitacion, id_usuario, fecha_entrada, fecha_salida,
                             numero_personas, subtotal, tasa_iva, impuesto, exento_iva, valor_total, estado)
        VALUES (@codigo_out, @id_huesped, @id_habitacion, @id_usuario, @fecha_entrada, @fecha_salida,
                @numero_personas, @subtotal, @tasa, @impuesto, @exento_iva, @valor_total, 'CONFIRMADA');
        SET @id_reserva_out = SCOPE_IDENTITY();

        INSERT INTO PAGO (id_reserva, valor, medio, estado)
        VALUES (@id_reserva_out, @valor_total, @medio_pago, 'APROBADO');

        INSERT INTO COMPROBANTE (id_reserva, numero, valor)
        VALUES (@id_reserva_out, 'CMP' + RIGHT('000000000' + CAST(@id_reserva_out AS NVARCHAR(9)), 9), @valor_total);

        INSERT INTO BITACORA_RESERVA (id_reserva, id_usuario, estado_anterior, estado_nuevo, detalle)
        VALUES (@id_reserva_out, @id_usuario, NULL, 'CONFIRMADA',
                'Reserva confirmada: ' + CAST(@noches AS NVARCHAR(5)) + ' noche(s), subtotal ' + CAST(@subtotal AS NVARCHAR(20))
                + ', IVA ' + CAST(@impuesto AS NVARCHAR(20)) + CASE WHEN @exento_iva = 1 THEN ' (exento)' ELSE '' END
                + ', neto ' + CAST(@valor_total AS NVARCHAR(20)));

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* ---------- 3. Comprobante compartible (RF17) ----------
   Devuelve el texto listo para enviarse por WhatsApp o SMS desde la app:
   la API lo entrega en GET /api/comprobantes/{numero}/compartir */
CREATE OR ALTER PROCEDURE sp_TextoComprobante
    @id_reserva INT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.numero AS comprobante, h.telefono,
           N'Hotel Reservas - Comprobante ' + c.numero + CHAR(10)
         + N'Reserva: ' + r.codigo + CHAR(10)
         + N'Huésped: ' + h.nombres + N' ' + h.apellidos + N' (' + h.tipo_documento + N' ' + h.numero_documento + N')' + CHAR(10)
         + N'Habitación: ' + ha.numero + N' - ' + t.nombre + N' - ' + CAST(r.numero_personas AS NVARCHAR(3)) + N' persona(s)' + CHAR(10)
         + N'Entrada: ' + CONVERT(NVARCHAR(10), r.fecha_entrada, 23) + N' - Salida: ' + CONVERT(NVARCHAR(10), r.fecha_salida, 23)
         + N' (' + CAST(DATEDIFF(DAY, r.fecha_entrada, r.fecha_salida) AS NVARCHAR(4)) + N' noche(s))' + CHAR(10)
         + N'Subtotal: $' + FORMAT(r.subtotal, 'N0', 'es-CO') + CHAR(10)
         + N'IVA ' + FORMAT(r.tasa_iva, 'P0') + N': ' + CASE WHEN r.exento_iva = 1 THEN N'exento (extranjero no residente)' ELSE N'$' + FORMAT(r.impuesto, 'N0', 'es-CO') END + CHAR(10)
         + N'Precio neto: $' + FORMAT(r.valor_total, 'N0', 'es-CO') + CHAR(10)
         + N'Pago: ' + p.medio + N' - Estado: ' + r.estado + CHAR(10)
         + N'Presente este comprobante en recepción. ¡Lo esperamos!' AS texto
    FROM RESERVA r
    JOIN COMPROBANTE c ON c.id_reserva = r.id_reserva
    JOIN HUESPED h ON h.id_huesped = r.id_huesped
    JOIN HABITACION ha ON ha.id_habitacion = r.id_habitacion
    JOIN TIPO_HABITACION t ON t.id_tipo = ha.id_tipo
    OUTER APPLY (SELECT TOP 1 medio FROM PAGO WHERE id_reserva = r.id_reserva ORDER BY id_pago DESC) p
    WHERE r.id_reserva = @id_reserva;
END
GO

/* ---------- 4. Reserva rápida para huésped frecuente (RF18) ----------
   Con el documento devuelve el huésped, el número de estadías y las
   preferencias de su última reserva (tipo, personas, noches, medio de pago).
   Huésped frecuente = 2 o más estadías. La API lo expone en GET /api/huespedes?documento= */
CREATE OR ALTER PROCEDURE sp_HuespedFrecuente
    @numero_documento NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT h.id_huesped, h.nombres, h.apellidos, h.tipo_documento, h.numero_documento, h.telefono,
           h.extranjero_no_residente,
           est.estadias,
           CASE WHEN est.estadias >= 2 THEN 1 ELSE 0 END AS es_frecuente,
           u.id_tipo AS ultimo_tipo, u.tipo_nombre AS ultimo_tipo_nombre, u.numero AS ultima_habitacion,
           u.numero_personas AS ultimas_personas, u.noches AS ultimas_noches, u.medio AS ultimo_medio_pago, u.fecha_entrada AS ultima_fecha
    FROM HUESPED h
    OUTER APPLY (SELECT COUNT(*) AS estadias FROM RESERVA r
                 WHERE r.id_huesped = h.id_huesped AND r.estado IN ('CONFIRMADA','EN_CASA','FINALIZADA')) est
    OUTER APPLY (SELECT TOP 1 ha.id_tipo, t.nombre AS tipo_nombre, ha.numero, r.numero_personas,
                        DATEDIFF(DAY, r.fecha_entrada, r.fecha_salida) AS noches, r.fecha_entrada,
                        (SELECT TOP 1 medio FROM PAGO WHERE id_reserva = r.id_reserva ORDER BY id_pago DESC) AS medio
                 FROM RESERVA r JOIN HABITACION ha ON ha.id_habitacion = r.id_habitacion
                 JOIN TIPO_HABITACION t ON t.id_tipo = ha.id_tipo
                 WHERE r.id_huesped = h.id_huesped AND r.estado IN ('CONFIRMADA','EN_CASA','FINALIZADA')
                 ORDER BY r.fecha_entrada DESC) u
    WHERE h.numero_documento = @numero_documento;
END
GO

/* ---------- 5. Prueba rápida ----------
DECLARE @id INT, @cod NVARCHAR(12);
EXEC sp_ConfirmarReserva @id_huesped = 1, @id_habitacion = 3, @id_usuario = 2,
     @fecha_entrada = '2026-11-01', @fecha_salida = '2026-11-03', @numero_personas = 2,
     @medio_pago = 'TARJETA', @id_reserva_out = @id OUTPUT, @codigo_out = @cod OUTPUT;
SELECT codigo, subtotal, tasa_iva, impuesto, exento_iva, valor_total FROM RESERVA WHERE id_reserva = @id;
EXEC sp_TextoComprobante @id;
EXEC sp_HuespedFrecuente '2020202020';
*/
