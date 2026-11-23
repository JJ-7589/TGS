using System.Net;
using System.Net.Http.Json;
using System.Text;
using Caninos.Api.Dtos.Mascotas;
using Caninos.Api.Dtos.Usuarios;

namespace Caninos.Api.Tests;

public sealed class UsuariosTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public UsuariosTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAll_ReturnsSeededAdminAndNewlyCreatedUsers()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var initial = await client.GetFromJsonAsync<List<UsuarioResponse>>("/api/usuarios", TestHelpers.JsonOptions);
        Assert.NotNull(initial);
        Assert.Contains(initial, u => u.Username == "admin");

        var created = await TestHelpers.CreateSampleUsuarioAsync(client);
        var after = await client.GetFromJsonAsync<List<UsuarioResponse>>("/api/usuarios", TestHelpers.JsonOptions);
        Assert.Contains(after!, u => u.Id == created.Id);
    }

    [Theory]
    [InlineData("username")]
    [InlineData("documento")]
    [InlineData("nombre")]
    [InlineData("email")]
    public async Task GetAll_SearchFilter_MatchesEachSupportedField(string mode)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var tag = TestHelpers.UniqueTag();
        var user = await TestHelpers.CreateSampleUsuarioAsync(client, tag);

        var term = mode switch
        {
            "username" => user.Username,
            "documento" => user.Documento,
            "nombre" => $"Prueba {tag}",
            _ => user.Email
        };

        var results = await client.GetFromJsonAsync<List<UsuarioResponse>>(
            $"/api/usuarios?search={Uri.EscapeDataString(term)}",
            TestHelpers.JsonOptions);
        Assert.NotNull(results);
        Assert.Contains(results, u => u.Id == user.Id);
    }

    [Theory]
    [InlineData("termino_que_no_coincide_jamas_1")]
    [InlineData("termino_que_no_coincide_jamas_2")]
    public async Task GetAll_SearchWithoutMatches_ReturnsEmptyArray(string term)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var results = await client.GetFromJsonAsync<List<UsuarioResponse>>($"/api/usuarios?search={term}", TestHelpers.JsonOptions);
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(150)]
    public async Task GetAll_SearchExceeding100Chars_Returns400(int length)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.GetAsync($"/api/usuarios?search={new string('a', length)}");
        var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
        Assert.True(error.Errors.ContainsKey("search"));
    }

    [Fact]
    public async Task GetById_ExistingUsers_Returns200WithoutPassword()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u1Res = await client.GetAsync("/api/usuarios/1");
        Assert.Equal(HttpStatusCode.OK, u1Res.StatusCode);
        Assert.DoesNotContain("password", await u1Res.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var u2 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var u2Res = await client.GetAsync($"/api/usuarios/{u2.Id}");
        Assert.Equal(HttpStatusCode.OK, u2Res.StatusCode);
        var fetched = await u2Res.Content.ReadFromJsonAsync<UsuarioResponse>(TestHelpers.JsonOptions);
        Assert.Equal(u2.Username, fetched!.Username);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-7")]
    [InlineData("999999999999")]
    public async Task GetById_InvalidId_Returns400(string badId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.GetAsync($"/api/usuarios/{badId}");
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(999901)]
    [InlineData(999902)]
    public async Task GetById_NonExistent_Returns404(int id)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.GetAsync($"/api/usuarios/{id}");
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMascotasByUsuario_EmptyAndPopulated_Returns200()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u = await TestHelpers.CreateSampleUsuarioAsync(client);

        var empty = await client.GetFromJsonAsync<List<MascotaResponse>>($"/api/usuarios/{u.Id}/mascotas", TestHelpers.JsonOptions);
        Assert.Empty(empty!);

        await TestHelpers.CreateSampleMascotaAsync(client, u.Id, "Toby");
        await TestHelpers.CreateSampleMascotaAsync(client, u.Id, "Kira");

        var populated = await client.GetFromJsonAsync<List<MascotaResponse>>($"/api/usuarios/{u.Id}/mascotas", TestHelpers.JsonOptions);
        Assert.Equal(2, populated!.Count);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("-1")]
    public async Task GetMascotasByUsuario_InvalidId_Returns400(string badId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.GetAsync($"/api/usuarios/{badId}/mascotas");
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(888101)]
    [InlineData(888102)]
    public async Task GetMascotasByUsuario_NonExistentUser_Returns404(int id)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.GetAsync($"/api/usuarios/{id}/mascotas");
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_BoundaryMinAndRichUnicodeValues_Returns201()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var t1 = TestHelpers.UniqueTag()[..3];
        var t2 = TestHelpers.UniqueTag();

        // Valores en límite mínimo exacto
        var minRes = await client.PostAsJsonAsync("/api/usuarios", new
        {
            username = $"u{t1[..2]}",   // 3 chars
            password = "00000",         // 5 dígitos
            documento = $"D{t1}1",      // 5 chars
            nombreCompleto = "Li",      // 2 chars
            telefono = "1234567",       // 7 dígitos
            email = $"{t1}@a.co",
            direccion = "Cll 1"         // 5 chars
        });
        Assert.Equal(HttpStatusCode.Created, minRes.StatusCode);

        // Valores con acentos, ñ y teléfono internacional con guiones/espacios
        var richRes = await client.PostAsJsonAsync("/api/usuarios", new
        {
            username = $"jose.nunez_{t2}",
            password = "98765432109876543210", // 20 dígitos exactos
            documento = $"CE-{t2}",
            nombreCompleto = "  José Ángel Núñez Peña  ",
            telefono = "+57 310-555-7890",
            email = $"JOSE.{t2}@CORREO.EDU.CO",
            direccion = "  Avenida Boyacá # 116-50 Apto 402  "
        });
        Assert.Equal(HttpStatusCode.Created, richRes.StatusCode);
        var rich = (await richRes.Content.ReadFromJsonAsync<UsuarioResponse>(TestHelpers.JsonOptions))!;
        Assert.Equal("José Ángel Núñez Peña", rich.NombreCompleto);
        Assert.Equal($"jose.{t2}@correo.edu.co", rich.Email);
        Assert.Equal("Avenida Boyacá # 116-50 Apto 402", rich.Direccion);
    }

    [Theory]
    [InlineData("""{"username":"ab","password":"12345","documento":"12345","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""", "username")]
    [InlineData("""{"username":"user!@#","password":"12345","documento":"12345","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""", "username")]
    [InlineData("""{"username":"valido1","password":"1234","documento":"12345","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""", "password")]
    [InlineData("""{"username":"valido2","password":"abcde","documento":"12345","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""", "password")]
    [InlineData("""{"username":"valido3","password":12345,"documento":"12345","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""", "password")]
    [InlineData("""{"username":"valido4","password":"12345","documento":"1234","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""", "documento")]
    [InlineData("""{"username":"valido5","password":"12345","documento":"DOC$%&","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""", "documento")]
    [InlineData("""{"username":"valido6","password":"12345","documento":"12345","nombreCompleto":"123456","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""", "nombreCompleto")]
    [InlineData("""{"username":"valido7","password":"12345","documento":"12345","nombreCompleto":"   ","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""", "nombreCompleto")]
    [InlineData("""{"username":"valido8","password":"12345","documento":"12345","nombreCompleto":"Ana","telefono":"123456","email":"a@b.co","direccion":"Calle 123"}""", "telefono")]
    [InlineData("""{"username":"valido9","password":"12345","documento":"12345","nombreCompleto":"Ana","telefono":"300-ABC-123","email":"a@b.co","direccion":"Calle 123"}""", "telefono")]
    [InlineData("""{"username":"valido10","password":"12345","documento":"12345","nombreCompleto":"Ana","telefono":"3001234567","email":"correo-invalido","direccion":"Calle 123"}""", "email")]
    [InlineData("""{"username":"valido11","password":"12345","documento":"12345","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"abc"}""", "direccion")]
    [InlineData("""{"username":"valido12","password":"12345","documento":"12345","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"     "}""", "direccion")]
    public async Task Create_InvalidFieldValues_Returns400WithSpecificFieldError(string rawJson, string expectedField)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        using var content = TestHelpers.JsonContent(rawJson);
        var res = await client.PostAsync("/api/usuarios", content);
        var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
        Assert.True(error.Errors.ContainsKey(expectedField), $"Se esperaba error en '{expectedField}'.");
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task Create_WrongContentType_Returns415(string mediaType)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        using var content = new StringContent("body", Encoding.UTF8, mediaType);
        var res = await client.PostAsync("/api/usuarios", content);
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Update_WithAndWithoutPassword_BehavesCorrectly()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var anon = _factory.CreateClient();
        var user = await TestHelpers.CreateSampleUsuarioAsync(client, password: "11223");

        // 1. Update sin password: debe preservar "11223"
        var upd1 = await client.PutAsJsonAsync($"/api/usuarios/{user.Id}", new
        {
            username = user.Username,
            documento = user.Documento,
            nombreCompleto = "Nombre Editado Uno",
            telefono = "3209998877",
            email = user.Email,
            direccion = "Dirección Editada 100"
        });
        Assert.Equal(HttpStatusCode.OK, upd1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password = "11223" })).StatusCode);

        // 2. Update con nuevo password "99887": invalida el anterior y activa el nuevo
        var upd2 = await client.PutAsJsonAsync($"/api/usuarios/{user.Id}", new
        {
            username = user.Username,
            password = "99887",
            documento = user.Documento,
            nombreCompleto = "Nombre Editado Dos",
            telefono = "3209998877",
            email = user.Email,
            direccion = "Dirección Editada 200"
        });
        Assert.Equal(HttpStatusCode.OK, upd2.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password = "11223" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password = "99887" })).StatusCode);
    }

    [Theory]
    [InlineData("abc", """{"username":"u","documento":"12345","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""")]
    [InlineData("0", """{"username":"u","documento":"12345","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""")]
    public async Task Update_InvalidRouteId_Returns400(string badId, string rawJson)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        using var content = TestHelpers.JsonContent(rawJson);
        var res = await client.PutAsync($"/api/usuarios/{badId}", content);
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123a")]
    public async Task Update_InvalidProvidedPassword_Returns400(string badPassword)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var user = await TestHelpers.CreateSampleUsuarioAsync(client);

        var res = await client.PutAsJsonAsync($"/api/usuarios/{user.Id}", new
        {
            username = user.Username,
            password = badPassword,
            documento = user.Documento,
            nombreCompleto = user.NombreCompleto,
            telefono = user.Telefono,
            email = user.Email,
            direccion = user.Direccion
        });
        var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
        Assert.True(error.Errors.ContainsKey("password"));
    }

    [Theory]
    [InlineData(777101)]
    [InlineData(777102)]
    public async Task Update_NonExistentUser_Returns404(int id)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var res = await client.PutAsJsonAsync($"/api/usuarios/{id}", new
        {
            username = $"fantasma_{id}",
            documento = $"F{id}",
            nombreCompleto = "Fantasma",
            telefono = "3001234567",
            email = $"f{id}@mail.com",
            direccion = "Calle 99 # 1-2"
        });
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("username")]
    [InlineData("documento")]
    [InlineData("email")]
    public async Task Update_DuplicateFieldWithAnotherUser_Returns409(string conflictField)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u1 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var u2 = await TestHelpers.CreateSampleUsuarioAsync(client);

        var res = await client.PutAsJsonAsync($"/api/usuarios/{u2.Id}", new
        {
            username = conflictField == "username" ? u1.Username.ToUpperInvariant() : u2.Username,
            documento = conflictField == "documento" ? u1.Documento : u2.Documento,
            nombreCompleto = u2.NombreCompleto,
            telefono = u2.Telefono,
            email = conflictField == "email" ? u1.Email.ToUpperInvariant() : u2.Email,
            direccion = u2.Direccion
        });
        var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.Conflict);
        Assert.True(error.Errors.ContainsKey(conflictField));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    public async Task Update_WrongContentType_Returns415(string mediaType)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        using var content = new StringContent("texto", Encoding.UTF8, mediaType);
        var res = await client.PutAsync("/api/usuarios/1", content);
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Delete_TwoIndependentUsers_Returns204AndSubsequentGet404()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        var u1 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var u2 = await TestHelpers.CreateSampleUsuarioAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/usuarios/{u1.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/usuarios/{u2.Id}")).StatusCode);

        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/usuarios/{u1.Id}"), HttpStatusCode.NotFound);
        await TestHelpers.AssertErrorAsync(await client.GetAsync($"/api/usuarios/{u2.Id}"), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("invalido")]
    [InlineData("-9")]
    public async Task Delete_InvalidId_Returns400(string badId)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.DeleteAsync($"/api/usuarios/{badId}"), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(666101)]
    [InlineData(666102)]
    public async Task Delete_NonExistent_Returns404(int id)
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);
        await TestHelpers.AssertErrorAsync(await client.DeleteAsync($"/api/usuarios/{id}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_UserWithMascotaOrConsulta_Returns409InBothCases()
    {
        var client = await TestHelpers.CreateAuthorizedClientAsync(_factory);

        // Caso 1: usuario con mascota (sin consultas) -> 409
        var u1 = await TestHelpers.CreateSampleUsuarioAsync(client);
        await TestHelpers.CreateSampleMascotaAsync(client, u1.Id);
        await TestHelpers.AssertErrorAsync(await client.DeleteAsync($"/api/usuarios/{u1.Id}"), HttpStatusCode.Conflict);

        // Caso 2: usuario con mascota y consulta -> 409
        var u2 = await TestHelpers.CreateSampleUsuarioAsync(client);
        var m2 = await TestHelpers.CreateSampleMascotaAsync(client, u2.Id);
        await TestHelpers.CreateSampleConsultaAsync(client, u2.Id, m2.Id);
        await TestHelpers.AssertErrorAsync(await client.DeleteAsync($"/api/usuarios/{u2.Id}"), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("POST", "/api/usuarios")]
    [InlineData("DELETE", "/api/usuarios/1")]
    public async Task Usuarios_WithoutToken_Returns401(string method, string path)
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
