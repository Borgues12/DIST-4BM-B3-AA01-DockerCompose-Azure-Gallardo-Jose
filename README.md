# Sistema Distribuido de Gestión Clínica

**Asignatura:** Aplicaciones Distribuidas
**Tema:** Arquitectura distribuida segura y despliegue en Azure
**Modalidad:** Individual

---

## 1. Descripción general

Sistema clínico compuesto por microservicios independientes que se comunican mediante un **API Gateway** (enrutamiento) y **RabbitMQ** (comunicación asíncrona basada en eventos), con autenticación y autorización centralizadas mediante un servicio independiente **OAuthJWT**.

El microservicio de negocio (`api-pacientes`) ya **no emite** tokens JWT — esa responsabilidad se delegó completamente al servicio `OAuthJWT`, que es el único punto de autenticación del sistema. Los demás servicios solo **validan** el token recibido. La solución completa está desplegada en **Azure Container Apps**.

---

## 2. Arquitectura

El API Gateway es el único punto de entrada público del sistema y enruta cada petición según su prefijo: las rutas `/api/Auth/*` van hacia `OAuthJWT`, las rutas `/api/pacientes/*` hacia `api-pacientes`, y las rutas `/api/historiales/*` hacia `api-historiasClinicas`.

`OAuthJWT` es el único servicio que emite tokens; no tiene base de datos propia. `api-pacientes` valida ese token, gestiona el CRUD de pacientes y publica eventos hacia RabbitMQ cuando un paciente es creado o actualizado. `api-historiasClinicas` valida el mismo token, gestiona el CRUD de historiales y consume esos eventos de RabbitMQ para mantenerse sincronizado con `api-pacientes`, sin comunicación directa entre ambos microservicios.

### Componentes (5 requeridos por la guía)

| # | Componente | Responsabilidad |
| --- | --- | --- |
| 1 | `OAuthJWT` | Autenticación y emisión de tokens JWT |
| 2 | `api-pacientes` | CRUD de pacientes, valida JWT, publica eventos |
| 3 | `api-historiasClinicas` | CRUD de historiales, valida JWT, consume eventos |
| 4 | `API Gateway` | Enrutamiento único de entrada (YARP) |
| 5 | `RabbitMQ` | Broker de mensajería basada en eventos |

Todos los servicios corren desplegados en Azure Container Apps, dentro del mismo entorno gestionado (`env-clinico`), comunicándose entre sí por sus nombres internos de servicio.

---

## 3. Descripción de cada servicio

### 3.1 OAuthJWT

* Único servicio responsable de autenticar usuarios y generar tokens JWT.
* Usuarios manejados de forma hardcodeada (ver sección 7 — decisiones de diseño).
* Configura `Issuer`, `Audience`, `Key` y `ExpireMinutes`.
* No tiene base de datos ni protege endpoints propios; su única función es emitir tokens.
* Endpoint: `POST /api/Auth/login`

### 3.2 api-pacientes

* CRUD de pacientes (`GET`, `GET/{id}`, `POST`, `PUT`, `DELETE`).
* Protegido con `[Authorize]` (lectura) y `[Authorize(Roles = "Administrador")]` (creación, edición, eliminación).
* Valida el JWT emitido por `OAuthJWT` (mismo `Issuer`/`Audience`/`Key`, no genera tokens propios).
* Publica dos eventos a RabbitMQ:
  * `PacienteCreado` → cola `pacientes_creados_queue`
  * `PacienteActualizado` → cola `pacientes_actualizados_queue`

### 3.3 api-historiasClinicas

* CRUD de historiales clínicos, protegido igual que `api-pacientes`.
* Valida el mismo JWT compartido.
* `BackgroundService` (`RabbitMQConsumer`) con **dos listeners independientes**:
  * Consume `PacienteCreado` → crea automáticamente una historia clínica inicial (`HC-{año}-{idPaciente}`) si el paciente no tiene una.
  * Consume `PacienteActualizado` → registra el evento en el log de auditoría, **sin escribir en base de datos** (ver justificación en sección 7).

### 3.4 API Gateway

* Implementado con **YARP (Reverse Proxy)**.
* Enruta las peticiones externas hacia el servicio correspondiente:
  * `/api/Auth/*` → `OAuthJWT`
  * `/api/pacientes/*` → `api-pacientes`
  * `/api/historiales/*` → `api-historiasClinicas`
* Es el único punto de entrada expuesto para el cliente; los microservicios internos no deberían consumirse directamente en producción.

### 3.5 RabbitMQ

* Imagen `rabbitmq:4-management`.
* Desplegado como servicio interno dentro del entorno de Azure Container Apps (no accesible desde Internet), exponiendo públicamente solo su panel de administración.
* Dos colas activas: `pacientes_creados_queue` y `pacientes_actualizados_queue`.

