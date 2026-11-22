# Sistema de Registro de Salud, Estética y Recreación de Caninos — Backend (.NET + SQLite)

Backend REST API desarrollado en **.NET 10 (ASP.NET Core)** con base de datos **SQLite** para el registro de clientes (`usuarios`), caninos (`mascotas`) y servicios de salud, estética y recreación (`consultas`).

## Documentación para el Frontend

Consulta el contrato completo de endpoints, tipos de datos, códigos HTTP, casos de extremo y ejemplos en JavaScript (`fetch`) en:

- **[`API_DOCS.md`](./API_DOCS.md)** — Guía completa de integración para el equipo de frontend.

## Estructura del Proyecto

```text
├── API_DOCS.md                        # Contrato de API y guía para frontend
├── Caninos.sln                        # Solución .NET
├── database/
│   └── schema.sql                     # Esquema SQLite (usuarios, mascotas, consultas + FKs e índices)
├── src/
│   └── Caninos.Api/
│       ├── Controllers/               # Health, Auth, Usuarios, Mascotas, Consultas
│       ├── Data/                      # AppDbContext, DbInitializer, SqliteForeignKeysInterceptor
│       ├── Dtos/                      # Contratos de entrada/salida y ApiErrorResponse
│       ├── Middleware/                # Manejo global de errores y respuestas HTTP uniformes
│       ├── Models/                    # Entidades Usuario, Mascota, Consulta
│       ├── Services/                  # Lógica de negocio y JWT (sin roles)
│       ├── Validation/                # Validaciones estrictas de dominio y JSON
│       └── Program.cs                 # Configuración del host, SQLite, JWT, CORS y Swagger
└── tests/
    └── Caninos.Api.Tests/             # Pruebas de integración end-to-end (xUnit + WebApplicationFactory)
```

## Cómo ejecutar el backend en local

### Requisitos
- [.NET 10 SDK](https://dotnet.microsoft.com/) (el archivo SQLite `storage/caninos.db` y sus tablas se crean automáticamente al iniciar).

### Comandos

```bash
# 1. Restaurar dependencias
dotnet restore Caninos.sln

# 2. Iniciar el servidor en http://localhost:5150
dotnet run --project src/Caninos.Api
```

- **URL Base:** `http://localhost:5150`
- **Swagger UI:** `http://localhost:5150/swagger`
- **Health check:** `GET http://localhost:5150/api/health`
- **Base de datos SQLite generada:** `storage/caninos.db` (relativa al directorio de ejecución)
- **Credenciales iniciales (usuario semilla):** `username`: `admin` | `password`: `12345`

### Ejecutar pruebas automatizadas

```bash
dotnet test tests/Caninos.Api.Tests/Caninos.Api.Tests.csproj
```

## Resumen de Arquitectura y Validaciones

- **Base de datos:** SQLite con `PRAGMA foreign_keys = ON` en cada conexión y restricciones `CHECK` / `UNIQUE` / `FOREIGN KEY ... ON DELETE RESTRICT`.
- **Relaciones:**
  - `usuarios` (1) ── (N) `mascotas` (`mascotas.user_id` → `usuarios.id`)
  - `mascotas` (1) ── (N) `consultas` (`consultas.mascota_id` → `mascotas.id`)
  - `consultas` (N) ── (1) `usuarios` (`consultas.user_id` → `usuarios.id`)
- **Autenticación sin roles:** JWT Bearer emitido por `POST /api/auth/login` o `POST /api/auth/register`. La contraseña se valida estrictamente como numérica de mínimo 5 dígitos (`^\d{5,20}$`) y se guarda hasheada con PBKDF2.
- **Consultas requeridas:**
  - Por ID del cliente: `GET /api/consultas/usuario/{userId}` o `GET /api/consultas?userId={userId}`
  - Por ID del canino: `GET /api/consultas/mascota/{mascotaId}` o `GET /api/consultas?mascotaId={mascotaId}`
  - Por tipo de servicio: `GET /api/consultas/servicio/{tipoServicio}` o `GET /api/consultas?tipoServicio={tipoServicio}`
