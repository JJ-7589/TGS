using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Caninos.Api.Dtos.Auth;
using Caninos.Api.Dtos.Consultas;
using Caninos.Api.Dtos.Mascotas;
using Caninos.Api.Dtos.Usuarios;
using Microsoft.Data.Sqlite;

namespace Caninos.Api.Tests;

public sealed class RolesProfileAndPetsMediaTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RolesProfileAndPetsMediaTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, UsuarioResponse User)> RegisterAndCreateUserClientAsync(
        string? tag = null,
        string password = "12345")
    {
        tag ??= TestHelpers.UniqueTag();
        var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/auth/register", new
        {
            username = $"std_{tag}",
            password,
            documento = $"STD{tag}",
            nombreCompleto = $"Usuario Estandar {tag}",
            telefono = "3105559999",
            email = $"std_{tag}@test.local",
            direccion = $"Calle {tag} # 1-2",
            rol = "admin" // Intento de escalación de privilegios: debe ser ignorado
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal("usuario", auth.Usuario.Rol);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return (client, auth.Usuario);
    }

    [Fact]
    public async Task PermissionsMiddleware_RegularUserCannotAccessAdminOrOtherUsersResources()
    {
        var adminClient = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var (user1Client, user1) = await RegisterAndCreateUserClientAsync();
        var (user2Client, user2) = await RegisterAndCreateUserClientAsync();

        var pet1 = await TestHelpers.CreateSampleMascotaAsync(user1Client, user1.Id, "Firulais");
        var pet2 = await TestHelpers.CreateSampleMascotaAsync(user2Client, user2.Id, "Canela");

        var future = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var con1 = await TestHelpers.CreateSampleConsultaAsync(user1Client, user1.Id, pet1.Id, "Salud", "Programada", future);
        var con2 = await TestHelpers.CreateSampleConsultaAsync(user2Client, user2.Id, pet2.Id, "Salud", "Programada", future);

        // 1. Endpoints solo-admin en /api/usuarios -> 403 Forbidden
        await TestHelpers.AssertErrorAsync(await user1Client.GetAsync("/api/usuarios"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.PostAsJsonAsync("/api/usuarios", new
        {
            username = "otro_nuevo",
            password = "12345",
            documento = "999999",
            nombreCompleto = "Otro",
            telefono = "3001234567",
            email = "otro@test.com",
            direccion = "Calle 123"
        }), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.DeleteAsync($"/api/usuarios/{user1.Id}"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.DeleteAsync($"/api/usuarios/{user2.Id}"), HttpStatusCode.Forbidden);

        // 2. Acceso a otro usuario y escalación de rol -> 403 Forbidden
        await TestHelpers.AssertErrorAsync(await user1Client.GetAsync($"/api/usuarios/{user2.Id}"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.GetAsync($"/api/usuarios/{user2.Id}/mascotas"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.PutAsJsonAsync($"/api/usuarios/{user1.Id}", new
        {
            username = user1.Username,
            documento = user1.Documento,
            nombreCompleto = user1.NombreCompleto,
            telefono = user1.Telefono,
            email = user1.Email,
            direccion = user1.Direccion,
            rol = "admin"
        }), HttpStatusCode.Forbidden);

        // 3. Mascotas: usuario solo ve las suyas; acceso a mascotas ajenas -> 403
        var ownPets = await user1Client.GetFromJsonAsync<List<MascotaResponse>>("/api/mascotas", TestHelpers.JsonOptions);
        Assert.Single(ownPets!);
        Assert.Equal(pet1.Id, ownPets![0].Id);

        await TestHelpers.AssertErrorAsync(await user1Client.GetAsync($"/api/mascotas?userId={user2.Id}"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.GetAsync($"/api/mascotas/{pet2.Id}"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.GetAsync($"/api/mascotas/{pet2.Id}/historial"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.DeleteAsync($"/api/mascotas/{pet2.Id}"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = "Ajeno",
            raza = "Beagle",
            edad = 2,
            pesoKg = 10,
            sexo = "Macho",
            color = "Café",
            esterilizado = false,
            userId = user2.Id
        }), HttpStatusCode.Forbidden);

        // 4. Consultas: usuario solo ve las suyas; acceso a consultas ajenas -> 403
        var ownConsultas = await user1Client.GetFromJsonAsync<List<ConsultaResponse>>("/api/consultas", TestHelpers.JsonOptions);
        Assert.Single(ownConsultas!);
        Assert.Equal(con1.Id, ownConsultas![0].Id);

        var ownByService = await user1Client.GetFromJsonAsync<List<ConsultaResponse>>("/api/consultas/servicio/Salud", TestHelpers.JsonOptions);
        Assert.Single(ownByService!);
        Assert.Equal(con1.Id, ownByService![0].Id);

        await TestHelpers.AssertErrorAsync(await user1Client.GetAsync($"/api/consultas?userId={user2.Id}"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.GetAsync($"/api/consultas?mascotaId={pet2.Id}"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.GetAsync($"/api/consultas/usuario/{user2.Id}"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.GetAsync($"/api/consultas/mascota/{pet2.Id}"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.GetAsync($"/api/consultas/{con2.Id}"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.DeleteAsync($"/api/consultas/{con2.Id}"), HttpStatusCode.Forbidden);
        await TestHelpers.AssertErrorAsync(await user1Client.PostAsJsonAsync("/api/consultas", new
        {
            userId = user2.Id,
            mascotaId = pet2.Id,
            tipoServicio = "Salud",
            descripcion = "No autorizado",
            fechaConsulta = future,
            costo = 10000
        }), HttpStatusCode.Forbidden);

        // 5. Admin sí puede ver todos los usuarios, mascotas y consultas
        var allUsers = await adminClient.GetFromJsonAsync<List<UsuarioResponse>>("/api/usuarios", TestHelpers.JsonOptions);
        Assert.Contains(allUsers!, u => u.Id == user1.Id);
        Assert.Contains(allUsers!, u => u.Id == user2.Id);
    }

    [Fact]
    public async Task Admin_CanCreateAndAssignAdminRole_AndInvalidRoleReturns400()
    {
        var adminClient = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var tag = TestHelpers.UniqueTag();

        // Rol inválido -> 400
        var badRole = await adminClient.PostAsJsonAsync("/api/usuarios", new
        {
            username = $"badrol_{tag}",
            password = "12345",
            documento = $"BR{tag}",
            nombreCompleto = "Rol Malo",
            telefono = "3001234567",
            email = $"br_{tag}@test.local",
            direccion = "Calle 123",
            rol = "superadmin"
        });
        await TestHelpers.AssertErrorAsync(badRole, HttpStatusCode.BadRequest);

        // Admin crea otro admin -> 201
        var createAdmin = await adminClient.PostAsJsonAsync("/api/usuarios", new
        {
            username = $"adm_{tag}",
            password = "54321",
            documento = $"AD{tag}",
            nombreCompleto = "Segundo Admin",
            telefono = "3001234567",
            email = $"adm_{tag}@test.local",
            direccion = "Calle Admin 10",
            rol = "admin"
        });
        Assert.Equal(HttpStatusCode.Created, createAdmin.StatusCode);
        var created = (await createAdmin.Content.ReadFromJsonAsync<UsuarioResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal("admin", created.Rol);
    }

    [Fact]
    public async Task Perfil_UpdateOwnDataAndChangePassword_WorksAndValidatesEdgeCases()
    {
        var (client, user) = await RegisterAndCreateUserClientAsync(password: "12345");
        var anon = _factory.CreateClient();

        // GET /api/perfil
        var me = await client.GetFromJsonAsync<UsuarioResponse>("/api/perfil", TestHelpers.JsonOptions);
        Assert.Equal(user.Id, me!.Id);
        Assert.Equal("usuario", me.Rol);

        // PUT /api/perfil
        var updRes = await client.PutAsJsonAsync("/api/perfil", new
        {
            username = user.Username,
            documento = user.Documento,
            nombreCompleto = "Nombre Propio Actualizado",
            telefono = "3157778899",
            email = user.Email,
            direccion = "Nueva Dirección de Perfil 99"
        });
        Assert.Equal(HttpStatusCode.OK, updRes.StatusCode);
        var updated = (await updRes.Content.ReadFromJsonAsync<UsuarioResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal("Nombre Propio Actualizado", updated.NombreCompleto);
        Assert.Equal("usuario", updated.Rol);

        // PUT /api/perfil/password -> casos inválidos (400)
        // Contraseña actual incorrecta
        await TestHelpers.AssertErrorAsync(await client.PutAsJsonAsync("/api/perfil/password", new
        {
            passwordActual = "99999",
            passwordNueva = "54321",
            confirmarPassword = "54321"
        }), HttpStatusCode.BadRequest);

        // Confirmación no coincide
        await TestHelpers.AssertErrorAsync(await client.PutAsJsonAsync("/api/perfil/password", new
        {
            passwordActual = "12345",
            passwordNueva = "54321",
            confirmarPassword = "11111"
        }), HttpStatusCode.BadRequest);

        // Igual a la actual
        await TestHelpers.AssertErrorAsync(await client.PutAsJsonAsync("/api/perfil/password", new
        {
            passwordActual = "12345",
            passwordNueva = "12345",
            confirmarPassword = "12345"
        }), HttpStatusCode.BadRequest);

        // No numérica
        await TestHelpers.AssertErrorAsync(await client.PutAsJsonAsync("/api/perfil/password", new
        {
            passwordActual = "12345",
            passwordNueva = "abcde",
            confirmarPassword = "abcde"
        }), HttpStatusCode.BadRequest);

        // Cambio válido -> 200
        var pwdOk = await client.PutAsJsonAsync("/api/perfil/password", new
        {
            passwordActual = "12345",
            passwordNueva = "67890",
            confirmarPassword = "67890"
        });
        Assert.Equal(HttpStatusCode.OK, pwdOk.StatusCode);

        // Verificar que la vieja falla (401) y la nueva funciona (200)
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password = "12345" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password = "67890" })).StatusCode);
    }

    [Fact]
    public async Task Consultas_CannotScheduleBeforeCurrentDateTime_AndAllowsCompletingExisting()
    {
        var (client, user) = await RegisterAndCreateUserClientAsync();
        var pet = await TestHelpers.CreateSampleMascotaAsync(client, user.Id, "Mateo");

        // 1. POST con fecha en el pasado -> 400 Bad Request
        var past1 = DateTime.UtcNow.AddMinutes(-15).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var past2 = DateTime.UtcNow.AddDays(-5).ToString("yyyy-MM-ddTHH:mm:ssZ");

        foreach (var pastDate in new[] { past1, past2 })
        {
            var res = await client.PostAsJsonAsync("/api/consultas", new
            {
                userId = user.Id,
                mascotaId = pet.Id,
                tipoServicio = "Salud",
                descripcion = "Intento de cita en el pasado",
                fechaConsulta = pastDate,
                costo = 30000
            });
            var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
            Assert.True(error.Errors.ContainsKey("fechaConsulta"));
        }

        // 2. POST con fecha futura -> 201 Created
        var futureDate = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var created = await TestHelpers.CreateSampleConsultaAsync(client, user.Id, pet.Id, "Salud", "Programada", futureDate);

        // 3. PUT reprogramando como Programada en fecha pasada -> 400 Bad Request
        var badReschedule = await client.PutAsJsonAsync($"/api/consultas/{created.Id}", new
        {
            userId = user.Id,
            mascotaId = pet.Id,
            tipoServicio = "Salud",
            descripcion = "Reprogramada al pasado",
            fechaConsulta = past1,
            costo = 30000,
            estado = "Programada"
        });
        await TestHelpers.AssertErrorAsync(badReschedule, HttpStatusCode.BadRequest);

        // 4. PUT marcando como Completada -> 200 OK
        var completedRes = await client.PutAsJsonAsync($"/api/consultas/{created.Id}", new
        {
            userId = user.Id,
            mascotaId = pet.Id,
            tipoServicio = "Salud",
            descripcion = "Cita ya finalizada",
            fechaConsulta = futureDate,
            costo = 30000,
            estado = "Completada"
        });
        Assert.Equal(HttpStatusCode.OK, completedRes.StatusCode);
    }

    [Fact]
    public async Task MascotaPhoto_ProximaCita_Historial_AndIndexedMascotaIdQuery_Work()
    {
        var (client, user) = await RegisterAndCreateUserClientAsync();

        // 1. Subir foto PNG válida
        byte[] validPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
        ];

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(validPng);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(fileContent, "file", "perro.png");

        var uploadRes = await client.PostAsync("/api/uploads/mascotas", multipart);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);
        using var uploadDoc = JsonDocument.Parse(await uploadRes.Content.ReadAsStringAsync());
        var fotoUrl = uploadDoc.RootElement.GetProperty("fotoUrl").GetString()!;
        Assert.StartsWith("/uploads/mascotas/", fotoUrl);

        // La foto subida es accesible por HTTP GET
        var getPhotoRes = await _factory.CreateClient().GetAsync(fotoUrl);
        Assert.Equal(HttpStatusCode.OK, getPhotoRes.StatusCode);

        // Archivo falso (texto con extensión .png) -> 400
        using var fakeMultipart = new MultipartFormDataContent();
        var fakeContent = new ByteArrayContent("no es una imagen"u8.ToArray());
        fakeContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        fakeMultipart.Add(fakeContent, "file", "falso.png");
        await TestHelpers.AssertErrorAsync(await client.PostAsync("/api/uploads/mascotas", fakeMultipart), HttpStatusCode.BadRequest);

        // URL peligrosa javascript: en fotoUrl -> 400
        await TestHelpers.AssertErrorAsync(await client.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = "Malicioso",
            raza = "Beagle",
            edad = 2,
            pesoKg = 10,
            sexo = "Macho",
            color = "Negro",
            esterilizado = false,
            fotoUrl = "javascript:alert(1)",
            userId = user.Id
        }), HttpStatusCode.BadRequest);

        // 2. Crear mascota con la foto subida
        var petRes = await client.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = "Coco",
            raza = "Border Collie",
            edad = 3,
            pesoKg = 18.5m,
            sexo = "Macho",
            color = "Blanco y Negro",
            esterilizado = true,
            fotoUrl,
            userId = user.Id
        });
        Assert.Equal(HttpStatusCode.Created, petRes.StatusCode);
        var pet = (await petRes.Content.ReadFromJsonAsync<MascotaResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal(fotoUrl, pet.FotoUrl);
        Assert.Null(pet.ProximaCita);
        Assert.Equal(0, pet.TotalHistorial);

        // 3. Crear 2 citas futuras (día +5 y día +2) y 1 completada (historial)
        var nearDate = DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var farDate = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var completedDate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ");

        await TestHelpers.CreateSampleConsultaAsync(client, user.Id, pet.Id, "Recreacion", "Programada", farDate, 25000);
        var nearest = await TestHelpers.CreateSampleConsultaAsync(client, user.Id, pet.Id, "Estetica", "Programada", nearDate, 45000);
        var done = await TestHelpers.CreateSampleConsultaAsync(client, user.Id, pet.Id, "Salud", "Completada", completedDate, 60000);

        // Verificar proximaCita (debe elegir la más cercana: nearest) y totalHistorial == 1
        var fetchedPet = await client.GetFromJsonAsync<MascotaResponse>($"/api/mascotas/{pet.Id}", TestHelpers.JsonOptions);
        Assert.NotNull(fetchedPet!.ProximaCita);
        Assert.Equal(nearest.Id, fetchedPet.ProximaCita!.Id);
        Assert.Equal("Estetica", fetchedPet.ProximaCita.TipoServicio);
        Assert.Equal(1, fetchedPet.TotalHistorial);

        // Verificar GET /api/mascotas/{id}/historial
        var historial = await client.GetFromJsonAsync<MascotaHistorialResponse>($"/api/mascotas/{pet.Id}/historial", TestHelpers.JsonOptions);
        Assert.NotNull(historial);
        Assert.Equal(pet.Id, historial!.MascotaId);
        Assert.Equal(fotoUrl, historial.FotoUrl);
        Assert.Equal(nearest.Id, historial.ProximaCita!.Id);
        Assert.Single(historial.CitasPrevias);
        Assert.Equal(done.Id, historial.CitasPrevias[0].Id);
        Assert.Equal(3, historial.TodasLasConsultas.Count);

        // 4. Verificar directamente en SQLite que existe el índice en mascota_id y que el planificador lo utiliza
        await using var conn = new SqliteConnection($"Data Source={_factory.DatabasePath};Foreign Keys=True");
        await conn.OpenAsync();

        await using (var idxCmd = conn.CreateCommand())
        {
            idxCmd.CommandText = "PRAGMA index_list(consultas);";
            var indexes = new List<string>();
            await using var reader = await idxCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexes.Add(reader.GetString(1));
            }
            Assert.Contains("ix_consultas_mascota_id", indexes);
            Assert.Contains("ix_consultas_mascota_id_fecha", indexes);
        }

        await using (var planCmd = conn.CreateCommand())
        {
            planCmd.CommandText = "EXPLAIN QUERY PLAN SELECT * FROM consultas WHERE mascota_id = 1 ORDER BY fecha_consulta DESC;";
            var planDetails = new List<string>();
            await using var reader = await planCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                planDetails.Add(reader.GetString(3));
            }
            Assert.Contains(planDetails, d => d.Contains("ix_consultas_mascota_id", StringComparison.OrdinalIgnoreCase));
        }
    }
}
