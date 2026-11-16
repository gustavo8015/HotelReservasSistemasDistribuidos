/* =====================================================================
   Hotel Reservas - Script 16
   Quitar la seccion "Como esta distribuida" de la habitacion Cuadruple.
   Requiere los guiones anteriores. Se puede ejecutar varias veces.

   La ficha dibuja esa seccion, con su titulo y su texto, solo cuando el
   tipo tiene una foto cuya ruta empieza por "plano-". Al borrar esa fila
   desaparece la seccion completa, sin tocar codigo.

   La Sencilla y la Doble conservan su plano.

   El archivo img/habitaciones/plano-cuadruple.jpg se queda en la carpeta
   de imagenes. Para devolver la seccion basta con registrarlo otra vez
   desde Administracion, pestaña Fotos del catalogo, respetando el
   prefijo "plano-" en el nombre del archivo.
   ===================================================================== */
USE HotelReservas;
GO

DELETE f
  FROM FOTO_HABITACION f
  JOIN TIPO_HABITACION t ON t.id_tipo = f.id_tipo
 WHERE t.nombre = N'Cuádruple'
   AND f.ruta LIKE N'img/habitaciones/plano-%';
GO

/* La portada solo se recalcula si quedo apuntando a una foto que ya no esta. */
UPDATE t SET t.imagen = f.ruta
  FROM TIPO_HABITACION t
 CROSS APPLY (SELECT TOP 1 ruta FROM FOTO_HABITACION WHERE id_tipo = t.id_tipo ORDER BY orden, id_foto) f
 WHERE t.imagen IS NULL OR t.imagen = N''
    OR NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = t.imagen);
GO

/* ---------- Verificacion ----------
   La Cuadruple debe quedar con dos filas, su fotografia y el bano.
   La Sencilla y la Doble siguen con tres, incluido su plano. */
SELECT t.nombre, f.orden, f.ruta, f.descripcion
  FROM FOTO_HABITACION f JOIN TIPO_HABITACION t ON t.id_tipo = f.id_tipo
 ORDER BY t.capacidad, t.nombre, f.orden, f.id_foto;
GO

PRINT 'Script 16 aplicado: la Cuadruple ya no muestra la seccion del plano.';
GO
