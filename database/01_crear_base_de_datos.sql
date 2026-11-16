/* =====================================================================
   Hotel Reservas - Sistema Transaccional Movil (APK)
   Script 01: creacion de la base de datos y del modelo relacional
   Motor: Microsoft SQL Server (Express / Developer) - ejecutar en SSMS
   Corresponde al modelo relacional de la Fase II (seccion 7).
   ===================================================================== */

IF DB_ID('HotelReservas') IS NULL
    CREATE DATABASE HotelReservas;
GO
USE HotelReservas;
GO

/* ---------- 1. USUARIO (recepcionista / administrador) ---------- */
CREATE TABLE USUARIO (
    id_usuario   INT IDENTITY(1,1) NOT NULL,
    nombre       NVARCHAR(100) NOT NULL,
    correo       NVARCHAR(120) NOT NULL,
    clave_hash   NVARCHAR(255) NOT NULL,
    rol          NVARCHAR(20)  NOT NULL,
    activo       BIT           NOT NULL CONSTRAINT DF_USUARIO_activo DEFAULT (1),
    CONSTRAINT PK_USUARIO PRIMARY KEY (id_usuario),
    CONSTRAINT UQ_USUARIO_correo UNIQUE (correo),
    CONSTRAINT CK_USUARIO_rol CHECK (rol IN ('RECEPCIONISTA', 'ADMINISTRADOR'))
);

/* ---------- 2. HUESPED ---------- */
CREATE TABLE HUESPED (
    id_huesped        INT IDENTITY(1,1) NOT NULL,
    tipo_documento    NVARCHAR(5)  NOT NULL,
    numero_documento  NVARCHAR(20) NOT NULL,
    nombres           NVARCHAR(80) NOT NULL,
    apellidos         NVARCHAR(80) NOT NULL,
    telefono          NVARCHAR(20) NULL,
    correo            NVARCHAR(120) NULL,
    CONSTRAINT PK_HUESPED PRIMARY KEY (id_huesped),
    CONSTRAINT UQ_HUESPED_documento UNIQUE (numero_documento)
);

/* ---------- 3. TIPO_HABITACION ---------- */
CREATE TABLE TIPO_HABITACION (
    id_tipo      INT IDENTITY(1,1) NOT NULL,
    nombre       NVARCHAR(50)  NOT NULL,
    capacidad    INT           NOT NULL,
    descripcion  NVARCHAR(255) NULL,
    CONSTRAINT PK_TIPO_HABITACION PRIMARY KEY (id_tipo),
    CONSTRAINT CK_TIPO_capacidad CHECK (capacidad > 0)
);

/* ---------- 4. HABITACION ---------- */
CREATE TABLE HABITACION (
    id_habitacion  INT IDENTITY(1,1) NOT NULL,
    numero         NVARCHAR(10) NOT NULL,
    piso           INT          NOT NULL,
    id_tipo        INT          NOT NULL,
    estado         NVARCHAR(15) NOT NULL CONSTRAINT DF_HABITACION_estado DEFAULT ('DISPONIBLE'),
    CONSTRAINT PK_HABITACION PRIMARY KEY (id_habitacion),
    CONSTRAINT UQ_HABITACION_numero UNIQUE (numero),
    CONSTRAINT FK_HABITACION_TIPO FOREIGN KEY (id_tipo) REFERENCES TIPO_HABITACION (id_tipo),
    CONSTRAINT CK_HABITACION_estado CHECK (estado IN ('DISPONIBLE', 'OCUPADA', 'MANTENIMIENTO'))
);

/* ---------- 5. TARIFA ---------- */
CREATE TABLE TARIFA (
    id_tarifa       INT IDENTITY(1,1) NOT NULL,
    id_tipo         INT           NOT NULL,
    valor_noche     DECIMAL(12,2) NOT NULL,
    vigencia_desde  DATE          NOT NULL,
    vigencia_hasta  DATE          NULL,
    CONSTRAINT PK_TARIFA PRIMARY KEY (id_tarifa),
    CONSTRAINT FK_TARIFA_TIPO FOREIGN KEY (id_tipo) REFERENCES TIPO_HABITACION (id_tipo),
    CONSTRAINT CK_TARIFA_valor CHECK (valor_noche > 0),
    CONSTRAINT CK_TARIFA_vigencia CHECK (vigencia_hasta IS NULL OR vigencia_hasta >= vigencia_desde)
);