---

## 4. Servicios desplegados en Azure

La arquitectura completa está desplegada en **Azure Container Apps**, región `eastus`, dentro del grupo de recursos `rg-clinico-gallardo`. El broker `rabbitmq` está configurado como interno por seguridad; solo su panel de administración es público.

> **Nota:** aunque cada microservicio tiene asignada una URL pública propia por Container Apps, todas las pruebas de consumo deben realizarse a través de la URL del **API Gateway**, que es el único punto de entrada previsto.

| Componente | Acceso público | URL de Azure Container Apps |
| --- | --- | --- |
| **API Gateway** | Sí | `https://gateway.jollystone-f8f3ed4e.eastus.azurecontainerapps.io` |
| OAuthJWT | Sí | `https://oauthjwt.jollystone-f8f3ed4e.eastus.azurecontainerapps.io` |
| api-pacientes | Sí | `https://api-pacientes.jollystone-f8f3ed4e.eastus.azurecontainerapps.io` |
| api-historiasclinicas | Sí | `https://api-historiasclinicas.jollystone-f8f3ed4e.eastus.azurecontainerapps.io` |
| **RabbitMQ (Broker AMQP)** | No (interno) | `https://rabbitmq.internal.jollystone-f8f3ed4e.eastus.azurecontainerapps.io` |
| RabbitMQ (Dashboard) | Sí | `https://rabbitmq-dashboard.jollystone-f8f3ed4e.eastus.azurecontainerapps.io` |

La persistencia usa dos bases de datos independientes en **Azure SQL** (`pacienteDatabase` e `historialDatabase`), alojadas en el mismo servidor lógico y accedidas mediante cadenas de conexión configuradas como variables de entorno en cada Container App (ver `CLAVES_AZURE_EJEMPLO.txt`).

---

## 5. Obtener un token JWT y usarlo (contra Azure)

### Paso 1 — Login

```http
POST https://gateway.jollystone-f8f3ed4e.eastus.azurecontainerapps.io/api/Auth/login
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

```http
GET https://gateway.jollystone-f8f3ed4e.eastus.azurecontainerapps.io/api/pacientes
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

Una petición sin token devuelve `401 Unauthorized`. Una petición con token válido pero rol insuficiente devuelve `403 Forbidden`.

### Endpoints principales (a través del Gateway)

| Acción | Método | Ruta |
| --- | --- | --- |
| Login | `POST` | `/api/Auth/login` |
| Listar pacientes | `GET` | `/api/pacientes` |
| Buscar paciente por ID | `GET` | `/api/pacientes/{id}` |
| Crear paciente | `POST` | `/api/pacientes/crear` |
| Listar historiales | `GET` | `/api/historiales` |

---

## 6. Comunicación basada en eventos (RabbitMQ)

`api-pacientes` publica dos tipos de eventos, y `api-historiasClinicas` los consume mediante dos listeners independientes dentro del mismo `RabbitMQConsumer`:

| Evento | Cola | Acción del consumidor |
| --- | --- | --- |
| `PacienteCreado` | `pacientes_creados_queue` | Crea automáticamente una historia clínica inicial para el nuevo paciente (si no existe). |
| `PacienteActualizado` | `pacientes_actualizados_queue` | Registra el evento en el log de auditoría del servicio. No modifica la base de datos. |

El modelo `HistorialClinico` solo almacena `IdPaciente` como referencia (clave foránea), sin duplicar nombre, cédula ni otros datos del paciente. Por eso las actualizaciones de pacientes solo dejan un rastro de auditoría, sin modificar registros del historial.

---

## 7. Decisiones de diseño

* **Usuarios hardcodeados en OAuthJWT:** para efectos académicos y simplicidad del laboratorio, los usuarios están hardcodeados directamente en `AuthController`. En un entorno productivo se usaría una base de datos con contraseñas hasheadas.
* **Seguridad en Azure:** las credenciales y cadenas de conexión a Azure SQL se manejan mediante variables de entorno en cada Container App. Ninguna clave sensible está versionada en este repositorio (ver `CLAVES_AZURE_EJEMPLO.txt`).

---

## 8. Detener y eliminar recursos de Azure (post-revisión)

Para evitar costos no deseados una vez finalizada la revisión académica, ejecutar en Azure CLI:

```bash
az group delete --name rg-clinico-gallardo --yes --no-wait
```

Esto elimina el grupo de recursos completo: Azure SQL, Container Registry y los 5 Container Apps del proyecto.

*(El historial completo de comandos usados para crear estos recursos está en `MEMORIA_COMANDOS_AZURE.txt`.)*