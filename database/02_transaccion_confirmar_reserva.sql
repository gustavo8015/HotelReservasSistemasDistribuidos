/* =====================================================================
   Hotel Reservas - Sistema Transaccional Movil (APK)
   Script 02: transaccion critica "confirmar reserva"
   Implementa la unidad de trabajo ACID descrita en la Fase II (seccion 4):
   verificar disponibilidad -> bloquear -> registrar reserva, pago,
   comprobante y bitacora. Si algo falla, ROLLBACK completo.
   ===================================================================== */
USE HotelReservas;
GO

/* Secuencia para el codigo legible de la reserva */
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'seq_reserva')
    CREATE SEQUENCE seq_reserva AS INT START WITH 1 INCREMENT BY 1;
GO

CREATE OR ALTER PROCEDURE sp_ConfirmarReserva
    @id_huesped       INT,
    @id_habitacion    INT,
    @id_usuario       INT,
    @fecha_entrada    DATE,
    @fecha_salida     DATE,
    @numero_personas  INT,
    @medio_pago       NVARCHAR(20),
    @id_reserva_out   INT OUTPUT,
    @codigo_out       NVARCHAR(12) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;                 -- cualquier error revierte toda la transaccion
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

    IF @fecha_salida <= @fecha_entrada
        THROW 50000, 'La fecha de salida debe ser posterior a la fecha de entrada.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        /* 1. Verificar disponibilidad con bloqueo pesimista:
              UPDLOCK + HOLDLOCK impiden que otra sesion inserte una reserva
              solapada para la misma habitacion hasta que esta termine. */
        IF EXISTS (SELECT 1
                   FROM RESERVA WITH (UPDLOCK, HOLDLOCK)
                   WHERE id_habitacion = @id_habitacion
                     AND estado IN ('CONFIRMADA', 'EN_CASA')
                     AND fecha_entrada < @fecha_salida
                     AND fecha_salida  > @fecha_entrada)
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50001, 'Habitación no disponible para el rango de fechas solicitado.', 1;
        END

        /* 2. Validar que la habitacion exista y no este en mantenimiento */
        IF NOT EXISTS (SELECT 1 FROM HABITACION
                       WHERE id_habitacion = @id_habitacion AND estado <> 'MANTENIMIENTO')
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50002, 'La habitación no existe o está en mantenimiento.', 1;
        END

        /* 3. Calcular el valor total con la tarifa vigente del tipo de habitacion */
        DECLARE @noches INT = DATEDIFF(DAY, @fecha_entrada, @fecha_salida);
        DECLARE @valor_noche DECIMAL(12,2);

        SELECT TOP 1 @valor_noche = t.valor_noche
        FROM TARIFA t
        INNER JOIN HABITACION h ON h.id_tipo = t.id_tipo
        WHERE h.id_habitacion = @id_habitacion
          AND t.vigencia_desde <= @fecha_entrada
          AND (t.vigencia_hasta IS NULL OR t.vigencia_hasta >= @fecha_entrada)
        ORDER BY t.vigencia_desde DESC;

        IF @valor_noche IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 50003, 'No existe una tarifa vigente para el tipo de habitación.', 1;
        END

        DECLARE @valor_total DECIMAL(12,2) = @noches * @valor_noche;

        /* 4. Registrar la reserva */
        SET @codigo_out = 'RSV' + RIGHT('000000000' + CAST(NEXT VALUE FOR seq_reserva AS NVARCHAR(9)), 9);

        INSERT INTO RESERVA (codigo, id_huesped, id_habitacion, id_usuario, fecha_entrada,
                             fecha_salida, numero_personas, valor_total, estado)
        VALUES (@codigo_out, @id_huesped, @id_habitacion, @id_usuario, @fecha_entrada,
                @fecha_salida, @numero_personas, @valor_total, 'CONFIRMADA');

        SET @id_reserva_out = SCOPE_IDENTITY();

        /* 5. Registrar el pago */
        INSERT INTO PAGO (id_reserva, valor, medio, estado)
        VALUES (@id_reserva_out, @valor_total, @medio_pago, 'APROBADO');

        /* 6. Emitir el comprobante (1:1 con la reserva) */
        INSERT INTO COMPROBANTE (id_reserva, numero, valor)
        VALUES (@id_reserva_out, 'CMP' + RIGHT('000000000' + CAST(@id_reserva_out AS NVARCHAR(9)), 9), @valor_total);

        /* 7. Bitacora de auditoria */
        INSERT INTO BITACORA_RESERVA (id_reserva, id_usuario, estado_anterior, estado_nuevo, detalle)
        VALUES (@id_reserva_out, @id_usuario, NULL, 'CONFIRMADA',
                'Reserva confirmada: ' + CAST(@noches AS NVARCHAR(5)) + ' noche(s), valor ' + CAST(@valor_total AS NVARCHAR(20)));

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;      -- nada queda a medias
        THROW;                         -- la API traduce el error a HTTP 409 / 400
    END CATCH
END
GO

/* ---------------------------------------------------------------------
   Ejemplo de uso (despues de ejecutar 03_datos_de_prueba.sql):

   DECLARE @id INT, @cod NVARCHAR(12);
   EXEC sp_ConfirmarReserva
        @id_huesped = 1, @id_habitacion = 1, @id_usuario = 1,
        @fecha_entrada = '2026-10-10', @fecha_salida = '2026-10-12',
        @numero_personas = 2, @medio_pago = 'TARJETA',
        @id_reserva_out = @id OUTPUT, @codigo_out = @cod OUTPUT;
   SELECT @id AS id_reserva, @cod AS codigo;

   Prueba de concurrencia: ejecutar la misma llamada en dos ventanas de
   SSMS al mismo tiempo; solo una confirma, la otra recibe el error 50001.
   --------------------------------------------------------------------- */
