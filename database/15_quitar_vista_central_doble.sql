/* =====================================================================
   Hotel Reservas - Script 15
   Quitar de la galeria de la Doble la vista central "Las dos camas
   sencillas" (img/habitaciones/doble-camas.jpg).
   Requiere los guiones anteriores. Se puede ejecutar varias veces.

   Motivo: esa imagen muestra una habitacion distinta de la fotografia
   real de la Doble (otros cabeceros, otro piso, otra ventana), y puestas
   una al lado de la otra se contradicen.

   La galeria de la Doble queda con su fotografia, el bano y el plano,
   igual que la de la Sencilla.

   El archivo se queda en HotelReservas.Web/wwwroot/img/habitaciones por
   si quiere volver a usarlo. Para devolverlo a la galeria basta con
   registrarlo desde Administracion, pestaña Fotos del catalogo.
   ===================================================================== */
USE HotelReservas;
GO

DELETE FROM FOTO_HABITACION WHERE ruta = N'img/habitaciones/doble-camas.jpg';
GO

/* La portada solo se recalcula si quedo apuntando a una foto que ya no esta. */
UPDATE t SET t.imagen = f.ruta
  FROM TIPO_HABITACION t
 CROSS APPLY (SELECT TOP 1 ruta FROM FOTO_HABITACION WHERE id_tipo = t.id_tipo ORDER BY orden, id_foto) f
 WHERE t.imagen IS NULL OR t.imagen = N''
    OR NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = t.imagen);
GO

/* ---------- Verificacion ----------
   Cada tipo debe quedar con su fotografia, el bano y el plano. */
SELECT t.nombre, f.orden, f.ruta, f.descripcion
  FROM FOTO_HABITACION f JOIN TIPO_HABITACION t ON t.id_tipo = f.id_tipo
 ORDER BY t.capacidad, t.nombre, f.orden, f.id_foto;
GO

PRINT 'Script 15 aplicado: la Doble queda con foto, bano y plano.';
GO
