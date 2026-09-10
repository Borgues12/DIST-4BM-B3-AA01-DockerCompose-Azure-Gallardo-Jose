# Sistema Distribuido de Gestión Clínica

**Asignatura:** Aplicaciones Distribuidas
**Tema:** Arquitectura distribuida segura y despliegue en Azure
**Modalidad:** Individual

> ⚠️ Este README documenta el estado del proyecto hasta la validación completa en **Docker Compose local**. La sección de Azure se completará en la siguiente sesión de trabajo.

---

## 1. Descripción general

Sistema clínico compuesto por microservicios independientes que se comunican mediante un **API Gateway** (enrutamiento) y **RabbitMQ** (comunicación asíncrona basada en eventos), con autenticación y autorización centralizadas mediante un servicio independiente **OAuthJWT**.

El microservicio de negocio (`api-pacientes`) ya **no emite** tokens JWT — esa responsabilidad se delegó completamente al servicio `OAuthJWT`, que es el único punto de autenticación del sistema. Los demás servicios solo **validan** el token recibido.

---

## 2. Arquitectura

```
                         ┌──────────────────┐
                         │   API Gateway     │
                         │   (YARP - .NET)   │
                         │   puerto 5000      │
                         └─────────┬─────────┘
                    ┌──────────────┼──────────────┐
                    │              │              │
             /api/Auth/*   /api/pacientes/*  /api/historiales/*
                    │              │              │
                    ▼              ▼              ▼
            ┌───────────────┐ ┌───────────┐ ┌────────────────────┐
            │   OAuthJWT     │ │ Pacientes │ │ Historias Clínicas  │
            │  puerto 5003   │ │ puerto 5001│ │   puerto 5002       │
            └───────────────┘ └─────┬─────┘ └──────────┬──────────┘
                                    │                   │
                                    │   RabbitMQ        │
                                    │  (eventos)         │
                                    └────────►◄──────────┘
                                       puerto 5672 / 15672
```

### Componentes (5 requeridos por la guía)

| # | Componente | Responsabilidad | Puerto host |
|---|---|---|---|
| 1 | `OAuthJWT` | Autenticación y emisión de tokens JWT | 5003 |
| 2 | `api-pacientes` | CRUD de pacientes, valida JWT, publica eventos | 5001 |
| 3 | `api-historiasClinicas` | CRUD de historiales, valida JWT, consume eventos | 5002 |
| 4 | `API Gateway` | Enrutamiento único de entrada (YARP) | 5000 |
| 5 | `RabbitMQ` | Broker de mensajería basada en eventos | 5672 (AMQP) / 15672 (panel admin) |

Todos los servicios corren en la misma red interna de Docker (`distribuidos-network`) y se resuelven entre sí por **nombre de servicio** (no por IP ni `localhost`).

---

## 3. Descripción de cada servicio

### 3.1 OAuthJWT
- Único servicio responsable de autenticar usuarios y generar tokens JWT.
- Usuarios manejados de forma hardcodeada (ver sección 8 — decisiones de diseño).
- Configura `Issuer`, `Audience`, `Key` y `ExpireMinutes`.
- No tiene base de datos ni protege endpoints propios; su única función es emitir tokens.
- Endpoint: `POST /api/Auth/login`

### 3.2 api-pacientes
- CRUD de pacientes (`GET`, `GET/{id}`, `POST`, `PUT`, `DELETE`).
- Protegido con `[Authorize]` (lectura) y `[Authorize(Roles = "Administrador")]` (creación, edición, eliminación).
- Valida el JWT emitido por `OAuthJWT` (mismo `Issuer`/`Audience`/`Key`, no genera tokens propios).
- Publica dos eventos a RabbitMQ:
  - `PacienteCreado` → cola `pacientes_creados_queue`
  - `PacienteActualizado` → cola `pacientes_actualizados_queue`

### 3.3 api-historiasClinicas
- CRUD de historiales clínicos, protegido igual que `api-pacientes`.
- Valida el mismo JWT compartido.
- `BackgroundService` (`RabbitMQConsumer`) con **dos listeners independientes**:
  - Consume `PacienteCreado` → crea automáticamente una historia clínica inicial (`HC-{año}-{idPaciente}`) si el paciente no tiene una.
  - Consume `PacienteActualizado` → registra el evento en el log de auditoría, **sin escribir en base de datos** (ver justificación en sección 8).

