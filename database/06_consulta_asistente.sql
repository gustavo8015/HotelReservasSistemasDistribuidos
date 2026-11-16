/* =====================================================================
   Hotel Reservas - Script 06
   Tabla CONSULTA_ASISTENTE (modelo de datos del servicio de IA, sección 13.3.1)
   y contraseñas de los usuarios de prueba para la API (PBKDF2-SHA256).
   Requiere 01, 03 y 05. Ejecutar antes de iniciar HotelReservas.Api.
   ===================================================================== */
USE HotelReservas;
GO

/* ---------- 1. Bitácora de consultas del asistente (trazabilidad, RNF05) ----------
   El asistente solo lee las tablas del núcleo; esta es la única tabla que agrega. */
IF OBJECT_ID('CONSULTA_ASISTENTE') IS NULL
BEGIN
    CREATE TABLE CONSULTA_ASISTENTE (
        id_consulta      BIGINT IDENTITY(1,1) NOT NULL,
        id_usuario       INT            NOT NULL,
        fecha            DATETIME2      NOT NULL CONSTRAINT DF_CONSULTA_fecha DEFAULT (SYSDATETIME()),
        texto_consulta   NVARCHAR(500)  NOT NULL,   -- ya filtrado: sin documentos, teléfonos ni correos
        funcion_invocada NVARCHAR(60)   NULL,       -- función del catálogo de solo lectura (NULL = aclaración)
        parametros       NVARCHAR(400)  NULL,       -- JSON con los parámetros interpretados
        interprete       NVARCHAR(20)   NOT NULL,   -- AZURE_OPENAI o REGLAS
        exitosa          BIT            NOT NULL,
        duracion_ms      INT            NOT NULL CONSTRAINT DF_CONSULTA_duracion DEFAULT (0),
        CONSTRAINT PK_CONSULTA_ASISTENTE PRIMARY KEY (id_consulta),
        CONSTRAINT FK_CONSULTA_USUARIO FOREIGN KEY (id_usuario) REFERENCES USUARIO (id_usuario),
        CONSTRAINT CK_CONSULTA_funcion CHECK (funcion_invocada IS NULL OR funcion_invocada IN
            ('consultarDisponibilidad','consultarTarifa','consultarReserva','consultarLlegadas','consultarReservasConfirmadas'))
    );
    CREATE INDEX IX_CONSULTA_usuario_fecha ON CONSULTA_ASISTENTE (id_usuario, fecha);
END
GO

/* ---------- 2. Contraseñas de prueba (todas: Hotel2026*) ----------
   Formato PBKDF2$iteraciones$sal$hash, verificado por HashClave.Verificar en la API.
   En producción, cada usuario debe cambiarla; la API expone POST /api/auth/hash para generar nuevos hashes. */
UPDATE USUARIO SET clave_hash = N'PBKDF2$100000$X1XHSPK7pdC0hn9XUZYjyA==$5i5eQxGVxYV26jzJNQ4f5WdfnzhYGtHzD5LGbpafMKo=' WHERE correo = 'admin@hotel.com';
UPDATE USUARIO SET clave_hash = N'PBKDF2$100000$j3y+YC+HKX7hl/E4Sf68kA==$hwYjSOb51VqPz2sroFeQ/ETHM4IOySuT3XvtCxbckgI=' WHERE correo = 'recepcion1@hotel.com';
UPDATE USUARIO SET clave_hash = N'PBKDF2$100000$U3KNyaA8qXvk0sE1Qa912g==$2jk6bO+ogbQzdN/UnYTWar83XVvYbA9aa3x5+/rX17w=' WHERE correo = 'recepcion2@hotel.com';
GO

/* ---------- 3. Consulta de verificación ----------
SELECT TOP 20 c.fecha, u.nombre, c.texto_consulta, c.funcion_invocada, c.interprete, c.exitosa, c.duracion_ms
FROM CONSULTA_ASISTENTE c JOIN USUARIO u ON u.id_usuario = c.id_usuario ORDER BY c.fecha DESC;
*/
