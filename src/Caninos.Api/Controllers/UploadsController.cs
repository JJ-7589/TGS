using Caninos.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Caninos.Api.Controllers;

[ApiController]
[Route("api/uploads")]
[Authorize]
public sealed class UploadsController : ControllerBase
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private readonly IWebHostEnvironment _env;

    public UploadsController(IWebHostEnvironment env)
    {
        _env = env;
    }

    [HttpPost("mascotas")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<IActionResult> UploadMascotaPhoto(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            throw ApiValidationException.Single("file", "Debes adjuntar una imagen válida (JPG, PNG o WEBP).");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            throw ApiValidationException.Single("file", "La imagen supera el tamaño máximo permitido de 5 MB.");
        }

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
        {
            throw ApiValidationException.Single("file", "Formato no permitido. Solo se aceptan imágenes .jpg, .jpeg, .png o .webp.");
        }

        if (string.IsNullOrWhiteSpace(file.ContentType) || !AllowedContentTypes.Contains(file.ContentType))
        {
            throw ApiValidationException.Single("file", "El tipo MIME del archivo debe ser image/jpeg, image/png o image/webp.");
        }

        // Validación de cabecera binaria (magic bytes)
        await using var input = file.OpenReadStream();
        var header = new byte[12];
        var bytesRead = await input.ReadAsync(header.AsMemory(0, header.Length));
        if (!HasValidImageSignature(header, bytesRead))
        {
            throw ApiValidationException.Single("file", "El archivo no corresponde a una imagen JPG, PNG o WEBP válida.");
        }

        input.Position = 0;

        var uploadsDir = Path.Combine(_env.ContentRootPath, "storage", "uploads", "mascotas");
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var fullPath = Path.Combine(uploadsDir, fileName);

        await using (var output = System.IO.File.Create(fullPath))
        {
            await input.CopyToAsync(output);
        }

        var publicUrl = $"/uploads/mascotas/{fileName}";
        return Created(publicUrl, new { fotoUrl = publicUrl });
    }

    private static bool HasValidImageSignature(byte[] header, int length)
    {
        // JPEG: FF D8 FF
        if (length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return true;
        }

        // WEBP: RIFF....WEBP
        if (length >= 12 &&
            header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F' &&
            header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            return true;
        }

        return false;
    }
}
