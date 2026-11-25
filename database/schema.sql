-- ============================================================================
-- Sistema para el registro de salud, estetica y recreacion de caninos
-- Motor: SQLite 3
-- Relaciones:
--   - usuarios (1) -> mascotas (N)      [mascotas.user_id -> usuarios.id]
--   - mascotas (1) -> consultas (N)     [consultas.mascota_id -> mascotas.id]
--   - consultas (N) -> usuarios (1)     [consultas.user_id -> usuarios.id]
--   - operaciones (1) -> consultas (N)  [consultas.operacion_id -> operaciones.id]
-- Roles soportados: 'usuario', 'admin'
-- ============================================================================

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
