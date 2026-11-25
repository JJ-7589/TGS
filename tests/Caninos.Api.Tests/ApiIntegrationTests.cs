using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Caninos.Api.Dtos.Auth;
using Caninos.Api.Dtos.Common;
using Caninos.Api.Dtos.Consultas;
using Caninos.Api.Dtos.Mascotas;
using Caninos.Api.Dtos.Usuarios;
using Microsoft.Data.Sqlite;

namespace Caninos.Api.Tests;

public sealed class ApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> AuthenticateAsAdminAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "admin",
            password = "12345"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
        return auth.Token;
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        var token = await AuthenticateAsAdminAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<ApiErrorResponse> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<ApiErrorResponse>(body, JsonOptions);
        Assert.NotNull(error);
        Assert.Equal((int)response.StatusCode, error.Status);
        Assert.False(string.IsNullOrWhiteSpace(error.Error));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.NotNull(error.Errors);
        return error;
    }

    [Fact]
    public async Task Health_AndSwagger_WorkWithoutAuth()
    {
        var healthResponse = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
        var healthJson = await healthResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", healthJson);
        Assert.Contains("\"database\":\"sqlite\"", healthJson);

        var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);
        var swaggerJson = await swaggerResponse.Content.ReadAsStringAsync();
        Assert.Contains("/api/usuarios", swaggerJson);
        Assert.Contains("/api/mascotas", swaggerJson);
        Assert.Contains("/api/consultas", swaggerJson);
    }

    [Fact]
    public async Task ProtectedEndpoints_WithoutToken_Return401WithStandardEnvelope()
    {
        foreach (var path in new[] { "/api/usuarios", "/api/mascotas", "/api/consultas" })
        {
            var res = await _client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            await ReadErrorAsync(res);
        }
    }

    [Fact]
    public async Task Login_SeededAdmin_ReturnsJwtWithoutRolesAndWithoutPasswordHash()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "ADMIN",
            password = "12345"
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var raw = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);

        var auth = JsonSerializer.Deserialize<AuthResponse>(raw, JsonOptions)!;
        Assert.Equal("Bearer", auth.TokenType);
        Assert.Equal("admin", auth.Usuario.Username);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.Token);
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "admin");
        Assert.Equal("admin", auth.Usuario.Rol);
    }

    [Theory]
    [InlineData("""{"username":"admin","password":"1234"}""")]        // menor a 5 dígitos
    [InlineData("""{"username":"admin","password":"1234a"}""")]       // contiene letra
    [InlineData("""{"username":"admin","password":" 12345"}""")]      // espacio al inicio
    [InlineData("""{"username":"admin","password":"12345 "}""")]      // espacio al final
    [InlineData("""{"username":"admin","password":"-12345"}""")]      // signo negativo
    [InlineData("""{"username":"admin","password":"123.45"}""")]      // decimal
    [InlineData("""{"username":"admin","password":12345}""")]         // número JSON en vez de string
    [InlineData("""{"username":"admin","password":""}""")]            // vacío
    [InlineData("""{"username":"","password":"12345"}""")]            // usuario vacío
    public async Task Login_InvalidFormatOrDataTypes_Return400(string rawJson)
    {
        using var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        var res = await _client.PostAsync("/api/auth/login", content);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var error = await ReadErrorAsync(res);
        Assert.NotEmpty(error.Errors);
    }

    [Fact]
    public async Task Login_WrongCredentials_Return401()
    {
        var wrongPwd = await _client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "99999" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPwd.StatusCode);
        await ReadErrorAsync(wrongPwd);

        var unknownUser = await _client.PostAsJsonAsync("/api/auth/login", new { username = "desconocido", password = "12345" });
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        await ReadErrorAsync(unknownUser);
    }

    [Fact]
    public async Task Register_LeadingZeroNumericPassword_WorksAndAllowsLogin()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var registerRes = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            username = $"user_{suffix}",
            password = "00456",
            documento = $"DOC{suffix}",
            nombreCompleto = "Laura Méndez",
            telefono = "+57 310-555-0001",
            email = $"laura_{suffix}@test.com",
            direccion = "Avenida Siempre Viva 123"
        });
        Assert.Equal(HttpStatusCode.Created, registerRes.StatusCode);

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username = $"user_{suffix}",
            password = "00456"
        });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
    }

    [Fact]
    public async Task RequestProtocolEdgeCases_MalformedJson_WrongContentType_MethodNotAllowed_InvalidIds()
    {
        var client = await CreateAuthorizedClientAsync();

        // JSON malformado -> 400
        using (var malformed = new StringContent("{ \"username\": ", Encoding.UTF8, "application/json"))
        {
            var res = await client.PostAsync("/api/usuarios", malformed);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            await ReadErrorAsync(res);
        }

        // Body vacío -> 400
        using (var empty = new StringContent("", Encoding.UTF8, "application/json"))
        {
            var res = await client.PostAsync("/api/usuarios", empty);
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            await ReadErrorAsync(res);
        }

        // Content-Type inválido -> 415
        using (var plain = new StringContent("hola", Encoding.UTF8, "text/plain"))
        {
            var res = await client.PostAsync("/api/usuarios", plain);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, res.StatusCode);
            await ReadErrorAsync(res);
        }

        // Método HTTP no soportado -> 405
        using (var patchReq = new HttpRequestMessage(HttpMethod.Patch, "/api/usuarios/1")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        })
        {
            var res = await client.SendAsync(patchReq);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, res.StatusCode);
            await ReadErrorAsync(res);
        }

        // IDs inválidos en ruta -> 400 (no 404 ni 500)
        foreach (var badId in new[] { "abc", "0", "-5", "999999999999" })
        {
            var res = await client.GetAsync($"/api/usuarios/{badId}");
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            await ReadErrorAsync(res);
        }

        // ID inexistente -> 404
        var notFound = await client.GetAsync("/api/usuarios/999999");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        await ReadErrorAsync(notFound);
    }

    [Fact]
    public async Task Usuarios_FullCrud_Duplicates_AndOptionalPasswordOnUpdate()
    {
        var client = await CreateAuthorizedClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Create -> 201
        var createRes = await client.PostAsJsonAsync("/api/usuarios", new
        {
            username = $"Carlos.{suffix}",
            password = "54321",
            documento = $"1020{suffix}",
            nombreCompleto = "Carlos Gómez",
            telefono = "3105551234",
            email = $"Carlos.{suffix}@Email.com",
            direccion = "Calle 45 # 12-30"
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        Assert.NotNull(createRes.Headers.Location);
        var created = await createRes.Content.ReadFromJsonAsync<UsuarioResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal($"carlos.{suffix}", created.Username);
        Assert.Equal($"carlos.{suffix}@email.com", created.Email);

        // Duplicate username/documento/email (case-insensitive) -> 409
        var dupRes = await client.PostAsJsonAsync("/api/usuarios", new
        {
            username = $"CARLOS.{suffix}",
            password = "99999",
            documento = $"1020{suffix}",
            nombreCompleto = "Otro Carlos",
            telefono = "3105559999",
            email = $"CARLOS.{suffix}@EMAIL.COM",
            direccion = "Carrera 10 # 20-30"
        });
        Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);
        var dupErr = await ReadErrorAsync(dupRes);
        Assert.True(dupErr.Errors.ContainsKey("username"));
        Assert.True(dupErr.Errors.ContainsKey("documento"));
        Assert.True(dupErr.Errors.ContainsKey("email"));

        // Search -> 200
        var searchRes = await client.GetAsync($"/api/usuarios?search={suffix}");
        Assert.Equal(HttpStatusCode.OK, searchRes.StatusCode);
        var searchList = await searchRes.Content.ReadFromJsonAsync<List<UsuarioResponse>>(JsonOptions)!;
        Assert.Single(searchList!);

        // Update SIN password -> conserva password actual ("54321")
        var updateRes = await client.PutAsJsonAsync($"/api/usuarios/{created.Id}", new
        {
            username = created.Username,
            documento = created.Documento,
            nombreCompleto = "Carlos Gómez Actualizado",
            telefono = "3112223333",
            email = created.Email,
            direccion = "Nueva Dirección 456"
        });
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await updateRes.Content.ReadFromJsonAsync<UsuarioResponse>(JsonOptions)!;
        Assert.Equal("Carlos Gómez Actualizado", updated!.NombreCompleto);

        // Verificar que la contraseña anterior sigue funcionando
        var loginOld = await _client.PostAsJsonAsync("/api/auth/login", new { username = created.Username, password = "54321" });
        Assert.Equal(HttpStatusCode.OK, loginOld.StatusCode);

        // Update CON password inválido ("123") -> 400
        var badUpdate = await client.PutAsJsonAsync($"/api/usuarios/{created.Id}", new
        {
            username = created.Username,
            password = "123",
            documento = created.Documento,
            nombreCompleto = "Carlos Gómez Actualizado",
            telefono = "3112223333",
            email = created.Email,
            direccion = "Nueva Dirección 456"
        });
        Assert.Equal(HttpStatusCode.BadRequest, badUpdate.StatusCode);

        // Delete -> 204
        var delRes = await client.DeleteAsync($"/api/usuarios/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Verify deleted -> 404
        var afterDel = await client.GetAsync($"/api/usuarios/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDel.StatusCode);
    }

    [Fact]
    public async Task Mascotas_StrictDataTypes_AndCrud_Work()
    {
        var client = await CreateAuthorizedClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var ownerRes = await client.PostAsJsonAsync("/api/usuarios", new
        {
            username = $"owner_{suffix}",
            password = "11111",
            documento = $"OWN{suffix}",
            nombreCompleto = "Ana Torres",
            telefono = "3014445566",
            email = $"ana_{suffix}@test.com",
            direccion = "Calle 100 # 15-20"
        });
        var owner = (await ownerRes.Content.ReadFromJsonAsync<UsuarioResponse>(JsonOptions))!;

        // Errores estrictos de tipo y rangos -> 400
        var invalidPayloads = new[]
        {
            // edad como string
            $$"""{"nombre":"Max","raza":"Beagle","edad":"3","pesoKg":12.5,"sexo":"Macho","color":"Cafe","esterilizado":true,"userId":{{owner.Id}}}""",
            // edad decimal
            $$"""{"nombre":"Max","raza":"Beagle","edad":3.5,"pesoKg":12.5,"sexo":"Macho","color":"Cafe","esterilizado":true,"userId":{{owner.Id}}}""",
            // esterilizado como string
            $$"""{"nombre":"Max","raza":"Beagle","edad":3,"pesoKg":12.5,"sexo":"Macho","color":"Cafe","esterilizado":"true","userId":{{owner.Id}}}""",
            // faltan campos numéricos/booleanos obligatorios (no debe rellenar 0/false)
            $$"""{"nombre":"Max","raza":"Beagle","sexo":"Macho","color":"Cafe","userId":{{owner.Id}}}""",
            // peso con 3 decimales y sexo inválido
            $$"""{"nombre":"Max","raza":"Beagle","edad":3,"pesoKg":12.345,"sexo":"Otro","color":"Cafe","esterilizado":true,"userId":{{owner.Id}}}""",
            // edad negativa
            $$"""{"nombre":"Max","raza":"Beagle","edad":-1,"pesoKg":12.5,"sexo":"Macho","color":"Cafe","esterilizado":true,"userId":{{owner.Id}}}"""
        };

        foreach (var raw in invalidPayloads)
        {
            using var content = new StringContent(raw, Encoding.UTF8, "application/json");
            var badRes = await client.PostAsync("/api/mascotas", content);
            Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);
            await ReadErrorAsync(badRes);
        }

        // Propietario inexistente -> 404
        var missingOwner = await client.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = "Max",
            raza = "Beagle",
            edad = 3,
            pesoKg = 12.5,
            sexo = "Macho",
            color = "Tricolor",
            esterilizado = true,
            userId = 888888
        });
        Assert.Equal(HttpStatusCode.NotFound, missingOwner.StatusCode);

        // Create válido -> 201
        var createRes = await client.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = "Max",
            raza = "Golden Retriever",
            edad = 3,
            pesoKg = 28.5,
            sexo = "macho",
            color = "Dorado",
            esterilizado = true,
            observaciones = "Vacunas al día",
            userId = owner.Id
        });
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = (await createRes.Content.ReadFromJsonAsync<MascotaResponse>(JsonOptions))!;
        Assert.Equal("Macho", created.Sexo);
        Assert.Equal("Ana Torres", created.PropietarioNombre);

        // Filtro por userId (query y sub-ruta)
        var byQuery = await client.GetFromJsonAsync<List<MascotaResponse>>($"/api/mascotas?userId={owner.Id}", JsonOptions);
        Assert.Single(byQuery!);
        var byOwnerRoute = await client.GetFromJsonAsync<List<MascotaResponse>>($"/api/usuarios/{owner.Id}/mascotas", JsonOptions);
        Assert.Single(byOwnerRoute!);

        // Query inválida -> 400
        var badQuery = await client.GetAsync("/api/mascotas?userId=abc");
        Assert.Equal(HttpStatusCode.BadRequest, badQuery.StatusCode);

        // Update -> 200
        var updRes = await client.PutAsJsonAsync($"/api/mascotas/{created.Id}", new
        {
            nombre = "Maximus",
            raza = "Golden Retriever",
            edad = 4,
            pesoKg = 29.25,
            sexo = "Macho",
            color = "Dorado claro",
            esterilizado = true,
            observaciones = "Control anual OK",
            userId = owner.Id
        });
        Assert.Equal(HttpStatusCode.OK, updRes.StatusCode);
        var updated = (await updRes.Content.ReadFromJsonAsync<MascotaResponse>(JsonOptions))!;
        Assert.Equal("Maximus", updated.Nombre);
        Assert.Equal(29.25m, updated.PesoKg);

        // Delete -> 204
        var delRes = await client.DeleteAsync($"/api/mascotas/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);
    }

    [Fact]
    public async Task Consultas_AllCrud_ThreeRequiredQueries_AndRelationalIntegrity()
    {
        var client = await CreateAuthorizedClientAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Crear 2 clientes y 2 mascotas
        var u1 = (await (await client.PostAsJsonAsync("/api/usuarios", new
        {
            username = $"cli1_{suffix}",
            password = "12345",
            documento = $"C1{suffix}",
            nombreCompleto = "Pedro Ruiz",
            telefono = "3001112233",
            email = $"pedro_{suffix}@test.com",
            direccion = "Calle 1 # 10-20"
        })).Content.ReadFromJsonAsync<UsuarioResponse>(JsonOptions))!;

        var u2 = (await (await client.PostAsJsonAsync("/api/usuarios", new
        {
            username = $"cli2_{suffix}",
            password = "67890",
            documento = $"C2{suffix}",
            nombreCompleto = "Sofía Vargas",
            telefono = "3004445566",
            email = $"sofia_{suffix}@test.com",
            direccion = "Calle 2 # 30-40"
        })).Content.ReadFromJsonAsync<UsuarioResponse>(JsonOptions))!;

        var m1 = (await (await client.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = "Rocky",
            raza = "Bulldog",
            edad = 2,
            pesoKg = 18.0,
            sexo = "Macho",
            color = "Blanco y café",
            esterilizado = false,
            userId = u1.Id
        })).Content.ReadFromJsonAsync<MascotaResponse>(JsonOptions))!;

        var m2 = (await (await client.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = "Luna",
            raza = "Poodle",
            edad = 5,
            pesoKg = 7.5,
            sexo = "Hembra",
            color = "Blanco",
            esterilizado = true,
            userId = u2.Id
        })).Content.ReadFromJsonAsync<MascotaResponse>(JsonOptions))!;

        // Edge case relacional: mascota de u1 con userId u2 -> 409 Conflict
        var mismatchRes = await client.PostAsJsonAsync("/api/consultas", new
        {
            userId = u2.Id,
            mascotaId = m1.Id,
            tipoServicio = "Salud",
            descripcion = "Intento con dueño equivocado",
            fechaConsulta = "2099-05-15T10:00:00Z",
            costo = 50000
        });
        Assert.Equal(HttpStatusCode.Conflict, mismatchRes.StatusCode);
        await ReadErrorAsync(mismatchRes);

        // Edge cases de validación de consulta -> 400
        var badCosto = await client.PostAsJsonAsync("/api/consultas", new
        {
            userId = u1.Id,
            mascotaId = m1.Id,
            tipoServicio = "Salud",
            descripcion = "Costo negativo inválido",
            fechaConsulta = "2099-05-15T10:00:00Z",
            costo = -500
        });
        Assert.Equal(HttpStatusCode.BadRequest, badCosto.StatusCode);

        var badServicio = await client.PostAsJsonAsync("/api/consultas", new
        {
            userId = u1.Id,
            mascotaId = m1.Id,
            tipoServicio = "Cirugia",
            descripcion = "Servicio fuera de catálogo",
            fechaConsulta = "2099-05-15T10:00:00Z",
            costo = 50000
        });
        Assert.Equal(HttpStatusCode.BadRequest, badServicio.StatusCode);

        // Crear 3 consultas (Salud, Estética con tilde, Recreación con tilde)
        var c1Res = await client.PostAsJsonAsync("/api/consultas", new
        {
            userId = u1.Id,
            mascotaId = m1.Id,
            tipoServicio = "Salud",
            descripcion = "Vacunación anual y desparasitación",
            fechaConsulta = "2099-05-15T10:00:00Z",
            costo = 85000
        });
        Assert.Equal(HttpStatusCode.Created, c1Res.StatusCode);
        var c1 = (await c1Res.Content.ReadFromJsonAsync<ConsultaResponse>(JsonOptions))!;
        Assert.Equal("Salud", c1.TipoServicio);
        Assert.Equal("Programada", c1.Estado);
        Assert.Equal("Rocky", c1.MascotaNombre);
        Assert.Equal("Pedro Ruiz", c1.UsuarioNombre);

        var c2Res = await client.PostAsJsonAsync("/api/consultas", new
        {
            userId = u1.Id,
            mascotaId = m1.Id,
            tipoServicio = "Estética",
            descripcion = "Baño medicado y cepillado",
            fechaConsulta = "2099-05-16T14:00:00Z",
            costo = 45000.50,
            estado = "EnProceso"
        });
        Assert.Equal(HttpStatusCode.Created, c2Res.StatusCode);
        var c2 = (await c2Res.Content.ReadFromJsonAsync<ConsultaResponse>(JsonOptions))!;
        Assert.Equal("Estetica", c2.TipoServicio);
        Assert.Equal("EnProceso", c2.Estado);

        var c3Res = await client.PostAsJsonAsync("/api/consultas", new
        {
            userId = u2.Id,
            mascotaId = m2.Id,
            tipoServicio = "Recreación",
            descripcion = "Jornada de agilidad en parque",
            fechaConsulta = "2099-05-17T09:00:00Z",
            costo = 30000,
            estado = "Completada"
        });
        Assert.Equal(HttpStatusCode.Created, c3Res.StatusCode);
        var c3 = (await c3Res.Content.ReadFromJsonAsync<ConsultaResponse>(JsonOptions))!;
        Assert.Equal("Recreacion", c3.TipoServicio);

        // 1) Consulta por ID del cliente
        var byUser = await client.GetFromJsonAsync<List<ConsultaResponse>>($"/api/consultas/usuario/{u1.Id}", JsonOptions);
        Assert.Equal(2, byUser!.Count);
        Assert.All(byUser, c => Assert.Equal(u1.Id, c.UserId));

        // 2) Consulta por ID del canino
        var byDog = await client.GetFromJsonAsync<List<ConsultaResponse>>($"/api/consultas/mascota/{m2.Id}", JsonOptions);
        Assert.Single(byDog!);
        Assert.Equal(c3.Id, byDog![0].Id);

        // 3) Consulta por tipo de servicio (con y sin tilde)
        var byService = await client.GetFromJsonAsync<List<ConsultaResponse>>("/api/consultas/servicio/Estética", JsonOptions);
        Assert.Contains(byService!, c => c.Id == c2.Id);
        Assert.All(byService!, c => Assert.Equal("Estetica", c.TipoServicio));

        var badServiceRoute = await client.GetAsync("/api/consultas/servicio/Invalido");
        Assert.Equal(HttpStatusCode.BadRequest, badServiceRoute.StatusCode);

        // Filtro combinado en GET /api/consultas
        var combined = await client.GetFromJsonAsync<List<ConsultaResponse>>(
            $"/api/consultas?userId={u1.Id}&mascotaId={m1.Id}&tipoServicio=Salud&estado=Programada",
            JsonOptions);
        Assert.Single(combined!);
        Assert.Equal(c1.Id, combined![0].Id);

        // Integridad referencial: NO se puede cambiar dueño de mascota con consultas -> 409
        var transferBlocked = await client.PutAsJsonAsync($"/api/mascotas/{m1.Id}", new
        {
            nombre = m1.Nombre,
            raza = m1.Raza,
            edad = m1.Edad,
            pesoKg = m1.PesoKg,
            sexo = m1.Sexo,
            color = m1.Color,
            esterilizado = m1.Esterilizado,
            userId = u2.Id
        });
        Assert.Equal(HttpStatusCode.Conflict, transferBlocked.StatusCode);

        // Integridad referencial: NO se puede borrar mascota con consultas -> 409
        var delPetBlocked = await client.DeleteAsync($"/api/mascotas/{m1.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delPetBlocked.StatusCode);

        // Integridad referencial: NO se puede borrar usuario con mascotas/consultas -> 409
        var delUserBlocked = await client.DeleteAsync($"/api/usuarios/{u1.Id}");
        Assert.Equal(HttpStatusCode.Conflict, delUserBlocked.StatusCode);

        // Update consulta -> 200
        var updConsulta = await client.PutAsJsonAsync($"/api/consultas/{c1.Id}", new
        {
            userId = u1.Id,
            mascotaId = m1.Id,
            tipoServicio = "Salud",
            descripcion = "Vacunación completada con éxito",
            fechaConsulta = "2099-05-15T10:30:00Z",
            costo = 90000,
            estado = "Completada",
            observaciones = "Sin reacciones adversas"
        });
        Assert.Equal(HttpStatusCode.OK, updConsulta.StatusCode);
        var updatedC1 = (await updConsulta.Content.ReadFromJsonAsync<ConsultaResponse>(JsonOptions))!;
        Assert.Equal("Completada", updatedC1.Estado);
        Assert.Equal(90000m, updatedC1.Costo);

        // Delete consultas -> 204, luego permite borrar mascota y usuario en orden
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/consultas/{c1.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/consultas/{c2.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/mascotas/{m1.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/usuarios/{u1.Id}")).StatusCode);
    }

    [Fact]
    public async Task SqliteSchema_HasExactTablesColumnsAndEnforcesForeignKeysDirectly()
    {
        // Asegura que el host arrancó y ejecutó DbInitializer
        var health = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        await using var conn = new SqliteConnection($"Data Source={_factory.DatabasePath};Foreign Keys=True");
        await conn.OpenAsync();

        // Verifica que existen las 3 tablas exactas y ninguna tabla de roles
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            var tables = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
            Assert.Equal(new[] { "consultas", "mascotas", "operaciones", "usuarios" }, tables);
        }

        // Verifica FKs en consultas: mascota_id -> mascotas(id) y user_id -> usuarios(id)
        await using (var fkCmd = conn.CreateCommand())
        {
            fkCmd.CommandText = "PRAGMA foreign_key_list(consultas);";
            var fks = new List<(string From, string Table, string To)>();
            await using var reader = await fkCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                fks.Add((reader.GetString(3), reader.GetString(2), reader.GetString(4)));
            }
            Assert.Contains(("mascota_id", "mascotas", "id"), fks);
            Assert.Contains(("user_id", "usuarios", "id"), fks);
        }

        // Verifica que SQLite rechaza directamente un INSERT con FK inexistente
        await using var invalidInsert = conn.CreateCommand();
        invalidInsert.CommandText = """
            INSERT INTO consultas (mascota_id, user_id, tipo_servicio, descripcion, fecha_consulta, costo, estado, fecha_creacion)
            VALUES (99999, 99999, 'Salud', 'Prueba FK directa', '2026-11-25T10:00:00Z', 1000, 'Programada', '2026-11-22T10:00:00Z');
            """;

        var ex = await Assert.ThrowsAsync<SqliteException>(() => invalidInsert.ExecuteNonQueryAsync());
        Assert.Equal(19, ex.SqliteErrorCode); // SQLITE_CONSTRAINT
    }
}
