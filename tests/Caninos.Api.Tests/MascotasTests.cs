using System.Net;
using System.Net.Http.Json;
using System.Text;
using Caninos.Api.Dtos.Mascotas;

namespace Caninos.Api.Tests;

public sealed class MascotasTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public MascotasTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAll_AndFilterByUserId_ReturnExpectedLists()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var ownerWithPets = await TestHelpers.CreateSampleUsuarioAsync(client);
        var ownerEmpty = await TestHelpers.CreateSampleUsuarioAsync(client);

        var m1 = await TestHelpers.CreateSampleMascotaAsync(client, ownerWithPets.Id, "Firulais");
        var m2 = await TestHelpers.CreateSampleMascotaAsync(client, ownerWithPets.Id, "Canela");

        var all = await client.GetFromJsonAsync<List<MascotaResponse>>("/api/mascotas", TestHelpers.JsonOptions);
        Assert.Contains(all!, m => m.Id == m1.Id && m.PropietarioNombre == ownerWithPets.NombreCompleto);
        Assert.Contains(all!, m => m.Id == m2.Id);

        var filtered = await client.GetFromJsonAsync<List<MascotaResponse>>($"/api/mascotas?userId={ownerWithPets.Id}", TestHelpers.JsonOptions);
        Assert.Equal(2, filtered!.Count);

        var empty = await client.GetFromJsonAsync<List<MascotaResponse>>($"/api/mascotas?userId={ownerEmpty.Id}", TestHelpers.JsonOptions);
        Assert.Empty(empty!);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-4")]
    [InlineData("")]
    public async Task GetAll_InvalidUserIdQuery_Returns400(string badUserId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.GetAsync($"/api/mascotas?userId={badUserId}");
        var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
        Assert.True(error.Errors.ContainsKey("userId"));
    }

    [Fact]
    public async Task GetById_TwoExistingPets_Returns200()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var owner = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m1 = await TestHelpers.CreateSampleMascotaAsync(client, owner.Id, "Thor");
        var m2 = await TestHelpers.CreateSampleMascotaAsync(client, owner.Id, "Nala");

        var r1 = await client.GetFromJsonAsync<MascotaResponse>($"/api/mascotas/{m1.Id}", TestHelpers.JsonOptions);
        var r2 = await client.GetFromJsonAsync<MascotaResponse>($"/api/mascotas/{m2.Id}", TestHelpers.JsonOptions);
        Assert.Equal("Thor", r1!.Nombre);
        Assert.Equal("Nala", r2!.Nombre);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-10")]
    public async Task GetById_InvalidId_Returns400(string badId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/mascotas/{badId}"), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(880001)]
    [InlineData(880002)]
    public async Task GetById_NonExistent_Returns404(int id)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/mascotas/{id}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_MinAndMaxBoundaries_AndWhitespaceObservaciones_Returns201()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var owner = await TestHelpers.CreateSampleUsuarioAsync(client);

        // Límite mínimo: edad=0, pesoKg=0.2, sexo="macho", esterilizado=false, observaciones=espacios -> null
        var minRes = await client.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = "A",
            raza = "CR",
            edad = 0,
            pesoKg = 0.2m,
            sexo = "macho",
            color = "Ne",
            esterilizado = false,
            observaciones = "    ",
            userId = owner.Id
        });
        Assert.Equal(HttpStatusCode.Created, minRes.StatusCode);
        Assert.NotNull(minRes.Headers.Location);
        var minPet = (await minRes.Content.ReadFromJsonAsync<MascotaResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal("Macho", minPet.Sexo);
        Assert.Equal(0, minPet.Edad);
        Assert.Equal(0.2m, minPet.PesoKg);
        Assert.Null(minPet.Observaciones);

        // Límite máximo: edad=30, pesoKg=120.0, sexo="HEMBRA", observaciones=500 chars
        var obs500 = new string('x', 500);
        var maxRes = await client.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = "Reina Mayor",
            raza = "Gran Danés",
            edad = 30,
            pesoKg = 120.0m,
            sexo = "HEMBRA",
            color = "Arlequín",
            esterilizado = true,
            observaciones = obs500,
            userId = owner.Id
        });
        Assert.Equal(HttpStatusCode.Created, maxRes.StatusCode);
        var maxPet = (await maxRes.Content.ReadFromJsonAsync<MascotaResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal("Hembra", maxPet.Sexo);
        Assert.Equal(30, maxPet.Edad);
        Assert.Equal(120.0m, maxPet.PesoKg);
        Assert.Equal(obs500, maxPet.Observaciones);
    }

    [Theory]
    [InlineData("""{"nombre":"Max","raza":"Beagle","edad":"3","pesoKg":10,"sexo":"Macho","color":"Cafe","esterilizado":true,"userId":1}""")]
    [InlineData("""{"nombre":"Max","raza":"Beagle","edad":3.5,"pesoKg":10,"sexo":"Macho","color":"Cafe","esterilizado":true,"userId":1}""")]
    [InlineData("""{"nombre":"Max","raza":"Beagle","edad":-1,"pesoKg":10,"sexo":"Macho","color":"Cafe","esterilizado":true,"userId":1}""")]
    [InlineData("""{"nombre":"Max","raza":"Beagle","edad":31,"pesoKg":10,"sexo":"Macho","color":"Cafe","esterilizado":true,"userId":1}""")]
    [InlineData("""{"nombre":"Max","raza":"Beagle","edad":3,"pesoKg":"10.5","sexo":"Macho","color":"Cafe","esterilizado":true,"userId":1}""")]
    [InlineData("""{"nombre":"Max","raza":"Beagle","edad":3,"pesoKg":0.19,"sexo":"Macho","color":"Cafe","esterilizado":true,"userId":1}""")]
    [InlineData("""{"nombre":"Max","raza":"Beagle","edad":3,"pesoKg":120.01,"sexo":"Macho","color":"Cafe","esterilizado":true,"userId":1}""")]
    [InlineData("""{"nombre":"Max","raza":"Beagle","edad":3,"pesoKg":12.345,"sexo":"Macho","color":"Cafe","esterilizado":true,"userId":1}""")]
    [InlineData("""{"nombre":"Max","raza":"Beagle","edad":3,"pesoKg":10,"sexo":"Otro","color":"Cafe","esterilizado":true,"userId":1}""")]
    [InlineData("""{"nombre":"Max","raza":"Beagle","edad":3,"pesoKg":10,"sexo":"Macho","color":"Cafe","esterilizado":"false","userId":1}""")]
    [InlineData("""{"nombre":"   ","raza":"B","edad":3,"pesoKg":10,"sexo":"Macho","color":"C","esterilizado":true,"userId":1}""")]
    [InlineData("""{"nombre":"Max","raza":"Beagle","edad":3,"pesoKg":10,"sexo":"Macho","color":"Cafe","esterilizado":true,"userId":0}""")]
    [InlineData("""{"nombre":"Max","raza":"Beagle","sexo":"Macho","color":"Cafe","userId":1}""")]
    [InlineData("""{}""")]
    [InlineData("""{"nombre": """)]
    public async Task Create_InvalidDataTypesOrRanges_Returns400(string rawJson)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        using var content = TestHelpers.JsonContent(rawJson);
        var res = await client.PostAsync("/api/mascotas", content);
        var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
        Assert.NotEmpty(error.Errors);
    }

    [Fact]
    public async Task Create_ObservacionesOver500Chars_Returns400()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = "Max",
            raza = "Beagle",
            edad = 3,
            pesoKg = 10.5m,
            sexo = "Macho",
            color = "Tricolor",
            esterilizado = true,
            observaciones = new string('z', 501),
            userId = 1
        });
        var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
        Assert.True(error.Errors.ContainsKey("observaciones"));
    }

    [Theory]
    [InlineData(999101)]
    [InlineData(999102)]
    public async Task Create_NonExistentOwnerUserId_Returns404(int missingUserId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = "Boby",
            raza = "Mestizo",
            edad = 2,
            pesoKg = 9.5m,
            sexo = "Macho",
            color = "Café",
            esterilizado = false,
            userId = missingUserId
        });
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task Create_WrongContentType_Returns415(string mediaType)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        using var content = new StringContent("nombre=Max", Encoding.UTF8, mediaType);
        await TestHelpers.AssertErrorAsync(await client.PostAsync("/api/mascotas", content), HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Update_AttributesAndOwnerTransferWithoutConsultas_Returns200()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u1 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var u2 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var pet = await TestHelpers.CreateSampleMascotaAsync(client, u1.Id, "Bruno");

        // 1. Actualizar atributos con el mismo propietario
        var upd1 = await client.PutAsJsonAsync($"/api/mascotas/{pet.Id}", new
        {
            nombre = "Bruno Jr",
            raza = "Boxer",
            edad = 5,
            pesoKg = 26.75m,
            sexo = "Macho",
            color = "Atigrado",
            esterilizado = true,
            observaciones = (string?)null,
            userId = u1.Id
        });
        Assert.Equal(HttpStatusCode.OK, upd1.StatusCode);
        var p1 = (await upd1.Content.ReadFromJsonAsync<MascotaResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal("Bruno Jr", p1.Nombre);
        Assert.Null(p1.Observaciones);

        // 2. Transferir propietario cuando la mascota NO tiene consultas -> permitido (200)
        var upd2 = await client.PutAsJsonAsync($"/api/mascotas/{pet.Id}", new
        {
            nombre = "Bruno Jr",
            raza = "Boxer",
            edad = 5,
            pesoKg = 26.75m,
            sexo = "Macho",
            color = "Atigrado",
            esterilizado = true,
            observaciones = "Adoptado por nuevo dueño",
            userId = u2.Id
        });
        Assert.Equal(HttpStatusCode.OK, upd2.StatusCode);
        var p2 = (await upd2.Content.ReadFromJsonAsync<MascotaResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal(u2.Id, p2.UserId);
        Assert.Equal(u2.NombreCompleto, p2.PropietarioNombre);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-2")]
    public async Task Update_InvalidRouteId_Returns400(string badId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.PutAsJsonAsync($"/api/mascotas/{badId}", new
        {
            nombre = "Max",
            raza = "Beagle",
            edad = 3,
            pesoKg = 10m,
            sexo = "Macho",
            color = "Café",
            esterilizado = true,
            userId = 1
        });
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(35, 10.0, "Macho")]
    [InlineData(5, 0.1, "Otro")]
    public async Task Update_InvalidBodyFields_Returns400(int edad, decimal pesoKg, string sexo)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var owner = await TestHelpers.CreateSampleUsuarioAsync(client);
        var pet = await TestHelpers.CreateSampleMascotaAsync(client, owner.Id);

        var res = await client.PutAsJsonAsync($"/api/mascotas/{pet.Id}", new
        {
            nombre = pet.Nombre,
            raza = pet.Raza,
            edad,
            pesoKg,
            sexo,
            color = pet.Color,
            esterilizado = true,
            userId = owner.Id
        });
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(770001)]
    [InlineData(770002)]
    public async Task Update_NonExistentPet_Returns404(int petId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.PutAsJsonAsync($"/api/mascotas/{petId}", new
        {
            nombre = "Fantasma",
            raza = "Pug",
            edad = 2,
            pesoKg = 8m,
            sexo = "Macho",
            color = "Beige",
            esterilizado = false,
            userId = 1
        });
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_NonExistentTargetOwner_Returns404()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var owner = await TestHelpers.CreateSampleUsuarioAsync(client);
        var pet = await TestHelpers.CreateSampleMascotaAsync(client, owner.Id);

        var res = await client.PutAsJsonAsync($"/api/mascotas/{pet.Id}", new
        {
            nombre = pet.Nombre,
            raza = pet.Raza,
            edad = pet.Edad,
            pesoKg = pet.PesoKg,
            sexo = pet.Sexo,
            color = pet.Color,
            esterilizado = pet.Esterilizado,
            userId = 770099
        });
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_TransferOwnerWhenPetHasConsultas_Returns409InTwoCases()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u1 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var u2 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m1 = await TestHelpers.CreateSampleMascotaAsync(client, u1.Id, "Sasha");
        var m2 = await TestHelpers.CreateSampleMascotaAsync(client, u2.Id, "Zeus");

        await TestHelpers.CreateSampleConsultaAsync(client, u1.Id, m1.Id, "Salud");
        await TestHelpers.CreateSampleConsultaAsync(client, u2.Id, m2.Id, "Estetica");

        foreach (var (pet, newOwnerId) in new[] { (m1, u2.Id), (m2, u1.Id) })
        {
            var res = await client.PutAsJsonAsync($"/api/mascotas/{pet.Id}", new
            {
                nombre = pet.Nombre,
                raza = pet.Raza,
                edad = pet.Edad,
                pesoKg = pet.PesoKg,
                sexo = pet.Sexo,
                color = pet.Color,
                esterilizado = pet.Esterilizado,
                userId = newOwnerId
            });
            await TestHelpers.AssertErrorAsync(res, HttpStatusCode.Conflict);
        }
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task Update_WrongContentType_Returns415(string mediaType)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        using var content = new StringContent("body", Encoding.UTF8, mediaType);
        await TestHelpers.AssertErrorAsync(await client.PutAsync("/api/mascotas/1", content), HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Delete_TwoPetsWithoutConsultas_Returns204AndVerify404()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var owner = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m1 = await TestHelpers.CreateSampleMascotaAsync(client, owner.Id);
        var m2 = await TestHelpers.CreateSampleMascotaAsync(client, owner.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/mascotas/{m1.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/mascotas/{m2.Id}")).StatusCode);

        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/mascotas/{m1.Id}"), HttpStatusCode.NotFound);
        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/mascotas/{m2.Id}"), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("invalido")]
    [InlineData("0")]
    public async Task Delete_InvalidId_Returns400(string badId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.DeleteAsync($"/api/mascotas/{badId}"), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(660001)]
    [InlineData(660002)]
    public async Task Delete_NonExistent_Returns404(int id)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.DeleteAsync($"/api/mascotas/{id}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_PetWithConsultas_Returns409InTwoCases()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m1 = await TestHelpers.CreateSampleMascotaAsync(client, u.Id);
        var m2 = await TestHelpers.CreateSampleMascotaAsync(client, u.Id);

        await TestHelpers.CreateSampleConsultaAsync(client, u.Id, m1.Id, "Salud");
        await TestHelpers.CreateSampleConsultaAsync(client, u.Id, m2.Id, "Recreacion");

        await TestHelpers.AssertErrorAsync(await client.DeleteAsync($"/api/mascotas/{m1.Id}"), HttpStatusCode.Conflict);
        await TestHelpers.AssertErrorAsync(await client.DeleteAsync($"/api/mascotas/{m2.Id}"), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("POST", "/api/mascotas")]
    [InlineData("DELETE", "/api/mascotas/1")]
    public async Task Mascotas_WithoutToken_Returns401(string method, string path)
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
