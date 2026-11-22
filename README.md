# Sistema de Registro de Salud, Estética y Recreación de Caninos — Backend (.NET + SQLite)

Backend REST API desarrollado en **.NET (ASP.NET Core)** con base de datos **SQLite** para el caso de estudio del curso: registro de clientes (`usuarios`), caninos (`mascotas`) y servicios de salud, estética y recreación (`consultas`).

> **Nota de ramas del repositorio:**
> - **`main`**: Contiene el contrato oficial de la API y la guía de integración para frontend ([`API_DOCS.md`](./API_DOCS.md)).
> - **`feat/backend-api`**: Contiene el código fuente del backend en .NET, el esquema SQLite, las validaciones y las pruebas automatizadas.

## Documentación para el Frontend

Consulta el documento completo de endpoints, tipos de datos, códigos HTTP, casos de extremo y ejemplos en JavaScript (`fetch`) en:

- **[`API_DOCS.md`](./API_DOCS.md)** — Guía completa de integración para el equipo de frontend.

## Resumen de Arquitectura

- **Framework:** ASP.NET Core Web API (.NET)
- **Base de datos:** SQLite (`caninos.db`) con `PRAGMA foreign_keys = ON`
- **Autenticación:** JWT Bearer (`POST /api/auth/login` y `POST /api/auth/register`), **sin roles**
- **Tablas y relaciones:**
  - `usuarios` (1) ── (N) `mascotas` (`mascotas.user_id` → `usuarios.id`)
  - `mascotas` (1) ── (N) `consultas` (`consultas.mascota_id` → `mascotas.id`)
  - `consultas` (N) ── (1) `usuarios` (`consultas.user_id` → `usuarios.id`)
- **Módulos soportados:**
  1. Iniciar sesión (`username` + `password` numérico de mínimo 5 dígitos)
  2. CRUD de `usuarios` (clientes)
  3. CRUD de `mascotas` (caninos)
  4. CRUD y filtros de `consultas` (por `userId`, `mascotaId`, `tipoServicio` y `estado`)
