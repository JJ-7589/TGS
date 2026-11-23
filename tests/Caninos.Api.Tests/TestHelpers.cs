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

namespace Caninos.Api.Tests;

internal static class TestHelpers
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string UniqueTag() => Guid.NewGuid().ToString("N")[..8];

    public static async Task<string> LoginAsAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
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

    public static async Task<HttpClient> CreateAuthorizedClientAsync(CustomWebApplicationFactory factory)
    {
        var anon = factory.CreateClient();
        var token = await LoginAsAdminAsync(anon);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<ApiErrorResponse> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var error = JsonSerializer.Deserialize<ApiErrorResponse>(body, JsonOptions);
        Assert.NotNull(error);
        Assert.Equal((int)expectedStatus, error.Status);
        Assert.False(string.IsNullOrWhiteSpace(error.Error));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.NotNull(error.Errors);
        return error;
    }

    public static async Task<UsuarioResponse> CreateSampleUsuarioAsync(
        HttpClient authClient,
        string? tag = null,
        string password = "12345")
    {
        tag ??= UniqueTag();
        var res = await authClient.PostAsJsonAsync("/api/usuarios", new
        {
            username = $"usr_{tag}",
            password,
            documento = $"DOC{tag}",
            nombreCompleto = $"Cliente Prueba {tag}",
            telefono = "3105551234",
            email = $"usr_{tag}@pruebas.local",
            direccion = $"Calle {tag} # 10-20"
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.Content.ReadFromJsonAsync<UsuarioResponse>(JsonOptions);
        Assert.NotNull(created);
        return created;
    }

    public static async Task<MascotaResponse> CreateSampleMascotaAsync(
        HttpClient authClient,
        int userId,
        string? nombre = null,
        string raza = "Labrador")
    {
        var tag = UniqueTag();
        var res = await authClient.PostAsJsonAsync("/api/mascotas", new
        {
            nombre = nombre ?? $"Can_{tag}",
            raza,
            edad = 4,
            pesoKg = 20.5m,
            sexo = "Macho",
            color = "Dorado",
            esterilizado = true,
            observaciones = "Mascota de prueba",
            userId
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.Content.ReadFromJsonAsync<MascotaResponse>(JsonOptions);
        Assert.NotNull(created);
        return created;
    }

    public static async Task<ConsultaResponse> CreateSampleConsultaAsync(
        HttpClient authClient,
        int userId,
        int mascotaId,
        string tipoServicio = "Salud",
        string estado = "Programada",
        string fechaConsulta = "2026-11-25T10:00:00Z",
        decimal costo = 50000m)
    {
        var res = await authClient.PostAsJsonAsync("/api/consultas", new
        {
            userId,
            mascotaId,
            tipoServicio,
            descripcion = $"Servicio de {tipoServicio} para mascota {mascotaId}",
            fechaConsulta,
            costo,
            estado,
            observaciones = "Observación inicial"
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = await res.Content.ReadFromJsonAsync<ConsultaResponse>(JsonOptions);
        Assert.NotNull(created);
        return created;
    }

    public static StringContent JsonContent(string rawJson) =>
        new(rawJson, Encoding.UTF8, "application/json");
}
