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

        CREATE TABLE IF NOT EXISTS consultas (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            mascota_id      INTEGER NOT NULL,
            user_id         INTEGER NOT NULL,
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
                FOREIGN KEY (user_id) REFERENCES usuarios(id) ON DELETE RESTRICT
        );

        CREATE INDEX IF NOT EXISTS ix_mascotas_user_id              ON mascotas(user_id);
        CREATE INDEX IF NOT EXISTS ix_consultas_mascota_id          ON consultas(mascota_id);
        CREATE INDEX IF NOT EXISTS ix_consultas_mascota_id_fecha    ON consultas(mascota_id, fecha_consulta DESC, estado);
        CREATE INDEX IF NOT EXISTS ix_consultas_user_id             ON consultas(user_id);
        CREATE INDEX IF NOT EXISTS ix_consultas_tipo_servicio       ON consultas(tipo_servicio);
        """;

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
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = schemaSql;
                await cmd.ExecuteNonQueryAsync();
            }

            // Migración segura para bases de datos creadas antes de rol / foto_url / índice compuesto
            await EnsureSchemaUpgradesAsync(conn);
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
    }

    private static async Task EnsureSchemaUpgradesAsync(DbConnection conn)
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
            CREATE INDEX IF NOT EXISTS ix_consultas_mascota_id ON consultas(mascota_id);
            CREATE INDEX IF NOT EXISTS ix_consultas_mascota_id_fecha ON consultas(mascota_id, fecha_consulta DESC, estado);
            """);
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
