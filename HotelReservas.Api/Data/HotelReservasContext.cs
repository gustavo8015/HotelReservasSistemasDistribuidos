using Microsoft.EntityFrameworkCore;

namespace HotelReservas.Api.Data;

/// <summary>
/// Capa de acceso a datos (Entity Framework Core). Mapea las tablas creadas por los scripts
/// 01, 05 y 06 del repositorio. La transacción crítica de confirmación no se escribe aquí:
/// se delega al procedimiento sp_ConfirmarReserva del motor (ver ServicioReservas).
/// </summary>
public class HotelReservasContext : DbContext
{
    public HotelReservasContext(DbContextOptions<HotelReservasContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Huesped> Huespedes => Set<Huesped>();
    public DbSet<TipoHabitacion> TiposHabitacion => Set<TipoHabitacion>();
    public DbSet<Habitacion> Habitaciones => Set<Habitacion>();
    public DbSet<Tarifa> Tarifas => Set<Tarifa>();
    public DbSet<Reserva> Reservas => Set<Reserva>();
    public DbSet<Pago> Pagos => Set<Pago>();
    public DbSet<Comprobante> Comprobantes => Set<Comprobante>();
    public DbSet<BitacoraReserva> Bitacora => Set<BitacoraReserva>();
    public DbSet<ConsultaAsistente> ConsultasAsistente => Set<ConsultaAsistente>();
    public DbSet<FotoHabitacion> FotosHabitacion => Set<FotoHabitacion>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Usuario>(e =>
        {
            e.ToTable("USUARIO"); e.HasKey(x => x.IdUsuario);
            e.Property(x => x.IdUsuario).HasColumnName("id_usuario");
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.Correo).HasColumnName("correo");
            e.Property(x => x.ClaveHash).HasColumnName("clave_hash");
            e.Property(x => x.Rol).HasColumnName("rol");
            e.Property(x => x.Activo).HasColumnName("activo");
            e.Property(x => x.IdHuesped).HasColumnName("id_huesped");
        });

        mb.Entity<Huesped>(e =>
        {
            e.ToTable("HUESPED"); e.HasKey(x => x.IdHuesped);
            e.Property(x => x.IdHuesped).HasColumnName("id_huesped");
            e.Property(x => x.TipoDocumento).HasColumnName("tipo_documento");
            e.Property(x => x.NumeroDocumento).HasColumnName("numero_documento");
            e.Property(x => x.Nombres).HasColumnName("nombres");
            e.Property(x => x.Apellidos).HasColumnName("apellidos");
            e.Property(x => x.Telefono).HasColumnName("telefono");
            e.Property(x => x.Correo).HasColumnName("correo");
            e.Property(x => x.ExtranjeroNoResidente).HasColumnName("extranjero_no_residente");
            e.HasMany(x => x.Reservas).WithOne(r => r.Huesped).HasForeignKey(r => r.IdHuesped);
        });

        mb.Entity<TipoHabitacion>(e =>
        {
            e.ToTable("TIPO_HABITACION"); e.HasKey(x => x.IdTipo);
            e.Property(x => x.IdTipo).HasColumnName("id_tipo");
            e.Property(x => x.Nombre).HasColumnName("nombre");
            e.Property(x => x.Capacidad).HasColumnName("capacidad");
            e.Property(x => x.Descripcion).HasColumnName("descripcion");
            e.Property(x => x.Imagen).HasColumnName("imagen");
            e.Property(x => x.Comodidades).HasColumnName("comodidades");
            e.HasMany(x => x.Habitaciones).WithOne(h => h.Tipo).HasForeignKey(h => h.IdTipo);
            e.HasMany(x => x.Fotos).WithOne().HasForeignKey(f => f.IdTipo);
            e.HasMany(x => x.Tarifas).WithOne().HasForeignKey(t => t.IdTipo);
        });

        mb.Entity<FotoHabitacion>(e =>
        {
            e.ToTable("FOTO_HABITACION"); e.HasKey(x => x.IdFoto);
            e.Property(x => x.IdFoto).HasColumnName("id_foto");
            e.Property(x => x.IdTipo).HasColumnName("id_tipo");
            e.Property(x => x.Ruta).HasColumnName("ruta");
            e.Property(x => x.Descripcion).HasColumnName("descripcion");
            e.Property(x => x.Orden).HasColumnName("orden");
        });

        mb.Entity<Habitacion>(e =>
        {
            e.ToTable("HABITACION"); e.HasKey(x => x.IdHabitacion);
            e.Property(x => x.IdHabitacion).HasColumnName("id_habitacion");
            e.Property(x => x.Numero).HasColumnName("numero");
            e.Property(x => x.Piso).HasColumnName("piso");
            e.Property(x => x.IdTipo).HasColumnName("id_tipo");
            e.Property(x => x.Estado).HasColumnName("estado");
            e.Property(x => x.Imagen).HasColumnName("imagen");
        });

        mb.Entity<Tarifa>(e =>
        {
            e.ToTable("TARIFA"); e.HasKey(x => x.IdTarifa);
            e.Property(x => x.IdTarifa).HasColumnName("id_tarifa");
            e.Property(x => x.IdTipo).HasColumnName("id_tipo");
            e.Property(x => x.ValorNoche).HasColumnName("valor_noche").HasColumnType("decimal(12,2)");
            e.Property(x => x.VigenciaDesde).HasColumnName("vigencia_desde");
            e.Property(x => x.VigenciaHasta).HasColumnName("vigencia_hasta");
        });