### 3.4 API Gateway
- Implementado con **YARP (Reverse Proxy)**.
- Enruta las peticiones externas hacia el servicio correspondiente:
  - `/api/Auth/*` → `OAuthJWT`
  - `/api/pacientes/*` → `api-pacientes`
  - `/api/historiales/*` → `api-historiasClinicas`
- Es el único punto de entrada expuesto para el cliente; los microservicios internos no deberían consumirse directamente en producción.

### 3.5 RabbitMQ
- Imagen `rabbitmq:4-management`.
- Panel de administración disponible en `http://localhost:15672`.
- Dos colas activas: `pacientes_creados_queue` y `pacientes_actualizados_queue`.

---

## 4. Ejecutar el entorno local con Docker Compose

### Requisitos previos
- Docker Desktop instalado y corriendo.
- SQL Server accesible desde `host.docker.internal,1433` (local o instancia externa) con las bases `pacienteDatabase` e `historialDatabase` ya creadas (ver scripts DDL/DML).

### Levantar todo el stack

```bash
docker compose up --build
```

Esto construye y levanta los 5 servicios. Verificar que todos queden en estado `Up`:

```bash
docker ps
```

### Ver logs de un servicio específico (útil para depurar RabbitMQ)

```bash
docker logs historialB-api-compose -f
docker logs pacienteB-api-compose -f
```

### URLs locales (a través del Gateway, puerto 5000)

| Acción | Método | URL |
|---|---|---|
| Login | `POST` | `http://localhost:5000/api/Auth/login` |
| Listar pacientes | `GET` | `http://localhost:5000/api/pacientes` |
| Buscar paciente por ID | `GET` | `http://localhost:5000/api/pacientes/{id}` |
| Crear paciente | `POST` | `http://localhost:5000/api/pacientes/crear` |
| Actualizar paciente | `PUT` | `http://localhost:5000/api/pacientes/actualizar/{id}` |
| Eliminar paciente | `DELETE` | `http://localhost:5000/api/pacientes/{id}` |
| Listar historiales | `GET` | `http://localhost:5000/api/historiales/historiales` |
| Historiales por paciente | `GET` | `http://localhost:5000/api/historiales/paciente/{idPaciente}` |
| Crear historial | `POST` | `http://localhost:5000/api/historiales/crear` |
| Actualizar historial | `PUT` | `http://localhost:5000/api/historiales/actualizar/{id}` |
| Eliminar historial | `DELETE` | `http://localhost:5000/api/historiales/{id}` |

Panel de administración de RabbitMQ: `http://localhost:15672` (usuario/clave configurados en `docker-compose.yml`).

---

## 5. Obtener un token JWT y usarlo

### Paso 1 — Login

```
POST http://localhost:5000/api/Auth/login
Content-Type: application/json

{
  "usuario": "admin",
  "password": "<contraseña-configurada>"
}
```

Respuesta:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "usuario": "admin",
  "rol": "Administrador"
}
```

### Paso 2 — Usar el token en las siguientes peticiones

```
GET http://localhost:5000/api/pacientes
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

### Roles disponibles

| Usuario | Rol | Permisos |
|---|---|---|
| `admin` | Administrador | Lectura + Creación + Edición + Eliminación |
| `usuario` | Usuario | Solo lectura (endpoints `GET`) |

Una petición sin token devuelve `401 Unauthorized`. Una petición con token válido pero rol insuficiente devuelve `403 Forbidden`.

---

## 6. Comunicación basada en eventos (RabbitMQ)

`api-pacientes` publica dos tipos de eventos, y `api-historiasClinicas` los consume mediante dos listeners independientes dentro del mismo `RabbitMQConsumer`:

| Evento | Cola | Acción del consumidor |
|---|---|---|
| `PacienteCreado` | `pacientes_creados_queue` | Crea automáticamente una historia clínica inicial para el nuevo paciente (si no existe). |
| `PacienteActualizado` | `pacientes_actualizados_queue` | Registra el evento en el log de auditoría del servicio. No modifica la base de datos. |

