/* =====================================================================
   19_catalogo_asistente_ampliado.sql
   Amplía CK_CONSULTA_funcion de CONSULTA_ASISTENTE con las funciones que
   nacieron en las versiones v0.14 y v0.15 del asistente.

   Por qué hace falta: el guion 06 creó la restricción con las cinco
   funciones que existían entonces. Al agregar las consultas de políticas,
   de las reservas del propio huésped, de las reservas sin pagar, de los
   plazos por vencer y de las cancelaciones del día, la bitácora rechaza la
   fila y la consulta termina en error 500 aunque la respuesta sea correcta.

   Requiere 06. Es idempotente: se puede ejecutar varias veces.
   ===================================================================== */
USE HotelReservas;
GO

IF OBJECT_ID('CK_CONSULTA_funcion', 'C') IS NOT NULL
BEGIN
    ALTER TABLE CONSULTA_ASISTENTE DROP CONSTRAINT CK_CONSULTA_funcion;
    PRINT 'Restricción anterior eliminada.';
END
GO

/* Catálogo completo de funciones de solo lectura (CatalogoFunciones.Descripciones).
   Al agregar una función nueva al catálogo de la API, agréguela también aquí. */
ALTER TABLE CONSULTA_ASISTENTE WITH CHECK
ADD CONSTRAINT CK_CONSULTA_funcion CHECK (funcion_invocada IS NULL OR funcion_invocada IN
    ('consultarDisponibilidad',
     'consultarTarifa',
     'consultarReserva',
     'consultarLlegadas',
     'consultarReservasConfirmadas',
     'consultarMisReservas',
     'consultarPoliticas',
     'consultarReservasSinPagar',
     'consultarPlazosPorVencer',
     'consultarCancelaciones'));
GO

PRINT 'CK_CONSULTA_funcion actualizada con las diez funciones del catálogo.';
GO

/* ---------- Verificación ----------
SELECT TOP 20 c.fecha, c.texto_consulta, c.funcion_invocada, c.interprete, c.exitosa, c.duracion_ms
FROM CONSULTA_ASISTENTE c ORDER BY c.fecha DESC;
---------------------------------- */
