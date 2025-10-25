# 🏡 RealEstate.Tests — Pruebas Unitarias e Integración

![.NET](https://img.shields.io/badge/.NET-8.0-blue?logo=dotnet)
![Tests](https://img.shields.io/badge/Tests-Passing-brightgreen?logo=nunit)
![Coverage](https://img.shields.io/badge/Coverage-98%25-success?logo=coveralls)
![License](https://img.shields.io/badge/License-MIT-yellow.svg)

Este módulo contiene las **pruebas automatizadas** del backend **RealEstate.API**, desarrolladas con **.NET 8**, **NUnit**, **Moq**, y **FluentAssertions**.  
Las pruebas garantizan la estabilidad, fiabilidad y mantenibilidad del sistema de gestión inmobiliaria.

---

## 🧪 Estructura del Proyecto

```
RealEstate.Tests/
 │
 ├── Services/
 │   ├── JwtServiceTests.cs
 │   ├── PropertyServiceTests.cs
 │   └── UserServiceTests.cs
 ├── Controllers/
 │   ├── AuthControllerTests.cs
 │   └── PropertyControllerTests.cs
 ├── Mocks/
 │   └── FakeMongoCollection.cs
 └── Helpers/
     └── TestHelper.cs
```

---

## 🔍 Descripción de Pruebas

### 🔐 AuthControllerTests.cs

Validan el proceso de **autenticación y registro** de usuarios mediante mocks de `UserService` y `JwtService`.

| Escenario | Método | Descripción | Resultado esperado |
|------------|---------|-------------|--------------------|
| ✅ Registro exitoso | `Register_ShouldReturnOk_WhenNewUser` | Crea un nuevo usuario si el correo no existe. | `200 OK` |
| 🚫 Usuario duplicado | `Register_ShouldReturnBadRequest_WhenUserExists` | Detecta correo ya registrado. | `400 BadRequest` |
| ✅ Login correcto | `Login_ShouldReturnOkWithToken_WhenCredentialsAreValid` | Autentica y genera JWT. | `200 OK` |
| 🚫 Usuario no existe | `Login_ShouldReturnUnauthorized_WhenUserNotFound` | Maneja login con correo no registrado. | `401 Unauthorized` |
| 🚫 Contraseña inválida | `Login_ShouldReturnUnauthorized_WhenPasswordInvalid` | Contraseña incorrecta. | `401 Unauthorized` |

---

### 🏠 PropertyServiceTests.cs

Pruebas unitarias para la lógica **CRUD** de propiedades.  
Usa `FakeMongoCollection<T>` para simular operaciones MongoDB.

| Escenario | Método | Descripción | Resultado esperado |
|------------|---------|-------------|--------------------|
| ✅ Crear propiedad | `CreateAsync_ShouldAddProperty` | Inserta nueva propiedad. | Propiedad agregada |
| ✅ Obtener todas | `GetAllAsync_ShouldReturnAllProperties` | Retorna todas las propiedades. | Lista completa |
| ✅ Buscar por ID | `GetByIdAsync_ShouldReturnProperty` | Busca propiedad por ID. | Propiedad encontrada |
| 🚫 No encontrada | `GetByIdAsync_ShouldReturnNull_WhenNotExists` | ID inexistente. | `null` |
| ✅ Actualizar | `UpdateAsync_ShouldModifyExistingProperty` | Modifica campos existentes. | Actualización correcta |
| ✅ Eliminar | `DeleteAsync_ShouldRemoveProperty` | Elimina propiedad específica. | Propiedad removida |

---

### 🌐 PropertyControllerTests.cs

Validan las rutas y respuestas HTTP del **PropertyController**, simulando peticiones reales.

| Escenario | Método | Descripción | Resultado esperado |
|------------|---------|-------------|--------------------|
| ✅ GET /api/property | `GetAll_ShouldReturnOkResult` | Devuelve lista de propiedades. | `200 OK` |
| ✅ GET /api/property/{id} | `GetById_ShouldReturnOk_WhenExists` | Devuelve propiedad específica. | `200 OK` |
| 🚫 No encontrada | `GetById_ShouldReturnNotFound_WhenMissing` | ID inexistente. | `404 NotFound` |
| ✅ POST /api/property | `Create_ShouldReturnCreatedAtAction` | Crea nueva propiedad. | `201 Created` |
| ✅ PUT /api/property/{id} | `Update_ShouldReturnNoContent_WhenSuccess` | Actualiza propiedad existente. | `204 NoContent` |
| 🚫 PUT inválido | `Update_ShouldReturnNotFound_WhenMissing` | Actualizar inexistente. | `404 NotFound` |
| ✅ DELETE /api/property/{id} | `Delete_ShouldReturnNoContent` | Elimina propiedad. | `204 NoContent` |

---

### 🔑 JwtServiceTests.cs

Verifica la generación, validación y expiración de tokens JWT.

| Escenario | Método | Descripción | Resultado esperado |
|------------|---------|-------------|--------------------|
| ✅ Generar token válido | `GenerateToken_ShouldReturnJwtString` | Crea un token con los claims del usuario. | Token no nulo y con formato válido |
| ✅ Validar token correcto | `ValidateToken_ShouldReturnPrincipal_WhenValid` | Decodifica y valida un JWT válido. | `ClaimsPrincipal` válido |
| 🚫 Token expirado | `ValidateToken_ShouldThrow_WhenExpired` | Detecta expiración y lanza excepción. | `SecurityTokenExpiredException` |
| 🚫 Token inválido | `ValidateToken_ShouldReturnNull_WhenCorrupted` | Maneja tokens manipulados. | `null` |

---

### 👤 UserServiceTests.cs

Pruebas unitarias de la lógica de usuarios (registro, login, hashing de contraseñas).

| Escenario | Método | Descripción | Resultado esperado |
|------------|---------|-------------|--------------------|
| ✅ Registrar nuevo usuario | `RegisterAsync_ShouldInsertUser_WhenNotExists` | Inserta usuario con contraseña encriptada. | Inserción exitosa |
| 🚫 Usuario ya existe | `RegisterAsync_ShouldThrow_WhenEmailTaken` | Evita duplicados. | Excepción lanzada |
| ✅ Validar credenciales correctas | `ValidateCredentials_ShouldReturnUser_WhenValid` | Autentica con email y contraseña válidos. | Retorna usuario |
| 🚫 Credenciales incorrectas | `ValidateCredentials_ShouldReturnNull_WhenInvalid` | Contraseña incorrecta. | `null` |
| ✅ Obtener usuario por email | `GetByEmailAsync_ShouldReturnUser_WhenExists` | Busca usuario existente. | Usuario retornado |

---

## ⚙️ Ejecución de Pruebas

Ejecuta todas las pruebas:
```bash
dotnet test
```

Con reporte de cobertura:
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
```

Generar reporte HTML (requiere ReportGenerator):
```bash
dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator -reports:**/coverage.info -targetdir:coverage-report
```

Abrir reporte:
```
coverage-report/index.html
```

---

## 🧩 Dependencias

```bash
dotnet add RealEstate.Tests package NUnit
dotnet add RealEstate.Tests package Moq
dotnet add RealEstate.Tests package FluentAssertions
dotnet add RealEstate.Tests package Microsoft.Extensions.Caching.Memory
dotnet add RealEstate.Tests package MongoDB.Driver
dotnet add RealEstate.Tests package BCrypt.Net-Next
```

---

## 📈 Cobertura Esperada

| Módulo | Cobertura |
|--------|------------|
| AuthController | ✅ 100% |
| PropertyService | ✅ 100% |
| PropertyController | ✅ 90–100% |
| JwtService | ✅ 100% |
| UserService | ✅ 100% |

---

## 🚀 Próximos pasos

- 🔸 Integrar pruebas en CI/CD con **GitHub Actions**  
- 🔸 Añadir pruebas E2E con **Postman/Newman**  
- 🔸 Extender cobertura en el frontend (React + Jest + RTL)  
- 🔸 Automatizar reportes de cobertura con **Coveralls** o **Codecov**

---

🧠 Autor: **Jorge Luis Muñoz Pabón**  
📦 Proyecto principal: [RealEstate.API](https://github.com/jorgelmunozp/RealEstate.API)
