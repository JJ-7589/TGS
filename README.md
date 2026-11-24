# Sistema de Registro de Salud, Estética y Recreación de Caninos (.NET + SQLite + Frontend Web)

Sistema full-stack desarrollado en **.NET 10 (ASP.NET Core)** con base de datos **SQLite** y frontend web en **HTML + CSS + JavaScript (ES Modules)** para el registro de clientes (`usuarios`), caninos (`mascotas`) y servicios de salud, estética y recreación (`consultas`).

## Documentación de la API

Consulta el contrato completo de endpoints, roles, tipos de datos, códigos HTTP, casos de extremo y ejemplos en JavaScript (`fetch`) en:

- **[`API_DOCS.md`](./API_DOCS.md)** — Guía completa de integración para el frontend.

## Novedades Principales

- **Roles (`usuario` y `admin`) + Middleware global de permisos (`PermissionsMiddleware`):**
  - **`admin`**: acceso completo a usuarios, mascotas, consultas y su perfil.
  - **`usuario`**: gestiona su propio perfil (`/perfil`), sus propias mascotas y sus propias consultas; cualquier intento de acceso a recursos ajenos o rutas de administrador responde `403 Forbidden`.
- **Página de Perfil (`/perfil`):** permite actualizar datos personales (`PUT /api/perfil`) y cambiar la contraseña numérica (`PUT /api/perfil/password`).
- **Tarjetas de Mascotas inteligentes con Foto, Próxima Cita e Historial:**
  - **1 mascota:** se muestra centrada en pantalla en vista destacada con foto, próxima cita y acceso al historial.
  - **Más de 1 mascota:** se muestra un **Grid View** con foto del animal, próxima cita y campo de historial; al hacer clic en cualquier tarjeta del grid, se abre centrada en pantalla con botón para volver a la cuadrícula.
  - **Fotos:** soporte para subir imagen real (`POST /api/uploads/mascotas`, JPG/PNG/WEBP máx. 5 MB) o pegar URL (`fotoUrl`).
  - **Historial indexado:** `GET /api/mascotas/{id}/historial` respaldado por índices `ix_consultas_mascota_id` e `ix_consultas_mascota_id_fecha` en SQLite para acelerar las cargas.
- **Validación de fecha de citas:** bloquea la programación de citas anteriores a la fecha y hora actual tanto en backend (`400 Bad Request`) como en frontend.

## Cómo emular / ejecutar el sistema completo en local

### Opción A — Sin instalar .NET (Ejecutable listo para Windows x64)
Abre la carpeta `publish-win/` y haz doble clic en:
- **[`publish-win/Iniciar-CentroCanino.bat`](./publish-win/Iniciar-CentroCanino.bat)** *(inicia el servidor y abre automáticamente `http://localhost:5150/login` en tu navegador)*, o directamente en `publish-win/Caninos.Api.exe`.

> El ejecutable es autocontenido (*self-contained*), no requiere instalar .NET, ni SQLite, ni Node.js. Al abrirlo por primera vez crea automáticamente `publish-win/storage/caninos.db` con el usuario administrador inicial (`admin` / `12345`).

### Opción B — Desde el código fuente (requiere .NET 10 SDK)

```powershell
# Si había una instancia previa corriendo, ciérrala primero:
Stop-Process -Name Caninos.Api -Force -ErrorAction SilentlyContinue

# Iniciar Backend + Frontend + SQLite
dotnet run --project src/Caninos.Api
```

| Recurso | URL |
| :--- | :--- |
| **Aplicación Web (redirige automáticamente a `/login`)** | `http://localhost:5150` → `http://localhost:5150/login` |
| **Panel principal (requiere iniciar sesión)** | `http://localhost:5150/dashboard` |
| **Mi Perfil y cambio de contraseña** | `http://localhost:5150/perfil` |
| **Documentación interactiva (Swagger UI)** | `http://localhost:5150/swagger` |
| **Estado del servidor y SQLite (Health)** | `http://localhost:5150/api/health` |

### Credenciales iniciales (usuario semilla)
- **Usuario:** `admin` (`rol: admin`)
- **Contraseña numérica:** `12345`

*(Al registrarte desde **"Regístrate aquí"**, se crea automáticamente una cuenta con rol `usuario` para probar el flujo de cliente con sus propias mascotas).*

## Ejecutar pruebas automatizadas

```powershell
dotnet test tests/Caninos.Api.Tests/Caninos.Api.Tests.csproj -c Release
```
