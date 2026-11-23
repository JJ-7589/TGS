# Sistema de Registro de Salud, Estética y Recreación de Caninos (.NET + SQLite + Frontend Web)

Sistema full-stack desarrollado en **.NET 10 (ASP.NET Core)** con base de datos **SQLite** y frontend web en **HTML + CSS + JavaScript (ES Modules)** para el registro de clientes (`usuarios`), caninos (`mascotas`) y servicios de salud, estética y recreación (`consultas`).

## Documentación de la API

Consulta el contrato completo de endpoints, tipos de datos, códigos HTTP, casos de extremo y ejemplos en JavaScript (`fetch`) en:

- **[`API_DOCS.md`](./API_DOCS.md)** — Guía completa de integración para el frontend.

## Estructura del Proyecto

```text
├── API_DOCS.md                        # Contrato de API y guía de endpoints
├── Caninos.sln                        # Solución .NET
├── database/
│   └── schema.sql                     # Esquema SQLite (usuarios, mascotas, consultas + FKs e índices)
├── frontend-caninos/
│   ├── index.html                     # Interfaz web (Login, Registro, Clientes, Caninos, Consultas)
│   ├── styles.css                     # Estilos de la aplicación
│   ├── apiClient.js                   # Cliente HTTP conectado a http://localhost:5150/api
│   └── app.js                         # Lógica de los módulos del frontend
├── src/
│   └── Caninos.Api/                   # Backend ASP.NET Core + SQLite + JWT + Swagger + archivos estáticos
└── tests/
    └── Caninos.Api.Tests/             # Batería de pruebas automatizadas (xUnit + WebApplicationFactory)
```

## Cómo emular / ejecutar el sistema completo en local

### Requisitos
- [.NET 10 SDK](https://dotnet.microsoft.com/) (no requiere Node.js ni servidores externos: el backend sirve automáticamente el frontend y crea la base de datos SQLite `storage/caninos.db`).

### Opción 1 (Recomendada): Todo en un solo comando

Desde la raíz del repositorio (`D:\TGS\TGS`):

```bash
dotnet run --project src/Caninos.Api
```

Una vez veas `Now listening on: http://localhost:5150`, abre tu navegador en:

| Recurso | URL |
| :--- | :--- |
| **Aplicación Web (redirige automáticamente a `/login`)** | `http://localhost:5150` → `http://localhost:5150/login` |
| **Panel principal (requiere iniciar sesión)** | `http://localhost:5150/dashboard` |
| **Documentación interactiva (Swagger UI)** | `http://localhost:5150/swagger` |
| **Estado del servidor y SQLite (Health)** | `http://localhost:5150/api/health` |

### Credenciales iniciales (usuario semilla)
- **Usuario:** `admin`
- **Contraseña numérica:** `12345`

*(También puedes crear nuevas cuentas desde el botón **"Regístrate aquí"** en la pantalla de inicio de sesión).*

### Opción 2 (Opcional): Ejecutar frontend en un puerto separado

Si prefieres levantar `frontend-caninos/` por separado (por ejemplo con la extensión **Live Server** de VS Code o `python -m http.server 5500 --directory frontend-caninos`):
1. Inicia primero el backend con `dotnet run --project src/Caninos.Api` (puerto `5150`).
2. Abre el frontend en `http://localhost:5500` (o `http://127.0.0.1:5500`). `apiClient.js` apunta automáticamente a `http://localhost:5150/api` y CORS ya está habilitado para `localhost` y `127.0.0.1`.
> No abras `index.html` con doble clic (`file://`), porque los navegadores bloquean módulos ES6 (`type="module"`) sin servidor HTTP.

## Ejecutar pruebas automatizadas

```bash
dotnet test tests/Caninos.Api.Tests/Caninos.Api.Tests.csproj -c Release
```

## Resumen de Arquitectura y Validaciones

- **Base de datos:** SQLite (`storage/caninos.db`) con `PRAGMA foreign_keys = ON` en cada conexión y restricciones `CHECK` / `UNIQUE` / `FOREIGN KEY ... ON DELETE RESTRICT`.
- **Relaciones:**
  - `usuarios` (1) ── (N) `mascotas` (`mascotas.user_id` → `usuarios.id`)
  - `mascotas` (1) ── (N) `consultas` (`consultas.mascota_id` → `mascotas.id`)
  - `consultas` (N) ── (1) `usuarios` (`consultas.user_id` → `usuarios.id`)
- **Autenticación sin roles:** JWT Bearer emitido por `POST /api/auth/login` o `POST /api/auth/register`. La contraseña se valida estrictamente como numérica de mínimo 5 dígitos (`^\d{5,20}$`) y se guarda hasheada con PBKDF2.
- **Consultas requeridas:**
  - Por ID del cliente: `GET /api/consultas/usuario/{userId}` o `GET /api/consultas?userId={userId}`
  - Por ID del canino: `GET /api/consultas/mascota/{mascotaId}` o `GET /api/consultas?mascotaId={mascotaId}`
  - Por tipo de servicio: `GET /api/consultas/servicio/{tipoServicio}` o `GET /api/consultas?tipoServicio={tipoServicio}`
