namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// يتحقق من التوقيع الثنائي الحقيقي للملف المرفوع بدل الاكتفاء بالامتداد،
/// حتى لا يُقبل ملف بامتداد صورة ومحتوى مختلف.
/// </summary>
public static class UploadSignatureValidator
{
    public static async Task<bool> IsValidImageAsync(IFormFile file)
    {
        var header = await ReadHeaderAsync(file, 12);
        return IsPng(header) || IsJpeg(header) || IsWebp(header);
    }

    public static async Task<bool> IsValidForExtensionAsync(IFormFile file, string extension)
    {
        var header = await ReadHeaderAsync(file, 12);

        return extension.ToLowerInvariant() switch
        {
            ".png" => IsPng(header),
            ".jpg" or ".jpeg" => IsJpeg(header),
            ".webp" => IsWebp(header),
            ".pdf" => StartsWith(header, (byte)'%', (byte)'P', (byte)'D', (byte)'F'),
            ".doc" or ".xls" => StartsWith(header, 0xD0, 0xCF, 0x11, 0xE0),
            ".docx" or ".xlsx" => StartsWith(header, 0x50, 0x4B, 0x03, 0x04),
            _ => false
        };
    }

    private static async Task<byte[]> ReadHeaderAsync(IFormFile file, int length)
    {
        await using var stream = file.OpenReadStream();
        var buffer = new byte[length];
        var read = 0;

        while (read < length)
        {
            var chunk = await stream.ReadAsync(buffer.AsMemory(read, length - read));

            if (chunk == 0)
            {
                break;
            }

            read += chunk;
        }

        return read == length ? buffer : buffer[..read];
    }

    private static bool IsPng(byte[] header) =>
        StartsWith(header, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);

    private static bool IsJpeg(byte[] header) =>
        StartsWith(header, 0xFF, 0xD8, 0xFF);

    private static bool IsWebp(byte[] header) =>
        header.Length >= 12 &&
        StartsWith(header, (byte)'R', (byte)'I', (byte)'F', (byte)'F') &&
        header[8] == (byte)'W' &&
        header[9] == (byte)'E' &&
        header[10] == (byte)'B' &&
        header[11] == (byte)'P';

    private static bool StartsWith(byte[] header, params byte[] signature)
    {
        if (header.Length < signature.Length)
        {
            return false;
        }

        for (var i = 0; i < signature.Length; i++)
        {
            if (header[i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }
}
