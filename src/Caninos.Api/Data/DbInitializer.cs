using System.Data.Common;
using Caninos.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Caninos.Api.Data;

public static class DbInitializer
{
    private const string FallbackSchemaSql = """
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS usuarios (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            username        TEXT    NOT NULL UNIQUE COLLATE NOCASE,
            password_hash   TEXT    NOT NULL,
            documento       TEXT    NOT NULL UNIQUE,
            nombre_completo TEXT    NOT NULL,
            telefono        TEXT    NOT NULL,
            email           TEXT    NOT NULL UNIQUE COLLATE NOCASE,
            direccion       TEXT    NOT NULL,
            rol             TEXT    NOT NULL DEFAULT 'usuario'
                            CHECK (rol IN ('usuario', 'admin')),
            fecha_registro  TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS mascotas (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            nombre          TEXT    NOT NULL,
            raza            TEXT    NOT NULL,
            edad            INTEGER NOT NULL CHECK (edad >= 0 AND edad <= 30),
            peso_kg         REAL    NOT NULL CHECK (peso_kg >= 0.2 AND peso_kg <= 120.0),
            sexo            TEXT    NOT NULL CHECK (sexo IN ('Macho', 'Hembra')),
            color           TEXT    NOT NULL,
            esterilizado    INTEGER NOT NULL CHECK (esterilizado IN (0, 1)),
            observaciones   TEXT    NULL,
            foto_url        TEXT    NULL,
            user_id         INTEGER NOT NULL,
            fecha_registro  TEXT    NOT NULL,
            CONSTRAINT fk_mascotas_usuarios
                FOREIGN KEY (user_id) REFERENCES usuarios(id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS operaciones (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            nombre          TEXT    NOT NULL UNIQUE COLLATE NOCASE,
            tipo_servicio   TEXT    NOT NULL CHECK (tipo_servicio IN ('Salud', 'Estetica', 'Recreacion')),
            descripcion     TEXT    NULL,
            costo           REAL    NOT NULL CHECK (costo >= 0 AND costo <= 100000000),
            activo          INTEGER NOT NULL DEFAULT 1 CHECK (activo IN (0, 1)),
            fecha_creacion  TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS consultas (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            mascota_id      INTEGER NOT NULL,
            user_id         INTEGER NOT NULL,
            operacion_id    INTEGER NULL,
            tipo_servicio   TEXT    NOT NULL CHECK (tipo_servicio IN ('Salud', 'Estetica', 'Recreacion')),
            descripcion     TEXT    NOT NULL,
            fecha_consulta  TEXT    NOT NULL,
            costo           REAL    NOT NULL CHECK (costo >= 0 AND costo <= 100000000),
            estado          TEXT    NOT NULL DEFAULT 'Programada'
                            CHECK (estado IN ('Programada', 'EnProceso', 'Completada', 'Cancelada')),
            observaciones   TEXT    NULL,
            fecha_creacion  TEXT    NOT NULL,
            CONSTRAINT fk_consultas_mascotas
                FOREIGN KEY (mascota_id) REFERENCES mascotas(id) ON DELETE RESTRICT,
            CONSTRAINT fk_consultas_usuarios
                FOREIGN KEY (user_id) REFERENCES usuarios(id) ON DELETE RESTRICT,
            CONSTRAINT fk_consultas_operaciones
                FOREIGN KEY (operacion_id) REFERENCES operaciones(id) ON DELETE RESTRICT
        );

        CREATE INDEX IF NOT EXISTS ix_mascotas_user_id              ON mascotas(user_id);
        CREATE INDEX IF NOT EXISTS ix_operaciones_tipo_servicio     ON operaciones(tipo_servicio, activo);
        CREATE INDEX IF NOT EXISTS ix_consultas_mascota_id          ON consultas(mascota_id);
        CREATE INDEX IF NOT EXISTS ix_consultas_mascota_id_fecha    ON consultas(mascota_id, fecha_consulta DESC, estado);
        CREATE INDEX IF NOT EXISTS ix_consultas_user_id             ON consultas(user_id);
        CREATE INDEX IF NOT EXISTS ix_consultas_operacion_id        ON consultas(operacion_id);
        CREATE INDEX IF NOT EXISTS ix_consultas_tipo_servicio       ON consultas(tipo_servicio);
        """;