### ¿Por qué el evento de actualización no escribe en la base de datos?

El modelo `HistorialClinico` solo almacena `IdPaciente` como referencia (clave foránea), sin duplicar nombre, cédula, ni otros datos del paciente. Como ese identificador es inmutable, no existe ningún campo que deba sincronizarse cuando el paciente se actualiza. Consumir el evento de todas formas demuestra el patrón de comunicación asíncrona entre microservicios y deja trazabilidad en los logs de la aplicación, evitando además duplicación innecesaria de datos entre servicios.

### Verificado en Docker Compose ✅

Prueba realizada localmente el 08/09/2026:
1. `PUT /api/pacientes/actualizar/{id}` a través del Gateway → `204 No Content`.
2. `pacienteB-api-compose` ejecuta el `UPDATE` en SQL Server.
3. RabbitMQ autentica y transporta el mensaje (conexión abierta y cerrada en ~13ms).
4. `historialB-api-compose` registra: `Evento PacienteActualizado recibido. IdPaciente: X - No se requiere acción sobre HistorialClinico`.
5. No se crea ni modifica ninguna fila en `tbl_historiales_clinicos`, confirmando el comportamiento esperado.

El flujo de creación (`PacienteCreado`) fue validado de forma equivalente: al crear un paciente, se genera automáticamente su historia clínica inicial con formato `HC-{año}-{idPaciente:D4}`.

---

## 7. Endpoints principales — resumen

### OAuthJWT
- `POST /api/Auth/login`

### api-pacientes
- `GET /api/pacientes` — `[Authorize]`
- `GET /api/pacientes/{id}` — `[Authorize]`
- `POST /api/pacientes/crear` — `[Authorize(Roles = "Administrador")]`
- `PUT /api/pacientes/actualizar/{id}` — `[Authorize(Roles = "Administrador")]`
- `DELETE /api/pacientes/{id}` — `[Authorize(Roles = "Administrador")]`

### api-historiasClinicas
- `GET /api/historiales/historiales` — `[Authorize]`
- `GET /api/historiales/paciente/{idPaciente}` — `[Authorize]`
- `POST /api/historiales/crear` — `[Authorize(Roles = "Administrador")]`
- `PUT /api/historiales/actualizar/{id}` — `[Authorize(Roles = "Administrador")]`
- `DELETE /api/historiales/{id}` — `[Authorize(Roles = "Administrador")]`

---

## 8. Decisiones de diseño

- **Usuarios hardcodeados en OAuthJWT:** el documento de la actividad no exige gestión de usuarios en base de datos, solo autenticación y emisión de JWT configurable (Issuer, Audience, Key, expiración). Para efectos académicos, los usuarios (`admin` / `usuario`) están hardcodeados directamente en `AuthController`. En un entorno productivo se recomendaría una tabla de usuarios con contraseñas hasheadas (bcrypt/Argon2).
- **Evento `PacienteActualizado` sin persistencia:** justificado en la sección 6 — el modelo `HistorialClinico` no depende de datos mutables del paciente.
- **Comunicación exclusiva a través del Gateway:** aunque cada microservicio expone su puerto individualmente en Docker Compose (para pruebas aisladas), la vía de acceso oficial del sistema es siempre el API Gateway (puerto 5000).

---

## 9. Servicios desplegados en Azure

> 🚧 **Pendiente** — se completará en la próxima sesión de trabajo.

| Servicio | URL pública |
|---|---|
| API Gateway | *(pendiente)* |
| OAuthJWT | *(pendiente)* |
| api-pacientes | *(pendiente)* |
| api-historiasClinicas | *(pendiente)* |

---

## 10. Detener y eliminar recursos de Azure (post-revisión)

> 🚧 **Pendiente** — se documentarán los comandos exactos de Azure CLI una vez completado el despliegue (ver también `MEMORIA_COMANDOS_AZURE.txt`).

Comando general de referencia (a confirmar):
```bash
az group delete --name <nombre-resource-group> --yes --no-wait
```

---

## 11. Detener el entorno local

```bash
docker compose down
```

Para eliminar también los volúmenes/imágenes generadas:
```bash
docker compose down -v --rmi local
```