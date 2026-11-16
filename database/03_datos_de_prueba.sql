/* =====================================================================
   Hotel Reservas - Sistema Transaccional Movil (APK)
   Script 03: datos de prueba para las demostraciones
   ===================================================================== */
USE HotelReservas;
GO

/* Usuarios (la clave se almacenara como hash desde la API; aqui es un marcador) */
INSERT INTO USUARIO (nombre, correo, clave_hash, rol) VALUES
('Administrador del hotel', 'admin@hotel.com',     'HASH_PENDIENTE', 'ADMINISTRADOR'),
('Recepcionista turno 1',   'recepcion1@hotel.com', 'HASH_PENDIENTE', 'RECEPCIONISTA'),
('Recepcionista turno 2',   'recepcion2@hotel.com', 'HASH_PENDIENTE', 'RECEPCIONISTA');

/* Tipos de habitacion */
INSERT INTO TIPO_HABITACION (nombre, capacidad, descripcion) VALUES
('Sencilla', 1, 'Una cama sencilla, baño privado'),
('Doble',    2, 'Una cama doble o dos sencillas'),
('Suite',    4, 'Sala, cama king y balcón');

/* Tarifas vigentes */
INSERT INTO TARIFA (id_tipo, valor_noche, vigencia_desde, vigencia_hasta) VALUES
(1, 120000.00, '2026-01-01', NULL),
(2, 180000.00, '2026-01-01', NULL),
(3, 350000.00, '2026-01-01', NULL);

/* Habitaciones (25 en total, 5 pisos) */
INSERT INTO HABITACION (numero, piso, id_tipo, estado) VALUES
('101', 1, 1, 'DISPONIBLE'), ('102', 1, 1, 'DISPONIBLE'), ('103', 1, 2, 'DISPONIBLE'), ('104', 1, 2, 'DISPONIBLE'), ('105', 1, 3, 'DISPONIBLE'),
('201', 2, 1, 'DISPONIBLE'), ('202', 2, 1, 'DISPONIBLE'), ('203', 2, 2, 'DISPONIBLE'), ('204', 2, 2, 'DISPONIBLE'), ('205', 2, 3, 'DISPONIBLE'),
('301', 3, 1, 'DISPONIBLE'), ('302', 3, 1, 'MANTENIMIENTO'), ('303', 3, 2, 'DISPONIBLE'), ('304', 3, 2, 'DISPONIBLE'), ('305', 3, 3, 'DISPONIBLE'),
('401', 4, 1, 'DISPONIBLE'), ('402', 4, 1, 'DISPONIBLE'), ('403', 4, 2, 'DISPONIBLE'), ('404', 4, 2, 'DISPONIBLE'), ('405', 4, 3, 'DISPONIBLE'),
('501', 5, 1, 'DISPONIBLE'), ('502', 5, 1, 'DISPONIBLE'), ('503', 5, 2, 'DISPONIBLE'), ('504', 5, 2, 'DISPONIBLE'), ('505', 5, 3, 'DISPONIBLE');

/* Huespedes */
INSERT INTO HUESPED (tipo_documento, numero_documento, nombres, apellidos, telefono, correo) VALUES
('CC', '1010101010', 'Laura',  'Martínez Pérez',  '3001112233', 'laura.martinez@correo.com'),
('CC', '2020202020', 'Carlos', 'Gómez Rojas',     '3014445566', 'carlos.gomez@correo.com'),
('CE', '3030303030', 'Ana',    'Silva Duarte',    '3027778899', 'ana.silva@correo.com');
GO

/* Verificacion rapida */
SELECT 'USUARIO' AS tabla, COUNT(*) AS filas FROM USUARIO
UNION ALL SELECT 'TIPO_HABITACION', COUNT(*) FROM TIPO_HABITACION
UNION ALL SELECT 'TARIFA', COUNT(*) FROM TARIFA
UNION ALL SELECT 'HABITACION', COUNT(*) FROM HABITACION
UNION ALL SELECT 'HUESPED', COUNT(*) FROM HUESPED;
GO
