using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace SmartAttendance.Web.Infrastructure.Security;

public enum FileThreatScanVerdict
{
    Clean,
    Threat,
    Unavailable,
    Error
}

public sealed record FileThreatScanResult(FileThreatScanVerdict Verdict, string? Detail = null)
{
    public static readonly FileThreatScanResult Clean = new(FileThreatScanVerdict.Clean);
}

public interface IFileThreatScanner
{
    Task<FileThreatScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default);
}

public static class FileThreatPolicy
{
    public static bool CanStore(MalwareScanningOptions options, FileThreatScanResult result) =>
        result.Verdict == FileThreatScanVerdict.Clean
        || (!options.Required && result.Verdict == FileThreatScanVerdict.Unavailable);

    public static async Task<FileThreatScanResult> ScanUploadAsync(
        IFileThreatScanner scanner,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        await using var stream = file.OpenReadStream();
        return await scanner.ScanAsync(stream, cancellationToken);
    }
}

public sealed class DisabledFileThreatScanner : IFileThreatScanner
{
    public Task<FileThreatScanResult> ScanAsync(
        Stream content,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileThreatScanResult(
            FileThreatScanVerdict.Unavailable,
            "Malware scanner is not configured."));
}

/// <summary>
/// عميل بروتوكول ClamAV INSTREAM. يرسل الملف على دفعات محدودة ولا يكتبه إلى
/// مكان الحجر/التخزين قبل ظهور نتيجة نظيفة.
/// </summary>
public sealed class ClamAvFileThreatScanner : IFileThreatScanner
{
    private const int ChunkSize = 64 * 1024;
    private const int MaxResponseBytes = 8 * 1024;
    private static readonly byte[] Command = Encoding.ASCII.GetBytes("zINSTREAM\0");

    private readonly MalwareScanningOptions _options;
    private readonly ILogger<ClamAvFileThreatScanner> _logger;

    public ClamAvFileThreatScanner(
        IOptions<MalwareScanningOptions> options,
        ILogger<ClamAvFileThreatScanner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FileThreatScanResult> ScanAsync(
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsUsable)
        {
            return new FileThreatScanResult(
                FileThreatScanVerdict.Unavailable,
                "ClamAV endpoint is incomplete.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.Host, _options.Port, timeout.Token);
            await using var network = client.GetStream();

            await network.WriteAsync(Command, timeout.Token);

            var buffer = new byte[ChunkSize];
            var prefix = new byte[sizeof(int)];
            int read;

            while ((read = await content.ReadAsync(buffer, timeout.Token)) > 0)
            {
                BinaryPrimitives.WriteInt32BigEndian(prefix, read);
                await network.WriteAsync(prefix, timeout.Token);
                await network.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            Array.Clear(prefix);
            await network.WriteAsync(prefix, timeout.Token);
            await network.FlushAsync(timeout.Token);

            var responseBuffer = new byte[MaxResponseBytes];
            var responseLength = 0;

            while (responseLength < responseBuffer.Length)
            {
                var count = await network.ReadAsync(
                    responseBuffer.AsMemory(responseLength), timeout.Token);
                if (count == 0) break;
                responseLength += count;
                if (responseBuffer.AsSpan(0, responseLength).Contains((byte)0)) break;
            }

            var response = Encoding.UTF8.GetString(responseBuffer, 0, responseLength)
                .TrimEnd('\0', '\r', '\n', ' ');

            return ParseResponse(response);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("انتهت مهلة فحص malware بعد {Seconds} ثانية.", _options.TimeoutSeconds);
            return new FileThreatScanResult(FileThreatScanVerdict.Unavailable, "Scanner timeout.");
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "تعذّر الاتصال بمحرك ClamAV على {Host}:{Port}.", _options.Host, _options.Port);
            return new FileThreatScanResult(FileThreatScanVerdict.Unavailable, "Scanner connection failed.");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "فشل تبادل بيانات فحص malware.");
            return new FileThreatScanResult(FileThreatScanVerdict.Error, "Scanner I/O failed.");
        }
    }

    public static FileThreatScanResult ParseResponse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return new FileThreatScanResult(FileThreatScanVerdict.Error, "Empty scanner response.");

        if (response.EndsWith(" OK", StringComparison.OrdinalIgnoreCase))
            return FileThreatScanResult.Clean;

        if (response.EndsWith(" FOUND", StringComparison.OrdinalIgnoreCase))
            return new FileThreatScanResult(FileThreatScanVerdict.Threat, response);

        return new FileThreatScanResult(FileThreatScanVerdict.Error, response);
    }
}