        mb.Entity<Reserva>(e =>
        {
            e.ToTable("RESERVA"); e.HasKey(x => x.IdReserva);
            e.Property(x => x.IdReserva).HasColumnName("id_reserva");
            e.Property(x => x.Codigo).HasColumnName("codigo");
            e.Property(x => x.IdHuesped).HasColumnName("id_huesped");
            e.Property(x => x.IdHabitacion).HasColumnName("id_habitacion");
            e.Property(x => x.IdUsuario).HasColumnName("id_usuario");
            e.Property(x => x.FechaEntrada).HasColumnName("fecha_entrada");
            e.Property(x => x.FechaSalida).HasColumnName("fecha_salida");
            e.Property(x => x.NumeroPersonas).HasColumnName("numero_personas");
            e.Property(x => x.Subtotal).HasColumnName("subtotal").HasColumnType("decimal(12,2)");
            e.Property(x => x.TasaIva).HasColumnName("tasa_iva").HasColumnType("decimal(5,4)");
            e.Property(x => x.Impuesto).HasColumnName("impuesto").HasColumnType("decimal(12,2)");
            e.Property(x => x.ExentoIva).HasColumnName("exento_iva");
            e.Property(x => x.ValorTotal).HasColumnName("valor_total").HasColumnType("decimal(12,2)");
            e.Property(x => x.Estado).HasColumnName("estado");
            e.Property(x => x.FechaCreacion).HasColumnName("fecha_creacion");
            e.Property(x => x.VersionFila).HasColumnName("version_fila").IsRowVersion();
            e.Property(x => x.CanalComprobante).HasColumnName("canal_comprobante");
            e.Property(x => x.DestinoComprobante).HasColumnName("destino_comprobante");
            e.Property(x => x.LimitePago).HasColumnName("limite_pago");
            e.HasOne(x => x.Habitacion).WithMany().HasForeignKey(x => x.IdHabitacion);
            e.HasMany(x => x.Pagos).WithOne().HasForeignKey(p => p.IdReserva);
            e.HasOne(x => x.Comprobante).WithOne().HasForeignKey<Comprobante>(c => c.IdReserva);
            e.HasMany(x => x.Bitacora).WithOne().HasForeignKey(b => b.IdReserva);
        });

        mb.Entity<Pago>(e =>
        {
            e.ToTable("PAGO"); e.HasKey(x => x.IdPago);
            e.Property(x => x.IdPago).HasColumnName("id_pago");
            e.Property(x => x.IdReserva).HasColumnName("id_reserva");
            e.Property(x => x.Valor).HasColumnName("valor").HasColumnType("decimal(12,2)");
            e.Property(x => x.Medio).HasColumnName("medio");
            e.Property(x => x.Fecha).HasColumnName("fecha");
            e.Property(x => x.Estado).HasColumnName("estado");
            e.Property(x => x.Referencia).HasColumnName("referencia");
        });

        mb.Entity<Comprobante>(e =>
        {
            e.ToTable("COMPROBANTE"); e.HasKey(x => x.IdComprobante);
            e.Property(x => x.IdComprobante).HasColumnName("id_comprobante");
            e.Property(x => x.IdReserva).HasColumnName("id_reserva");
            e.Property(x => x.Numero).HasColumnName("numero");
            e.Property(x => x.FechaEmision).HasColumnName("fecha_emision");
            e.Property(x => x.Valor).HasColumnName("valor").HasColumnType("decimal(12,2)");
        });

        mb.Entity<BitacoraReserva>(e =>
        {
            e.ToTable("BITACORA_RESERVA"); e.HasKey(x => x.IdBitacora);
            e.Property(x => x.IdBitacora).HasColumnName("id_bitacora");
            e.Property(x => x.IdReserva).HasColumnName("id_reserva");
            e.Property(x => x.IdUsuario).HasColumnName("id_usuario");
            e.Property(x => x.Fecha).HasColumnName("fecha");
            e.Property(x => x.EstadoAnterior).HasColumnName("estado_anterior");
            e.Property(x => x.EstadoNuevo).HasColumnName("estado_nuevo");
            e.Property(x => x.Detalle).HasColumnName("detalle");
            e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.IdUsuario);
        });

        mb.Entity<ConsultaAsistente>(e =>
        {
            e.ToTable("CONSULTA_ASISTENTE"); e.HasKey(x => x.IdConsulta);
            e.Property(x => x.IdConsulta).HasColumnName("id_consulta");
            e.Property(x => x.IdUsuario).HasColumnName("id_usuario");
            e.Property(x => x.Fecha).HasColumnName("fecha");
            e.Property(x => x.TextoConsulta).HasColumnName("texto_consulta");
            e.Property(x => x.FuncionInvocada).HasColumnName("funcion_invocada");
            e.Property(x => x.Parametros).HasColumnName("parametros");
            e.Property(x => x.Interprete).HasColumnName("interprete");
            e.Property(x => x.Exitosa).HasColumnName("exitosa");
            e.Property(x => x.DuracionMs).HasColumnName("duracion_ms");
        });
    }
}