/* ---------- 6. RESERVA (tabla central de la transaccion) ---------- */
CREATE TABLE RESERVA (
    id_reserva       INT IDENTITY(1,1) NOT NULL,
    codigo           NVARCHAR(12)  NOT NULL,
    id_huesped       INT           NOT NULL,
    id_habitacion    INT           NOT NULL,
    id_usuario       INT           NOT NULL,
    fecha_entrada    DATE          NOT NULL,
    fecha_salida     DATE          NOT NULL,
    numero_personas  INT           NOT NULL,
    valor_total      DECIMAL(12,2) NOT NULL,
    estado           NVARCHAR(15)  NOT NULL CONSTRAINT DF_RESERVA_estado DEFAULT ('CONFIRMADA'),
    fecha_creacion   DATETIME2     NOT NULL CONSTRAINT DF_RESERVA_fecha DEFAULT (SYSDATETIME()),
    version_fila     ROWVERSION    NOT NULL,   -- control de concurrencia optimista
    CONSTRAINT PK_RESERVA PRIMARY KEY (id_reserva),
    CONSTRAINT UQ_RESERVA_codigo UNIQUE (codigo),
    CONSTRAINT FK_RESERVA_HUESPED    FOREIGN KEY (id_huesped)    REFERENCES HUESPED (id_huesped),
    CONSTRAINT FK_RESERVA_HABITACION FOREIGN KEY (id_habitacion) REFERENCES HABITACION (id_habitacion),
    CONSTRAINT FK_RESERVA_USUARIO    FOREIGN KEY (id_usuario)    REFERENCES USUARIO (id_usuario),
    CONSTRAINT CK_RESERVA_fechas CHECK (fecha_salida > fecha_entrada),
    CONSTRAINT CK_RESERVA_personas CHECK (numero_personas > 0),
    CONSTRAINT CK_RESERVA_estado CHECK (estado IN ('PENDIENTE', 'CONFIRMADA', 'EN_CASA', 'FINALIZADA', 'CANCELADA'))
);

/* Indice para la verificacion de disponibilidad por habitacion y rango de fechas */
CREATE INDEX IX_RESERVA_habitacion_fechas
    ON RESERVA (id_habitacion, fecha_entrada, fecha_salida)
    INCLUDE (estado);

/* ---------- 7. PAGO ---------- */
CREATE TABLE PAGO (
    id_pago     INT IDENTITY(1,1) NOT NULL,
    id_reserva  INT           NOT NULL,
    valor       DECIMAL(12,2) NOT NULL,
    medio       NVARCHAR(20)  NOT NULL,
    fecha       DATETIME2     NOT NULL CONSTRAINT DF_PAGO_fecha DEFAULT (SYSDATETIME()),
    estado      NVARCHAR(15)  NOT NULL CONSTRAINT DF_PAGO_estado DEFAULT ('APROBADO'),
    CONSTRAINT PK_PAGO PRIMARY KEY (id_pago),
    CONSTRAINT FK_PAGO_RESERVA FOREIGN KEY (id_reserva) REFERENCES RESERVA (id_reserva),
    CONSTRAINT CK_PAGO_valor CHECK (valor > 0),
    CONSTRAINT CK_PAGO_medio CHECK (medio IN ('EFECTIVO', 'TARJETA', 'TRANSFERENCIA')),
    CONSTRAINT CK_PAGO_estado CHECK (estado IN ('APROBADO', 'RECHAZADO', 'ANULADO'))
);

/* ---------- 8. COMPROBANTE (1:1 con RESERVA) ---------- */
CREATE TABLE COMPROBANTE (
    id_comprobante  INT IDENTITY(1,1) NOT NULL,
    id_reserva      INT           NOT NULL,
    numero          NVARCHAR(15)  NOT NULL,
    fecha_emision   DATETIME2     NOT NULL CONSTRAINT DF_COMPROBANTE_fecha DEFAULT (SYSDATETIME()),
    valor           DECIMAL(12,2) NOT NULL,
    CONSTRAINT PK_COMPROBANTE PRIMARY KEY (id_comprobante),
    CONSTRAINT UQ_COMPROBANTE_numero UNIQUE (numero),
    CONSTRAINT UQ_COMPROBANTE_reserva UNIQUE (id_reserva),
    CONSTRAINT FK_COMPROBANTE_RESERVA FOREIGN KEY (id_reserva) REFERENCES RESERVA (id_reserva)
);

/* ---------- 9. BITACORA_RESERVA (auditoria) ---------- */
CREATE TABLE BITACORA_RESERVA (
    id_bitacora      INT IDENTITY(1,1) NOT NULL,
    id_reserva       INT           NOT NULL,
    id_usuario       INT           NOT NULL,
    fecha            DATETIME2     NOT NULL CONSTRAINT DF_BITACORA_fecha DEFAULT (SYSDATETIME()),
    estado_anterior  NVARCHAR(15)  NULL,
    estado_nuevo     NVARCHAR(15)  NOT NULL,
    detalle          NVARCHAR(255) NULL,
    CONSTRAINT PK_BITACORA_RESERVA PRIMARY KEY (id_bitacora),
    CONSTRAINT FK_BITACORA_RESERVA FOREIGN KEY (id_reserva) REFERENCES RESERVA (id_reserva),
    CONSTRAINT FK_BITACORA_USUARIO FOREIGN KEY (id_usuario) REFERENCES USUARIO (id_usuario)
);
GO

PRINT 'Base de datos HotelReservas creada con 9 tablas.';
GO
