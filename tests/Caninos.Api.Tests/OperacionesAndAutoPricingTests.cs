using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Caninos.Api.Dtos.Auth;
using Caninos.Api.Dtos.Consultas;
using Caninos.Api.Dtos.Mascotas;
using Caninos.Api.Dtos.Operaciones;
using Caninos.Api.Dtos.Usuarios;

namespace Caninos.Api.Tests;

public sealed class OperacionesAndAutoPricingTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OperacionesAndAutoPricingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, UsuarioResponse User)> CreateRegularUserClientAsync()
    {
        var tag = TestHelpers.UniqueTag();
        var anon = _factory.CreateClient();
        var regRes = await anon.PostAsJsonAsync("/api/auth/register", new
        {
            username = $"cli_{tag}",
            password = "12345",
            documento = $"DC{tag}",
            nombreCompleto = $"Cliente {tag}",
            telefono = "3105551234",
            email = $"cli_{tag}@test.local",
            direccion = "Calle 10 # 20-30"
        });
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);
        var auth = (await regRes.Content.ReadFromJsonAsync<AuthResponse>(TestHelpers.JsonOptions))!;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return (client, auth.Usuario);
    }

    [Fact]
    public async Task SeededOperaciones_AvailableForUsers_AndOnlyAdminCanModifyPrices()
    {
        var adminClient = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var (userClient, _) = await CreateRegularUserClientAsync();

        // Usuario estándar puede listar y filtrar operaciones activas
        var allOps = await userClient.GetFromJsonAsync<List<OperacionResponse>>("/api/operaciones?soloActivas=true", TestHelpers.JsonOptions);
        Assert.NotNull(allOps);
        Assert.True(allOps!.Count >= 9);

        var saludOps = await userClient.GetFromJsonAsync<List<OperacionResponse>>("/api/operaciones?tipoServicio=Salud", TestHelpers.JsonOptions);
        Assert.NotEmpty(saludOps!);
        Assert.All(saludOps!, o => Assert.Equal("Salud", o.TipoServicio));

        // Usuario estándar NO puede crear, editar ni eliminar operaciones -> 403 Forbidden
        await TestHelpers.AssertErrorAsync(await userClient.PostAsJsonAsync("/api/operaciones", new
        {
            nombre = "Intento Usuario",
            tipoServicio = "Salud",
            costo = 10000
        }), HttpStatusCode.Forbidden);

        await TestHelpers.AssertErrorAsync(await userClient.PutAsJsonAsync($"/api/operaciones/{allOps[0].Id}", new
        {
            nombre = allOps[0].Nombre,
            tipoServicio = allOps[0].TipoServicio,
            costo = 1000
        }), HttpStatusCode.Forbidden);

        await TestHelpers.AssertErrorAsync(await userClient.DeleteAsync($"/api/operaciones/{allOps[0].Id}"), HttpStatusCode.Forbidden);

        // Query inválida -> 400 Bad Request
        await TestHelpers.AssertErrorAsync(await adminClient.GetAsync("/api/operaciones?soloActivas=invalido"), HttpStatusCode.BadRequest);
        await TestHelpers.AssertErrorAsync(await adminClient.GetAsync("/api/operaciones?tipoServicio=Invalido"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DynamicPriceUpdate_AutoCalculatesCostCategoryAndStatus_AndLinksToHistorial()
    {
        var adminClient = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var (userClient, user) = await CreateRegularUserClientAsync();
        var pet = await TestHelpers.CreateSampleMascotaAsync(userClient, user.Id, "Toby", "Schnauzer");

        // 1. Admin crea un nuevo procedimiento en la BD
        var tag = TestHelpers.UniqueTag();
        var createOpRes = await adminClient.PostAsJsonAsync("/api/operaciones", new
        {
            nombre = $"Limpieza Dental {tag}",
            tipoServicio = "Estética",
            descripcion = "Profilaxis con ultrasonido",
            costo = 72000m,
            activo = true
        });
        Assert.Equal(HttpStatusCode.Created, createOpRes.StatusCode);
        var op = (await createOpRes.Content.ReadFromJsonAsync<OperacionResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal("Estetica", op.TipoServicio);
        Assert.Equal(72000m, op.Costo);

        // 2. Usuario agenda cita enviando SOLO mascotaId, operacionId y fechaConsulta
        //    (incluso si intenta manipular costo=0 o estado="Completada", el backend impone el precio real y estado "Programada")
        var cita1Res = await userClient.PostAsJsonAsync("/api/consultas", new
        {
            mascotaId = pet.Id,
            operacionId = op.Id,
            fechaConsulta = TestHelpers.FutureIso(3),
            costo = 0m,
            estado = "Completada",
            observaciones = "Primera profilaxis"
        });
        Assert.Equal(HttpStatusCode.Created, cita1Res.StatusCode);
        var cita1 = (await cita1Res.Content.ReadFromJsonAsync<ConsultaResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal(op.Id, cita1.OperacionId);
        Assert.Equal(op.Nombre, cita1.OperacionNombre);
        Assert.Equal("Estetica", cita1.TipoServicio);
        Assert.Equal(72000m, cita1.Costo);
        Assert.Equal("Programada", cita1.Estado);
        Assert.Contains(op.Nombre, cita1.Descripcion);

        // 3. Admin actualiza dinámicamente el precio en la tabla operaciones a 95000
        var updOpRes = await adminClient.PutAsJsonAsync($"/api/operaciones/{op.Id}", new
        {
            nombre = op.Nombre,
            tipoServicio = "Estetica",
            descripcion = "Profilaxis con ultrasonido y pulido",
            costo = 95000m,
            activo = true
        });
        Assert.Equal(HttpStatusCode.OK, updOpRes.StatusCode);

        // 4. Nueva cita con esa misma operación toma automáticamente el nuevo precio 95000
        var cita2Res = await userClient.PostAsJsonAsync("/api/consultas", new
        {
            mascotaId = pet.Id,
            operacionId = op.Id,
            fechaConsulta = TestHelpers.FutureIso(6)
        });
        Assert.Equal(HttpStatusCode.Created, cita2Res.StatusCode);
        var cita2 = (await cita2Res.Content.ReadFromJsonAsync<ConsultaResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal(95000m, cita2.Costo);
        Assert.Equal("Programada", cita2.Estado);

        // La cita histórica anterior conserva su costo original de 72000; admin la marca Completada para que pase al historial
        var completeRes = await adminClient.PutAsJsonAsync($"/api/consultas/{cita1.Id}", new
        {
            userId = user.Id,
            mascotaId = pet.Id,
            operacionId = op.Id,
            costo = cita1.Costo,
            fechaConsulta = cita1.FechaConsulta.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            estado = "Completada",
            observaciones = cita1.Observaciones
        });
        Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);

        // 5. Verificar relación en Historial y Próxima Cita de la mascota
        var historial = await userClient.GetFromJsonAsync<MascotaHistorialResponse>(
            $"/api/mascotas/{pet.Id}/historial",
            TestHelpers.JsonOptions);
        Assert.NotNull(historial);
        Assert.NotNull(historial!.ProximaCita);
        Assert.Equal(cita2.Id, historial.ProximaCita!.Id);
        Assert.Equal(op.Nombre, historial.ProximaCita.OperacionNombre);
        Assert.Equal(95000m, historial.ProximaCita.Costo);

        Assert.Single(historial.CitasPrevias);
        Assert.Equal(cita1.Id, historial.CitasPrevias[0].Id);
        Assert.Equal(op.Id, historial.CitasPrevias[0].OperacionId);
        Assert.Equal(op.Nombre, historial.CitasPrevias[0].OperacionNombre);
        Assert.Equal(72000m, historial.CitasPrevias[0].Costo);

        // 6. No se puede eliminar una operación vinculada al historial -> 409 Conflict
        await TestHelpers.AssertErrorAsync(
            await adminClient.DeleteAsync($"/api/operaciones/{op.Id}"),
            HttpStatusCode.Conflict);

        // 7. Si admin desactiva la operación (activo = false), no permite agendar nuevas citas -> 409 Conflict
        await adminClient.PutAsJsonAsync($"/api/operaciones/{op.Id}", new
        {
            nombre = op.Nombre,
            tipoServicio = op.TipoServicio,
            descripcion = op.Descripcion,
            costo = 95000m,
            activo = false
        });

        await TestHelpers.AssertErrorAsync(await userClient.PostAsJsonAsync("/api/consultas", new
        {
            mascotaId = pet.Id,
            operacionId = op.Id,
            fechaConsulta = TestHelpers.FutureIso(10)
        }), HttpStatusCode.Conflict);
    }
}
