/* =====================================================================
   Hotel Reservas - Script 18
   Plazo de pago en efectivo y cancelacion por parte del huesped.
   Requiere los guiones anteriores. Se puede ejecutar varias veces.

   Dos reglas de negocio nuevas:

   1. Pago en efectivo con plazo de 24 horas. La reserva en efectivo deja
      de cobrarse en el momento: queda RETENIDA en estado PENDIENTE con
      una hora limite guardada en RESERVA.limite_pago. Mientras el plazo
      corre, la habitacion no se le ofrece a nadie mas. Si el plazo vence
      sin pago, la reserva se cancela sola y la habitacion se libera.

   2. Cancelacion libre hasta 24 horas antes de la llegada. Dentro de ese
      ultimo dia el portal ya no permite cancelar; queda en manos de
      recepcion.

   Las dos cifras estan en un solo lugar de la API, en PortalController,
   como HorasPlazoPago y HorasMinimasCancelacion, de modo que cambiarlas
   es cambiar dos numeros.
   ===================================================================== */
USE HotelReservas;
GO

/* ---------- 1. Hora limite de pago ---------- */
IF COL_LENGTH('RESERVA', 'limite_pago') IS NULL
    ALTER TABLE RESERVA ADD limite_pago DATETIME2 NULL;
GO

/* Indice para encontrar rapido las retenciones que ya vencieron */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RESERVA_limite_pago')
    CREATE INDEX IX_RESERVA_limite_pago ON RESERVA (estado, limite_pago);
GO

/* ---------- 2. Liberar las retenciones vencidas ----------
   Se invoca desde la API antes de cada consulta de disponibilidad y
   antes de confirmar, de modo que una habitacion retenida por alguien
   que no pago vuelve a ofrecerse sin intervencion de nadie. */
CREATE OR ALTER PROCEDURE sp_LiberarReservasVencidas
    @liberadas INT = 0 OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @vencidas TABLE (id_reserva INT, id_usuario INT);

    BEGIN TRY
        BEGIN TRANSACTION;

        UPDATE r
           SET r.estado = 'CANCELADA'
          OUTPUT inserted.id_reserva, inserted.id_usuario INTO @vencidas
          FROM RESERVA r
         WHERE r.estado = 'PENDIENTE'
           AND r.limite_pago IS NOT NULL
           AND r.limite_pago < SYSDATETIME();

        SET @liberadas = (SELECT COUNT(*) FROM @vencidas);

        IF @liberadas > 0
            INSERT INTO BITACORA_RESERVA (id_reserva, id_usuario, estado_anterior, estado_nuevo, detalle)
            SELECT v.id_reserva, v.id_usuario, 'PENDIENTE', 'CANCELADA',
                   'Vencio el plazo de pago en efectivo. La habitacion queda libre.'
              FROM @vencidas v;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END
GO

/* ---------- 3. sp_ConfirmarReserva con estado y plazo ----------
   Se agregan dos parametros opcionales al final, de modo que cualquier
   llamada anterior sigue funcionando igual: @estado por defecto
   'CONFIRMADA' y @horas_plazo por defecto NULL.

   Cambio importante en la verificacion de disponibilidad: ahora una
   reserva PENDIENTE con el plazo todavia vigente tambien ocupa la
   habitacion. Sin esto, dos personas podrian retener la misma habitacion
   al mismo tiempo. */
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
    @exento_iva       BIT = NULL,          -- NULL: se toma de HUESPED.extranjero_no_residente
    @estado           NVARCHAR(15) = 'CONFIRMADA',
    @horas_plazo      INT = NULL           -- solo con @estado = 'PENDIENTE'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    IF @fecha_salida <= @fecha_entrada
        THROW 50000, 'La fecha de salida debe ser posterior a la fecha de entrada.', 1;

    IF @estado NOT IN ('CONFIRMADA', 'PENDIENTE')
        THROW 50013, 'El estado inicial de una reserva solo puede ser CONFIRMADA o PENDIENTE.', 1;

    DECLARE @limite DATETIME2 = CASE WHEN @estado = 'PENDIENTE' AND @horas_plazo IS NOT NULL
                                     THEN DATEADD(HOUR, @horas_plazo, SYSDATETIME()) END;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM RESERVA WITH (UPDLOCK, HOLDLOCK)
                   WHERE id_habitacion = @id_habitacion
                     AND (estado IN ('CONFIRMADA', 'EN_CASA')
                          OR (estado = 'PENDIENTE' AND limite_pago IS NOT NULL AND limite_pago > SYSDATETIME()))
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
                             numero_personas, subtotal, tasa_iva, impuesto, exento_iva, valor_total, estado, limite_pago)
        VALUES (@codigo_out, @id_huesped, @id_habitacion, @id_usuario, @fecha_entrada, @fecha_salida,
                @numero_personas, @subtotal, @tasa, @impuesto, @exento_iva, @valor_total, @estado, @limite);
        SET @id_reserva_out = SCOPE_IDENTITY();

        /* El pago solo se registra cuando el dinero ya entro. Una reserva
           retenida no tiene fila en PAGO: la crea recepcion al cobrar. */
        IF @estado = 'CONFIRMADA'
            INSERT INTO PAGO (id_reserva, valor, medio, estado)
            VALUES (@id_reserva_out, @valor_total, @medio_pago, 'APROBADO');

        INSERT INTO COMPROBANTE (id_reserva, numero, valor)
        VALUES (@id_reserva_out, 'CMP' + RIGHT('000000000' + CAST(@id_reserva_out AS NVARCHAR(9)), 9), @valor_total);

        INSERT INTO BITACORA_RESERVA (id_reserva, id_usuario, estado_anterior, estado_nuevo, detalle)
        VALUES (@id_reserva_out, @id_usuario, NULL, @estado,
                CASE WHEN @estado = 'PENDIENTE'
                     THEN 'Reserva retenida sin pago. Plazo hasta ' + CONVERT(NVARCHAR(20), @limite, 120) + '. '
                     ELSE 'Reserva confirmada: ' END
                + CAST(@noches AS NVARCHAR(5)) + ' noche(s), subtotal ' + CAST(@subtotal AS NVARCHAR(20))
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

/* ---------- 4. Verificacion ----------
   La columna debe existir, los dos procedimientos deben estar compilados
   y la consulta final no debe devolver filas si no hay retenciones. */
SELECT CASE WHEN COL_LENGTH('RESERVA','limite_pago') IS NULL
            THEN 'FALTA la columna limite_pago' ELSE 'limite_pago OK' END AS columna,
       CASE WHEN OBJECT_ID('sp_LiberarReservasVencidas') IS NULL
            THEN 'FALTA sp_LiberarReservasVencidas' ELSE 'sp_LiberarReservasVencidas OK' END AS liberar,
       CASE WHEN OBJECT_ID('sp_ConfirmarReserva') IS NULL
            THEN 'FALTA sp_ConfirmarReserva' ELSE 'sp_ConfirmarReserva OK' END AS confirmar;
GO

SELECT codigo, estado, limite_pago, fecha_entrada, fecha_salida
  FROM RESERVA
 WHERE estado = 'PENDIENTE'
 ORDER BY limite_pago;
GO

PRINT 'Script 18 aplicado: plazo de pago en efectivo y liberacion automatica de retenciones vencidas.';
GO
