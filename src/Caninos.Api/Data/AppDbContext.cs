using System.Globalization;
using Caninos.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Caninos.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Mascota> Mascotas => Set<Mascota>();
    public DbSet<Consulta> Consultas => Set<Consulta>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var utcDateTimeConverter = new ValueConverter<DateTime, string>(
            v => (v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime())
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            v => DateTime.SpecifyKind(
                DateTime.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                DateTimeKind.Utc));

        var decimalToRealConverter = new ValueConverter<decimal, double>(
            v => (double)v,
            v => Math.Round((decimal)v, 2, MidpointRounding.AwayFromZero));

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("usuarios", table =>
            {
                table.HasCheckConstraint("ck_usuarios_rol", "rol IN ('usuario', 'admin')");
            });
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(u => u.Username).HasColumnName("username").IsRequired().HasMaxLength(50).UseCollation("NOCASE");
            entity.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(u => u.Documento).HasColumnName("documento").IsRequired().HasMaxLength(20);
            entity.Property(u => u.NombreCompleto).HasColumnName("nombre_completo").IsRequired().HasMaxLength(120);
            entity.Property(u => u.Telefono).HasColumnName("telefono").IsRequired().HasMaxLength(20);
            entity.Property(u => u.Email).HasColumnName("email").IsRequired().HasMaxLength(150).UseCollation("NOCASE");
            entity.Property(u => u.Direccion).HasColumnName("direccion").IsRequired().HasMaxLength(200);
            entity.Property(u => u.Rol).HasColumnName("rol").IsRequired().HasMaxLength(20).HasDefaultValue("usuario");
            entity.Property(u => u.FechaRegistro).HasColumnName("fecha_registro").IsRequired().HasConversion(utcDateTimeConverter);

            entity.HasIndex(u => u.Username).IsUnique();
            entity.HasIndex(u => u.Documento).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Mascota>(entity =>
        {
            entity.ToTable("mascotas", table =>
            {
                table.HasCheckConstraint("ck_mascotas_edad", "edad >= 0 AND edad <= 30");
                table.HasCheckConstraint("ck_mascotas_peso_kg", "peso_kg >= 0.2 AND peso_kg <= 120.0");
                table.HasCheckConstraint("ck_mascotas_sexo", "sexo IN ('Macho', 'Hembra')");
                table.HasCheckConstraint("ck_mascotas_esterilizado", "esterilizado IN (0, 1)");
            });
            entity.HasKey(m => m.Id);

            entity.Property(m => m.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(m => m.Nombre).HasColumnName("nombre").IsRequired().HasMaxLength(80);
            entity.Property(m => m.Raza).HasColumnName("raza").IsRequired().HasMaxLength(80);
            entity.Property(m => m.Edad).HasColumnName("edad").IsRequired();
            entity.Property(m => m.PesoKg).HasColumnName("peso_kg").HasColumnType("REAL").IsRequired().HasConversion(decimalToRealConverter);
            entity.Property(m => m.Sexo).HasColumnName("sexo").IsRequired().HasMaxLength(10);
            entity.Property(m => m.Color).HasColumnName("color").IsRequired().HasMaxLength(50);
            entity.Property(m => m.Esterilizado).HasColumnName("esterilizado").IsRequired();
            entity.Property(m => m.Observaciones).HasColumnName("observaciones").HasMaxLength(500);
            entity.Property(m => m.FotoUrl).HasColumnName("foto_url").HasMaxLength(500);
            entity.Property(m => m.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(m => m.FechaRegistro).HasColumnName("fecha_registro").IsRequired().HasConversion(utcDateTimeConverter);

            entity.HasIndex(m => m.UserId).HasDatabaseName("ix_mascotas_user_id");

            entity.HasOne(m => m.Usuario)
                .WithMany(u => u.Mascotas)
                .HasForeignKey(m => m.UserId)
                .HasConstraintName("fk_mascotas_usuarios")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Consulta>(entity =>
        {
            entity.ToTable("consultas", table =>
            {
                table.HasCheckConstraint("ck_consultas_tipo_servicio", "tipo_servicio IN ('Salud', 'Estetica', 'Recreacion')");
                table.HasCheckConstraint("ck_consultas_costo", "costo >= 0 AND costo <= 100000000");
                table.HasCheckConstraint("ck_consultas_estado", "estado IN ('Programada', 'EnProceso', 'Completada', 'Cancelada')");
            });
            entity.HasKey(c => c.Id);

            entity.Property(c => c.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(c => c.MascotaId).HasColumnName("mascota_id").IsRequired();
            entity.Property(c => c.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(c => c.TipoServicio).HasColumnName("tipo_servicio").IsRequired().HasMaxLength(20);
            entity.Property(c => c.Descripcion).HasColumnName("descripcion").IsRequired().HasMaxLength(500);
            entity.Property(c => c.FechaConsulta).HasColumnName("fecha_consulta").IsRequired().HasConversion(utcDateTimeConverter);
            entity.Property(c => c.Costo).HasColumnName("costo").HasColumnType("REAL").IsRequired().HasConversion(decimalToRealConverter);
            entity.Property(c => c.Estado).HasColumnName("estado").IsRequired().HasMaxLength(20).HasDefaultValue("Programada");
            entity.Property(c => c.Observaciones).HasColumnName("observaciones").HasMaxLength(500);
            entity.Property(c => c.FechaCreacion).HasColumnName("fecha_creacion").IsRequired().HasConversion(utcDateTimeConverter);

            entity.HasIndex(c => c.MascotaId).HasDatabaseName("ix_consultas_mascota_id");
            entity.HasIndex(c => new { c.MascotaId, c.FechaConsulta, c.Estado }).HasDatabaseName("ix_consultas_mascota_id_fecha");
            entity.HasIndex(c => c.UserId).HasDatabaseName("ix_consultas_user_id");
            entity.HasIndex(c => c.TipoServicio).HasDatabaseName("ix_consultas_tipo_servicio");

            entity.HasOne(c => c.Mascota)
                .WithMany(m => m.Consultas)
                .HasForeignKey(c => c.MascotaId)
                .HasConstraintName("fk_consultas_mascotas")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.Usuario)
                .WithMany(u => u.Consultas)
                .HasForeignKey(c => c.UserId)
                .HasConstraintName("fk_consultas_usuarios")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