    public static string ResolveConnectionString(string connectionString, string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(builder.DataSource) &&
            !builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) &&
            !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(Path.Combine(contentRootPath, builder.DataSource));
        }

        return builder.ToString();
    }

    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>();

        var connectionString = db.Database.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (!string.IsNullOrWhiteSpace(builder.DataSource) &&
                !builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = Path.GetFullPath(builder.DataSource);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
        }

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "database", "schema.sql");
        var schemaSql = File.Exists(schemaPath)
            ? await File.ReadAllTextAsync(schemaPath)
            : FallbackSchemaSql;

        await db.Database.OpenConnectionAsync();
        try
        {
            var conn = db.Database.GetDbConnection();
            await EnsureSchemaUpgradesBeforeDdlAsync(conn);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = schemaSql;
                await cmd.ExecuteNonQueryAsync();
            }

            await EnsureSchemaUpgradesAfterDdlAsync(conn);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        if (!await db.Usuarios.AnyAsync())
        {
            var admin = new Usuario
            {
                Username = "admin",
                Documento = "1000000000",
                NombreCompleto = "Administrador del Centro",
                Telefono = "3000000000",
                Email = "admin@centrocanino.local",
                Direccion = "Sede Principal",
                Rol = "admin",
                FechaRegistro = DateTime.UtcNow
            };
            admin.PasswordHash = passwordHasher.HashPassword(admin, "12345");

            db.Usuarios.Add(admin);
            await db.SaveChangesAsync();
        }
        else if (!await db.Usuarios.AnyAsync(u => u.Rol == "admin"))
        {
            var firstAdminCandidate = await db.Usuarios
                .FirstOrDefaultAsync(u => u.Username.ToLower() == "admin")
                ?? await db.Usuarios.OrderBy(u => u.Id).FirstAsync();

            firstAdminCandidate.Rol = "admin";
            await db.SaveChangesAsync();
        }

        // Semilla inicial del catálogo de operaciones/procedimientos con sus precios
        if (!await db.Operaciones.AnyAsync())
        {
            var now = DateTime.UtcNow;
            db.Operaciones.AddRange(
                new Operacion
                {
                    Nombre = "Consulta Veterinaria General",
                    TipoServicio = "Salud",
                    Descripcion = "Valoración clínica completa de signos vitales y estado general",
                    Costo = 60000m,
                    Activo = true,
                    FechaCreacion = now
                },
                new Operacion
                {
                    Nombre = "Vacunación Antirrábica y Polivalente",
                    TipoServicio = "Salud",
                    Descripcion = "Aplicación de esquema anual de vacunación canina",
                    Costo = 85000m,
                    Activo = true,
                    FechaCreacion = now
                },
                new Operacion
                {
                    Nombre = "Desparasitación Interna y Externa",
                    TipoServicio = "Salud",
                    Descripcion = "Control integral contra parásitos intestinales, pulgas y garrapatas",
                    Costo = 45000m,
                    Activo = true,
                    FechaCreacion = now
                },
                new Operacion
                {
                    Nombre = "Baño Medicado e Hipoalergénico",
                    TipoServicio = "Estetica",
                    Descripcion = "Baño dermatológico con secado, limpieza de oídos y perfume",
                    Costo = 50000m,
                    Activo = true,
                    FechaCreacion = now
                },
                new Operacion
                {
                    Nombre = "Peluquería Canina y Corte de Uñas",
                    TipoServicio = "Estetica",
                    Descripcion = "Corte higiénico y estético según raza más limado de uñas",
                    Costo = 65000m,
                    Activo = true,
                    FechaCreacion = now
                },
                new Operacion
                {
                    Nombre = "Spa Relajante e Higiene Dental",
                    TipoServicio = "Estetica",
                    Descripcion = "Hidratación de pelaje, masaje relajante y profilaxis básica",
                    Costo = 80000m,
                    Activo = true,
                    FechaCreacion = now
                },
                new Operacion
                {
                    Nombre = "Paseo Guiado y Socialización (1 hora)",
                    TipoServicio = "Recreacion",
                    Descripcion = "Caminata supervisada al aire libre con grupo reducido",
                    Costo = 30000m,
                    Activo = true,
                    FechaCreacion = now
                },
                new Operacion
                {
                    Nombre = "Día de Guardería y Recreación",
                    TipoServicio = "Recreacion",
                    Descripcion = "Jornada completa de juegos, enriquecimiento ambiental y descanso",
                    Costo = 55000m,
                    Activo = true,
                    FechaCreacion = now
                },
                new Operacion
                {
                    Nombre = "Circuito de Agilidad Canina",
                    TipoServicio = "Recreacion",
                    Descripcion = "Sesión guiada de obstáculos, ejercicio físico y obediencia lúdica",
                    Costo = 40000m,
                    Activo = true,
                    FechaCreacion = now
                }
            );
            await db.SaveChangesAsync();
        }
    }

    private static async Task EnsureSchemaUpgradesBeforeDdlAsync(DbConnection conn)
    {
        // Si la tabla consultas ya existía de versiones previas sin operacion_id, agregarla ANTES de crear ix_consultas_operacion_id
        await ExecuteNonQueryAsync(conn, """
            CREATE TABLE IF NOT EXISTS operaciones (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                nombre          TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                tipo_servicio   TEXT    NOT NULL CHECK (tipo_servicio IN ('Salud', 'Estetica', 'Recreacion')),
                descripcion     TEXT    NULL,
                costo           REAL    NOT NULL CHECK (costo >= 0 AND costo <= 100000000),
                activo          INTEGER NOT NULL DEFAULT 1 CHECK (activo IN (0, 1)),
                fecha_creacion  TEXT    NOT NULL
            );
            """);

        if (await TableExistsAsync(conn, "consultas") && !await ColumnExistsAsync(conn, "consultas", "operacion_id"))
        {
            await ExecuteNonQueryAsync(conn, """
                ALTER TABLE consultas ADD COLUMN operacion_id INTEGER NULL REFERENCES operaciones(id) ON DELETE RESTRICT;
                """);
        }
    }

    private static async Task EnsureSchemaUpgradesAfterDdlAsync(DbConnection conn)
    {
        if (!await ColumnExistsAsync(conn, "usuarios", "rol"))
        {
            await ExecuteNonQueryAsync(conn, """
                ALTER TABLE usuarios ADD COLUMN rol TEXT NOT NULL DEFAULT 'usuario' CHECK (rol IN ('usuario', 'admin'));
                UPDATE usuarios SET rol = 'admin' WHERE LOWER(username) = 'admin';
                """);
        }

        if (!await ColumnExistsAsync(conn, "mascotas", "foto_url"))
        {
            await ExecuteNonQueryAsync(conn, "ALTER TABLE mascotas ADD COLUMN foto_url TEXT NULL;");
        }

        await ExecuteNonQueryAsync(conn, """
            CREATE INDEX IF NOT EXISTS ix_operaciones_tipo_servicio  ON operaciones(tipo_servicio, activo);
            CREATE INDEX IF NOT EXISTS ix_consultas_mascota_id       ON consultas(mascota_id);
            CREATE INDEX IF NOT EXISTS ix_consultas_mascota_id_fecha ON consultas(mascota_id, fecha_consulta DESC, estado);
            CREATE INDEX IF NOT EXISTS ix_consultas_operacion_id     ON consultas(operacion_id);
            """);
    }

    private static async Task<bool> TableExistsAsync(DbConnection conn, string tableName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        var param = cmd.CreateParameter();
        param.ParameterName = "$name";
        param.Value = tableName;
        cmd.Parameters.Add(param);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    private static async Task<bool> ColumnExistsAsync(DbConnection conn, string tableName, string columnName)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task ExecuteNonQueryAsync(DbConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
