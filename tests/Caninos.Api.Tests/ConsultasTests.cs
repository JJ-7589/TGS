using System.Net;
using System.Net.Http.Json;
using System.Text;
using Caninos.Api.Dtos.Consultas;

namespace Caninos.Api.Tests;

public sealed class ConsultasTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ConsultasTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAll_FiltersByUserMascotaServicioEstadoAndCombined_Work()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u1 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var u2 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var uEmpty = await TestHelpers.CreateSampleUsuarioAsync(client);

        var m1 = await TestHelpers.CreateSampleMascotaAsync(client, u1.Id, "Baxter", "Beagle");
        var m2 = await TestHelpers.CreateSampleMascotaAsync(client, u2.Id, "Mia", "Poodle");
        var mEmpty = await TestHelpers.CreateSampleMascotaAsync(client, uEmpty.Id, "Nube", "Husky");

        var c1 = await TestHelpers.CreateSampleConsultaAsync(client, u1.Id, m1.Id, "Salud", "Programada", "2026-11-24T08:00:00Z", 60000m);
        var c2 = await TestHelpers.CreateSampleConsultaAsync(client, u1.Id, m1.Id, "Estética", "EnProceso", "2026-11-26T15:00:00Z", 40000m);
        var c3 = await TestHelpers.CreateSampleConsultaAsync(client, u2.Id, m2.Id, "Recreación", "Completada", "2026-11-28T11:00:00Z", 35000m);

        // Orden descendente por fechaConsulta
        var byU1 = await client.GetFromJsonAsync<List<ConsultaResponse>>($"/api/consultas?userId={u1.Id}", TestHelpers.JsonOptions);
        Assert.Equal(2, byU1!.Count);
        Assert.Equal(c2.Id, byU1[0].Id);
        Assert.Equal(c1.Id, byU1[1].Id);

        // Usuario sin consultas -> []
        var byUEmpty = await client.GetFromJsonAsync<List<ConsultaResponse>>($"/api/consultas?userId={uEmpty.Id}", TestHelpers.JsonOptions);
        Assert.Empty(byUEmpty!);

        // Por mascotaId con y sin resultados
        var byM2 = await client.GetFromJsonAsync<List<ConsultaResponse>>($"/api/consultas?mascotaId={m2.Id}", TestHelpers.JsonOptions);
        Assert.Single(byM2!);
        Assert.Equal(c3.Id, byM2![0].Id);
        Assert.Empty((await client.GetFromJsonAsync<List<ConsultaResponse>>($"/api/consultas?mascotaId={mEmpty.Id}", TestHelpers.JsonOptions))!);

        // Por tipoServicio (con tilde y sin tilde)
        var byEstetica = await client.GetFromJsonAsync<List<ConsultaResponse>>("/api/consultas?tipoServicio=Estética", TestHelpers.JsonOptions);
        Assert.Contains(byEstetica!, c => c.Id == c2.Id);
        Assert.All(byEstetica!, c => Assert.Equal("Estetica", c.TipoServicio));

        // Por estado
        var byCompletada = await client.GetFromJsonAsync<List<ConsultaResponse>>("/api/consultas?estado=Completada", TestHelpers.JsonOptions);
        Assert.Contains(byCompletada!, c => c.Id == c3.Id);

        // Filtro combinado con resultado y sin resultado
        var comboHit = await client.GetFromJsonAsync<List<ConsultaResponse>>(
            $"/api/consultas?userId={u1.Id}&mascotaId={m1.Id}&tipoServicio=Salud&estado=Programada",
            TestHelpers.JsonOptions);
        Assert.Single(comboHit!);
        Assert.Equal(c1.Id, comboHit![0].Id);

        var comboMiss = await client.GetFromJsonAsync<List<ConsultaResponse>>(
            $"/api/consultas?userId={u1.Id}&tipoServicio=Recreacion",
            TestHelpers.JsonOptions);
        Assert.Empty(comboMiss!);
    }

    [Theory]
    [InlineData("userId=abc", "userId")]
    [InlineData("userId=-1", "userId")]
    [InlineData("userId=", "userId")]
    [InlineData("mascotaId=xyz", "mascotaId")]
    [InlineData("mascotaId=0", "mascotaId")]
    [InlineData("mascotaId=", "mascotaId")]
    [InlineData("tipoServicio=Cirugia", "tipoServicio")]
    [InlineData("tipoServicio=", "tipoServicio")]
    [InlineData("estado=Pendiente", "estado")]
    [InlineData("estado=", "estado")]
    public async Task GetAll_InvalidQueryParameters_Returns400(string query, string expectedField)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.GetAsync($"/api/consultas?{query}");
        var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
        Assert.True(error.Errors.ContainsKey(expectedField));
    }

    [Fact]
    public async Task GetById_TwoExistingConsultas_Returns200WithJoinedInfo()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m = await TestHelpers.CreateSampleMascotaAsync(client, u.Id, "Simba", "Golden");
        var c1 = await TestHelpers.CreateSampleConsultaAsync(client, u.Id, m.Id, "Salud");
        var c2 = await TestHelpers.CreateSampleConsultaAsync(client, u.Id, m.Id, "Recreacion");

        var r1 = await client.GetFromJsonAsync<ConsultaResponse>($"/api/consultas/{c1.Id}", TestHelpers.JsonOptions);
        var r2 = await client.GetFromJsonAsync<ConsultaResponse>($"/api/consultas/{c2.Id}", TestHelpers.JsonOptions);

        Assert.Equal("Simba", r1!.MascotaNombre);
        Assert.Equal("Golden", r1.MascotaRaza);
        Assert.Equal(u.NombreCompleto, r1.UsuarioNombre);
        Assert.Equal("Recreacion", r2!.TipoServicio);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task GetById_InvalidId_Returns400(string badId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/consultas/{badId}"), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(550001)]
    [InlineData(550002)]
    public async Task GetById_NonExistent_Returns404(int id)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/consultas/{id}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetByUsuarioRoute_WithAndWithoutConsultas_Returns200()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var uWith = await TestHelpers.CreateSampleUsuarioAsync(client);
        var uWithout = await TestHelpers.CreateSampleUsuarioAsync(client);
        var pet = await TestHelpers.CreateSampleMascotaAsync(client, uWith.Id);

        await TestHelpers.CreateSampleConsultaAsync(client, uWith.Id, pet.Id, "Salud");
        await TestHelpers.CreateSampleConsultaAsync(client, uWith.Id, pet.Id, "Estetica");

        var list = await client.GetFromJsonAsync<List<ConsultaResponse>>($"/api/consultas/usuario/{uWith.Id}", TestHelpers.JsonOptions);
        Assert.Equal(2, list!.Count);

        var empty = await client.GetFromJsonAsync<List<ConsultaResponse>>($"/api/consultas/usuario/{uWithout.Id}", TestHelpers.JsonOptions);
        Assert.Empty(empty!);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    public async Task GetByUsuarioRoute_InvalidId_Returns400(string badId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/consultas/usuario/{badId}"), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(551001)]
    [InlineData(551002)]
    public async Task GetByUsuarioRoute_NonExistent_Returns404(int userId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/consultas/usuario/{userId}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetByMascotaRoute_WithAndWithoutConsultas_Returns200()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u = await TestHelpers.CreateSampleUsuarioAsync(client);
        var mWith = await TestHelpers.CreateSampleMascotaAsync(client, u.Id);
        var mWithout = await TestHelpers.CreateSampleMascotaAsync(client, u.Id);

        await TestHelpers.CreateSampleConsultaAsync(client, u.Id, mWith.Id, "Recreacion");

        var list = await client.GetFromJsonAsync<List<ConsultaResponse>>($"/api/consultas/mascota/{mWith.Id}", TestHelpers.JsonOptions);
        Assert.Single(list!);

        var empty = await client.GetFromJsonAsync<List<ConsultaResponse>>($"/api/consultas/mascota/{mWithout.Id}", TestHelpers.JsonOptions);
        Assert.Empty(empty!);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("0")]
    public async Task GetByMascotaRoute_InvalidId_Returns400(string badId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/consultas/mascota/{badId}"), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(552001)]
    [InlineData(552002)]
    public async Task GetByMascotaRoute_NonExistent_Returns404(int mascotaId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/consultas/mascota/{mascotaId}"), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Salud", "Salud")]
    [InlineData("Estetica", "Estetica")]
    [InlineData("Estética", "Estetica")]
    [InlineData("Recreación", "Recreacion")]
    public async Task GetByServicioRoute_ValidServices_Returns200(string routeParam, string normalizedExpected)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m = await TestHelpers.CreateSampleMascotaAsync(client, u.Id);
        var created = await TestHelpers.CreateSampleConsultaAsync(client, u.Id, m.Id, routeParam);

        var res = await client.GetFromJsonAsync<List<ConsultaResponse>>(
            $"/api/consultas/servicio/{Uri.EscapeDataString(routeParam)}",
            TestHelpers.JsonOptions);
        Assert.NotNull(res);
        Assert.Contains(res, c => c.Id == created.Id);
        Assert.All(res, c => Assert.Equal(normalizedExpected, c.TipoServicio));
    }

    [Theory]
    [InlineData("Cirugia")]
    [InlineData("Guarderia")]
    public async Task GetByServicioRoute_InvalidService_Returns400(string badService)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.GetAsync($"/api/consultas/servicio/{badService}");
        var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
        Assert.True(error.Errors.ContainsKey("tipoServicio"));
    }

    [Fact]
    public async Task Create_AllServiceTypesStatesAndCostBoundaries_Returns201()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m = await TestHelpers.CreateSampleMascotaAsync(client, u.Id);

        // 1. Estado omitido -> default "Programada", costo mínimo = 0
        var r1 = await client.PostAsJsonAsync("/api/consultas", new
        {
            userId = u.Id,
            mascotaId = m.Id,
            tipoServicio = "salud",
            descripcion = "Revisión preventiva gratuita",
            fechaConsulta = "2026-11-25",
            costo = 0m
        });
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.NotNull(r1.Headers.Location);
        var c1 = (await r1.Content.ReadFromJsonAsync<ConsultaResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal("Salud", c1.TipoServicio);
        Assert.Equal("Programada", c1.Estado);
        Assert.Equal(0m, c1.Costo);

        // 2. Costo máximo = 100000000, estado Cancelada, observaciones vacías -> null
        var r2 = await client.PostAsJsonAsync("/api/consultas", new
        {
            userId = u.Id,
            mascotaId = m.Id,
            tipoServicio = "Estética",
            descripcion = "Paquete spa premium anual",
            fechaConsulta = "2026-12-01T16:30:00Z",
            costo = 100000000m,
            estado = "cancelada",
            observaciones = "   "
        });
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        var c2 = (await r2.Content.ReadFromJsonAsync<ConsultaResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal("Estetica", c2.TipoServicio);
        Assert.Equal("Cancelada", c2.Estado);
        Assert.Equal(100000000m, c2.Costo);
        Assert.Null(c2.Observaciones);
    }

    [Theory]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"Costo negativo","fechaConsulta":"2026-11-25T10:00:00Z","costo":-0.01}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"Costo excesivo","fechaConsulta":"2026-11-25T10:00:00Z","costo":100000000.01}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"Tres decimales","fechaConsulta":"2026-11-25T10:00:00Z","costo":50.123}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"Costo como texto","fechaConsulta":"2026-11-25T10:00:00Z","costo":"50000"}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Invalido","descripcion":"Servicio inválido","fechaConsulta":"2026-11-25T10:00:00Z","costo":100}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"Estado inválido","fechaConsulta":"2026-11-25T10:00:00Z","costo":100,"estado":"Pendiente"}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"Estado vacío","fechaConsulta":"2026-11-25T10:00:00Z","costo":100,"estado":""}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"abcd","fechaConsulta":"2026-11-25T10:00:00Z","costo":100}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"     ","fechaConsulta":"2026-11-25T10:00:00Z","costo":100}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"Fecha inválida","fechaConsulta":"no-es-fecha","costo":100}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"Fecha numérica","fechaConsulta":20261125,"costo":100}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"Año anterior a 2000","fechaConsulta":"1999-12-31T23:59:59Z","costo":100}""")]
    [InlineData("""{"userId":1,"mascotaId":1,"tipoServicio":"Salud","descripcion":"Año posterior a 2100","fechaConsulta":"2101-01-01T00:00:00Z","costo":100}""")]
    [InlineData("""{"userId":0,"mascotaId":-1,"tipoServicio":"Salud","descripcion":"IDs inválidos","fechaConsulta":"2026-11-25T10:00:00Z","costo":100}""")]
    [InlineData("""{}""")]
    [InlineData("""{"userId": """)]
    public async Task Create_InvalidDataTypesOrRanges_Returns400(string rawJson)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        using var content = TestHelpers.JsonContent(rawJson);
        var res = await client.PostAsync("/api/consultas", content);
        var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
        Assert.NotEmpty(error.Errors);
    }

    [Fact]
    public async Task Create_NonExistentUserOrMascota_Returns404InBothCases()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m = await TestHelpers.CreateSampleMascotaAsync(client, u.Id);

        // Usuario inexistente (2 veces)
        foreach (var badUser in new[] { 994001, 994002 })
        {
            var res = await client.PostAsJsonAsync("/api/consultas", new
            {
                userId = badUser,
                mascotaId = m.Id,
                tipoServicio = "Salud",
                descripcion = "Consulta con usuario inexistente",
                fechaConsulta = "2026-11-25T10:00:00Z",
                costo = 10000
            });
            await TestHelpers.AssertErrorAsync(res, HttpStatusCode.NotFound);
        }

        // Mascota inexistente (2 veces)
        foreach (var badPet in new[] { 994003, 994004 })
        {
            var res = await client.PostAsJsonAsync("/api/consultas", new
            {
                userId = u.Id,
                mascotaId = badPet,
                tipoServicio = "Salud",
                descripcion = "Consulta con mascota inexistente",
                fechaConsulta = "2026-11-25T10:00:00Z",
                costo = 10000
            });
            await TestHelpers.AssertErrorAsync(res, HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Create_MascotaDoesNotBelongToUser_Returns409InBothDirections()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u1 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var u2 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m1 = await TestHelpers.CreateSampleMascotaAsync(client, u1.Id);
        var m2 = await TestHelpers.CreateSampleMascotaAsync(client, u2.Id);

        foreach (var (uid, mid) in new[] { (u1.Id, m2.Id), (u2.Id, m1.Id) })
        {
            var res = await client.PostAsJsonAsync("/api/consultas", new
            {
                userId = uid,
                mascotaId = mid,
                tipoServicio = "Salud",
                descripcion = "Mascota no pertenece al cliente",
                fechaConsulta = "2026-11-25T10:00:00Z",
                costo = 25000
            });
            var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.Conflict);
            Assert.True(error.Errors.ContainsKey("mascotaId"));
        }
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task Create_WrongContentType_Returns415(string mediaType)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        using var content = new StringContent("body", Encoding.UTF8, mediaType);
        await TestHelpers.AssertErrorAsync(await client.PostAsync("/api/consultas", content), HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Update_SameOwnerAndReassignedValidPair_Returns200()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u1 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var u2 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m1 = await TestHelpers.CreateSampleMascotaAsync(client, u1.Id, "Rex");
        var m2 = await TestHelpers.CreateSampleMascotaAsync(client, u2.Id, "Lola");

        var created = await TestHelpers.CreateSampleConsultaAsync(client, u1.Id, m1.Id, "Salud", "Programada");

        // 1. Actualizar servicio, costo y estado manteniendo (u1, m1)
        var upd1 = await client.PutAsJsonAsync($"/api/consultas/{created.Id}", new
        {
            userId = u1.Id,
            mascotaId = m1.Id,
            tipoServicio = "Estética",
            descripcion = "Cambio a servicio de estética completo",
            fechaConsulta = "2026-11-29T12:00:00Z",
            costo = 55000.75m,
            estado = "EnProceso",
            observaciones = "Actualización 1"
        });
        Assert.Equal(HttpStatusCode.OK, upd1.StatusCode);
        var r1 = (await upd1.Content.ReadFromJsonAsync<ConsultaResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal("Estetica", r1.TipoServicio);
        Assert.Equal("EnProceso", r1.Estado);
        Assert.Equal(55000.75m, r1.Costo);

        // 2. Reasignar consulta a otro par coherente (u2, m2)
        var upd2 = await client.PutAsJsonAsync($"/api/consultas/{created.Id}", new
        {
            userId = u2.Id,
            mascotaId = m2.Id,
            tipoServicio = "Recreación",
            descripcion = "Reasignada a otro cliente y su propia mascota",
            fechaConsulta = "2026-11-30T15:00:00Z",
            costo = 30000m,
            estado = "Completada",
            observaciones = "Actualización 2"
        });
        Assert.Equal(HttpStatusCode.OK, upd2.StatusCode);
        var r2 = (await upd2.Content.ReadFromJsonAsync<ConsultaResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal(u2.Id, r2.UserId);
        Assert.Equal(m2.Id, r2.MascotaId);
        Assert.Equal("Lola", r2.MascotaNombre);
        Assert.Equal("Completada", r2.Estado);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    public async Task Update_InvalidRouteId_Returns400(string badId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.PutAsJsonAsync($"/api/consultas/{badId}", new
        {
            userId = 1,
            mascotaId = 1,
            tipoServicio = "Salud",
            descripcion = "Prueba de ID inválido",
            fechaConsulta = "2026-11-25T10:00:00Z",
            costo = 1000,
            estado = "Programada"
        });
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_MissingRequiredEstadoOrNegativeCosto_Returns400()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m = await TestHelpers.CreateSampleMascotaAsync(client, u.Id);
        var c = await TestHelpers.CreateSampleConsultaAsync(client, u.Id, m.Id);

        // 1. Estado null es obligatorio en PUT
        var noEstado = await client.PutAsJsonAsync($"/api/consultas/{c.Id}", new
        {
            userId = u.Id,
            mascotaId = m.Id,
            tipoServicio = "Salud",
            descripcion = "Falta estado en PUT",
            fechaConsulta = "2026-11-25T10:00:00Z",
            costo = 20000
        });
        var err1 = await TestHelpers.AssertErrorAsync(noEstado, HttpStatusCode.BadRequest);
        Assert.True(err1.Errors.ContainsKey("estado"));

        // 2. Costo negativo en PUT
        var negCosto = await client.PutAsJsonAsync($"/api/consultas/{c.Id}", new
        {
            userId = u.Id,
            mascotaId = m.Id,
            tipoServicio = "Salud",
            descripcion = "Costo negativo en PUT",
            fechaConsulta = "2026-11-25T10:00:00Z",
            costo = -10,
            estado = "Programada"
        });
        var err2 = await TestHelpers.AssertErrorAsync(negCosto, HttpStatusCode.BadRequest);
        Assert.True(err2.Errors.ContainsKey("costo"));
    }

    [Theory]
    [InlineData(440001)]
    [InlineData(440002)]
    public async Task Update_NonExistentConsulta_Returns404(int id)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.PutAsJsonAsync($"/api/consultas/{id}", new
        {
            userId = 1,
            mascotaId = 1,
            tipoServicio = "Salud",
            descripcion = "No existe esta consulta",
            fechaConsulta = "2026-11-25T10:00:00Z",
            costo = 1000,
            estado = "Programada"
        });
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_OwnershipMismatch_Returns409InBothDirections()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u1 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var u2 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m1 = await TestHelpers.CreateSampleMascotaAsync(client, u1.Id);
        var m2 = await TestHelpers.CreateSampleMascotaAsync(client, u2.Id);
        var c1 = await TestHelpers.CreateSampleConsultaAsync(client, u1.Id, m1.Id);
        var c2 = await TestHelpers.CreateSampleConsultaAsync(client, u2.Id, m2.Id);

        var r1 = await client.PutAsJsonAsync($"/api/consultas/{c1.Id}", new
        {
            userId = u1.Id,
            mascotaId = m2.Id,
            tipoServicio = "Salud",
            descripcion = "Conflicto en actualización 1",
            fechaConsulta = "2026-11-25T10:00:00Z",
            costo = 10000,
            estado = "Programada"
        });
        await TestHelpers.AssertErrorAsync(r1, HttpStatusCode.Conflict);

        var r2 = await client.PutAsJsonAsync($"/api/consultas/{c2.Id}", new
        {
            userId = u2.Id,
            mascotaId = m1.Id,
            tipoServicio = "Salud",
            descripcion = "Conflicto en actualización 2",
            fechaConsulta = "2026-11-25T10:00:00Z",
            costo = 10000,
            estado = "Programada"
        });
        await TestHelpers.AssertErrorAsync(r2, HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    public async Task Update_WrongContentType_Returns415(string mediaType)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        using var content = new StringContent("body", Encoding.UTF8, mediaType);
        await TestHelpers.AssertErrorAsync(await client.PutAsync("/api/consultas/1", content), HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Delete_TwoConsultas_Returns204AndVerify404()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m = await TestHelpers.CreateSampleMascotaAsync(client, u.Id);
        var c1 = await TestHelpers.CreateSampleConsultaAsync(client, u.Id, m.Id, "Salud");
        var c2 = await TestHelpers.CreateSampleConsultaAsync(client, u.Id, m.Id, "Estetica");

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/consultas/{c1.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/consultas/{c2.Id}")).StatusCode);

        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/consultas/{c1.Id}"), HttpStatusCode.NotFound);
        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/consultas/{c2.Id}"), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("invalido")]
    [InlineData("-4")]
    public async Task Delete_InvalidId_Returns400(string badId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.DeleteAsync($"/api/consultas/{badId}"), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(330001)]
    [InlineData(330002)]
    public async Task Delete_NonExistent_Returns404(int id)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.DeleteAsync($"/api/consultas/{id}"), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("POST", "/api/consultas")]
    [InlineData("DELETE", "/api/consultas/1")]
    public async Task Consultas_WithoutToken_Returns401(string method, string path)
    {
        var anon = _factory.CreateClient();
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            req.Content = TestHelpers.JsonContent("{}");
        }

        await TestHelpers.AssertErrorAsync(await anon.SendAsync(req), HttpStatusCode.Unauthorized);
    }
}
