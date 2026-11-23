using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Caninos.Api.Dtos.Auth;

namespace Caninos.Api.Tests;

public sealed class AuthAndProtocolTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthAndProtocolTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:3000")]
    public async Task Health_AndCorsAllowedLocalOrigins_Return200WithCorsHeader(string origin)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        req.Headers.Add("Origin", origin);

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(res.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Contains(origin, values);

        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body);
        Assert.Contains("\"database\":\"sqlite\"", body);
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://otrodominio.org")]
    public async Task Cors_DisallowedExternalOrigins_DoNotReceiveAllowOriginHeader(string origin)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        req.Headers.Add("Origin", origin);

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.False(res.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("/swagger/v1/swagger.json", "/api/usuarios")]
    [InlineData("/swagger/v1/swagger.json", "/api/consultas/servicio/{tipoServicio}")]
    [InlineData("/swagger/index.html", "swagger-ui")]
    public async Task Swagger_EndpointsAvailableAndLowercase(string path, string expectedContent)
    {
        var res = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var text = await res.Content.ReadAsStringAsync();
        Assert.Contains(expectedContent, text);
    }

    [Fact]
    public async Task RootPath_RedirectsAutomaticallyToLogin()
    {
        var noRedirectClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var res = await noRedirectClient.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/login", res.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/login", "Iniciar Sesión")]
    [InlineData("/registro", "Crear Cuenta")]
    [InlineData("/dashboard", "Centro Canino")]
    [InlineData("/apiClient.js", "API_BASE_URL")]
    [InlineData("/app.js", "showDashboard")]
    [InlineData("/styles.css", "--primary")]
    public async Task FrontendStaticFiles_ServedDirectlyFromBackend(string path, string expectedSnippet)
    {
        var res = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var content = await res.Content.ReadAsStringAsync();
        Assert.Contains(expectedSnippet, content);
    }

    [Theory]
    [InlineData("admin", "12345")]
    [InlineData("ADMIN", "12345")]
    [InlineData("  AdMiN  ", "12345")]
    public async Task Login_ValidAdminVariations_Return200WithJwtWithoutRoles(string username, string password)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var raw = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);

        var auth = JsonSerializer.Deserialize<AuthResponse>(raw, TestHelpers.JsonOptions)!;
        Assert.Equal("Bearer", auth.TokenType);
        Assert.Equal("admin", auth.Usuario.Username);
        Assert.True(auth.ExpiresAt > DateTime.UtcNow);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.Token);
        Assert.DoesNotContain(jwt.Claims, c => c.Type.Contains("role", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("""{"username":"admin","password":"1234"}""")]                    // 4 dígitos
    [InlineData("""{"username":"admin","password":"123456789012345678901"}""")]   // 21 dígitos
    [InlineData("""{"username":"admin","password":"1234a"}""")]                   // letra al final
    [InlineData("""{"username":"admin","password":"a1234"}""")]                   // letra al inicio
    [InlineData("""{"username":"admin","password":" 12345"}""")]                  // espacio inicio
    [InlineData("""{"username":"admin","password":"12345 "}""")]                  // espacio final
    [InlineData("""{"username":"admin","password":"12 345"}""")]                  // espacio medio
    [InlineData("""{"username":"admin","password":"-12345"}""")]                  // negativo
    [InlineData("""{"username":"admin","password":"+12345"}""")]                  // signo +
    [InlineData("""{"username":"admin","password":"123.45"}""")]                  // decimal
    [InlineData("""{"username":"admin","password":12345}""")]                     // número JSON
    [InlineData("""{"username":"admin","password":true}""")]                      // booleano JSON
    [InlineData("""{"username":"admin","password":""}""")]                        // vacío
    [InlineData("""{"username":"admin","password":null}""")]                      // null
    [InlineData("""{"username":"ab","password":"12345"}""")]                      // username muy corto
    [InlineData("""{"username":"user con espacio","password":"12345"}""")]        // username con espacios
    [InlineData("""{"username":12345,"password":"12345"}""")]                     // username numérico JSON
    [InlineData("""{}""")]                                                         // body vacío objeto
    [InlineData("""{"username": """)]                                              // JSON malformado
    public async Task Login_InvalidPayloadOrTypes_Returns400(string rawJson)
    {
        using var content = TestHelpers.JsonContent(rawJson);
        var res = await _client.PostAsync("/api/auth/login", content);
        var error = await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
        Assert.NotEmpty(error.Errors);
    }

    [Theory]
    [InlineData("admin", "54321")]
    [InlineData("admin", "00000")]
    [InlineData("usuario.inexistente", "12345")]
    [InlineData("otro.desconocido", "99999")]
    public async Task Login_WrongCredentials_Returns401(string username, string password)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { username, password });
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("00001")]                  // 5 dígitos exactos con ceros a la izquierda
    [InlineData("12345678901234567890")]   // 20 dígitos exactos (límite superior)
    public async Task Register_ValidBoundaryPasswords_Returns201AndTokenWorks(string password)
    {
        var tag = TestHelpers.UniqueTag();
        var regRes = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            username = $"Reg.{tag}",
            password,
            documento = $"RG{tag}",
            nombreCompleto = $"Usuario Registro {tag}",
            telefono = "+57 315 555 9876",
            email = $"Reg.{tag}@Mail.com",
            direccion = "Carrera 7 # 45-10"
        });
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);
        Assert.NotNull(regRes.Headers.Location);

        var auth = await regRes.Content.ReadFromJsonAsync<AuthResponse>(TestHelpers.JsonOptions);
        Assert.NotNull(auth);
        Assert.Equal($"reg.{tag}", auth.Usuario.Username);

        // El token emitido por /register debe autenticar en endpoints protegidos
        var authedClient = _factory.CreateClient();
        authedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var meRes = await authedClient.GetAsync($"/api/usuarios/{auth.Usuario.Id}");
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);

        // Y el login posterior con ese password debe responder 200
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new { username = $"reg.{tag}", password });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
    }

    [Theory]
    [InlineData("""{"username":"abc","password":"123","documento":"12345","nombreCompleto":"Ana","telefono":"3001234567","email":"a@b.co","direccion":"Calle 123"}""")]
    [InlineData("""{}""")]
    [InlineData("""{"username": """)]
    public async Task Register_InvalidPayloads_Returns400(string rawJson)
    {
        using var content = TestHelpers.JsonContent(rawJson);
        var res = await _client.PostAsync("/api/auth/register", content);
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("username")]
    [InlineData("documento")]
    [InlineData("email")]
    public async Task Register_DuplicateUniqueField_Returns409(string duplicateField)
    {
        var tag1 = TestHelpers.UniqueTag();
        var tag2 = TestHelpers.UniqueTag();

        var first = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            username = $"dup_{tag1}",
            password = "12345",
            documento = $"DUP{tag1}",
            nombreCompleto = "Primero Uno",
            telefono = "3001112233",
            email = $"dup_{tag1}@mail.com",
            direccion = "Calle 10 # 20-30"
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            username = duplicateField == "username" ? $"DUP_{tag1}" : $"dup_{tag2}",
            password = "67890",
            documento = duplicateField == "documento" ? $"DUP{tag1}" : $"DUP{tag2}",
            nombreCompleto = "Segundo Dos",
            telefono = "3004445566",
            email = duplicateField == "email" ? $"DUP_{tag1}@MAIL.COM" : $"dup_{tag2}@mail.com",
            direccion = "Calle 20 # 30-40"
        });
        var error = await TestHelpers.AssertErrorAsync(second, HttpStatusCode.Conflict);
        Assert.True(error.Errors.ContainsKey(duplicateField));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer token-malformado")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIn0.firma-invalida")]
    [InlineData("Basic YWRtaW46MTIzNDU=")]
    public async Task ProtectedRoutes_InvalidOrMissingAuthHeader_Returns401(string? authHeader)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/usuarios");
        if (authHeader is not null)
        {
            req.Headers.TryAddWithoutValidation("Authorization", authHeader);
        }

        var res = await _client.SendAsync(req);
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/auth/login", "text/plain")]
    [InlineData("/api/auth/register", "application/xml")]
    public async Task AuthEndpoints_WrongContentType_Returns415(string path, string mediaType)
    {
        using var content = new StringContent("datos planos", Encoding.UTF8, mediaType);
        var res = await _client.PostAsync(path, content);
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.UnsupportedMediaType);
    }

    [Theory]
    [InlineData("GET", "/api/auth/login")]
    [InlineData("DELETE", "/api/health")]
    public async Task UnsupportedHttpMethods_Return405WithStandardEnvelope(string method, string path)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), path);
        var res = await _client.SendAsync(req);
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.MethodNotAllowed);
    }

    [Theory]
    [InlineData("/api/ruta-que-no-existe")]
    [InlineData("/api/otro/recurso/123")]
    public async Task UnknownRoutes_Return404WithStandardEnvelope(string path)
    {
        var res = await _client.GetAsync(path);
        await TestHelpers.AssertErrorAsync(res, HttpStatusCode.NotFound);
    }
}
