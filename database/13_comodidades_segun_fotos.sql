/* =====================================================================
   Hotel Reservas - Script 13
   "Lo que incluye" ajustado a lo que muestran las fotografias.
   Requiere 01, 03, 05, 06, 07, 08, 09, 10, 11 y 12.
   Se puede ejecutar varias veces.

   Criterio aplicado, uniforme para los tres tipos: la lista de comodidades
   solo nombra lo que se ve en la fotografia del tipo, mas el bano privado,
   que tiene su propia vista en la galeria, y el wifi, que ninguna
   fotografia puede mostrar. Todo lo demas sale.

   Que cambia respecto de la version anterior:
     Sencilla   entra el minibar y el telefono, que estan en la foto;
                salen el escritorio y el aire acondicionado, que no estan.
     Doble      sale el minibar, que la foto no tiene; se mantienen el
                televisor y el aire acondicionado, que si se ven.
     Cuadruple  sale el televisor, que no aparece en la foto; entra el
                telefono de la mesa de noche; sale el escritorio.

   Tambien salen de la galeria las tres ilustraciones de "zona de trabajo",
   porque prometian un escritorio que ninguna habitacion declara ya.
   ===================================================================== */
USE HotelReservas;
GO

/* ---------- 1. Comodidades ---------- */
UPDATE TIPO_HABITACION
   SET comodidades = N'Cama sencilla;Televisor de pared;Minibar;Mesa de noche con teléfono;Lámpara de lectura;Ventana con postigos;Baño privado con ducha;Wifi sin costo'
 WHERE nombre = N'Sencilla';

UPDATE TIPO_HABITACION
   SET comodidades = N'Dos camas sencillas;Televisor de pared;Aire acondicionado;Ventana con persianas;Baño privado con ducha;Wifi sin costo'
 WHERE nombre = N'Doble';

UPDATE TIPO_HABITACION
   SET comodidades = N'Cuatro camas sencillas;Aire acondicionado;Mesa de noche con teléfono;Baño privado con ducha;Wifi sin costo;Servicio de aseo diario'
 WHERE nombre = N'Cuádruple';
GO

/* ---------- 2. Descripciones del catalogo ----------
   La descripcion corta tampoco puede prometer lo que la foto no tiene. */
UPDATE TIPO_HABITACION SET descripcion = N'Una cama sencilla, televisor, minibar y baño privado. Para un huésped.'
 WHERE nombre = N'Sencilla';
UPDATE TIPO_HABITACION SET descripcion = N'Dos camas sencillas, televisor, aire acondicionado y baño privado. Para dos huéspedes.'
 WHERE nombre = N'Doble';
UPDATE TIPO_HABITACION SET descripcion = N'Cuatro camas sencillas, aire acondicionado y baño privado. Para tres o cuatro huéspedes.'
 WHERE nombre = N'Cuádruple';
GO

/* ---------- 3. Galeria: fuera las vistas de zona de trabajo ---------- */
DELETE FROM FOTO_HABITACION
 WHERE ruta IN (N'img/habitaciones/sencilla-2.svg',
                N'img/habitaciones/doble-2.svg',
                N'img/habitaciones/cuadruple-2.svg');
GO

/* ---------- 4. Portada ----------
   Solo se recalcula si quedo vacia o si apunta a una foto que ya no esta. */
UPDATE t SET t.imagen = f.ruta
  FROM TIPO_HABITACION t
 CROSS APPLY (SELECT TOP 1 ruta FROM FOTO_HABITACION WHERE id_tipo = t.id_tipo ORDER BY orden, id_foto) f
 WHERE t.imagen IS NULL OR t.imagen = N''
    OR NOT EXISTS (SELECT 1 FROM FOTO_HABITACION x WHERE x.id_tipo = t.id_tipo AND x.ruta = t.imagen);
GO

/* ---------- 5. Verificacion ---------- */
SELECT t.nombre, t.capacidad, t.descripcion, t.comodidades
  FROM TIPO_HABITACION t
 ORDER BY t.capacidad, t.nombre;
GO

SELECT t.nombre, f.orden, f.ruta, f.descripcion
  FROM FOTO_HABITACION f JOIN TIPO_HABITACION t ON t.id_tipo = f.id_tipo
 ORDER BY t.capacidad, t.nombre, f.orden, f.id_foto;
GO

PRINT 'Script 13 aplicado: comodidades y descripciones ajustadas a las fotografias.';
GO
