using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SmartAttendance.Web.Infrastructure.PeopleAi;

public sealed class PeopleAiWorkerOptions
{
    public const string SectionName = "PeopleAIWorker";

    public bool Enabled { get; set; }
    public string PythonExecutable { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = "PeopleAI/local_ocr_worker.py";
    public int PollSeconds { get; set; } = 2;
    public int StartupTimeoutSeconds { get; set; } = 180;
    public int JobTimeoutSeconds { get; set; } = 180;
    public int MaxImageSide { get; set; } = 1600;
    public int PdfMaxPages { get; set; } = 20;
    public int PdfRenderDpi { get; set; } = 180;
    public string Device { get; set; } = "auto";
    public string Language { get; set; } = "ar";
    public string TempDirectory { get; set; } = string.Empty;

    public bool IsUsable =>
        Enabled &&
        !string.IsNullOrWhiteSpace(PythonExecutable) &&
        !string.IsNullOrWhiteSpace(ScriptPath);
}

public sealed record LocalOcrLine(
    int Index,
    string Text,
    double? Score,
    int[]? Box);

public sealed record LocalOcrPage(
    int PageIndex,
    List<LocalOcrLine> Lines);

public sealed record LocalOcrResponse(
    bool Success,
    string? RequestId,
    string? Provider,
    string? Model,
    string? Language,
    List<LocalOcrPage>? Pages,
    string? FullText,
    int LineCount,
    string? ErrorType,
    string? Error)
{
    public IEnumerable<LocalOcrLine> AllLines =>
        Pages?.SelectMany(page => page.Lines) ?? [];
}

public interface ILocalOcrProcessClient
{
    bool IsEnabled { get; }

    Task EnsureReadyAsync(
        CancellationToken cancellationToken = default);

    Task<LocalOcrResponse> ExtractAsync(
        string physicalPath,
        CancellationToken cancellationToken = default);

    Task<LocalOcrResponse> ExtractAsync(
        string physicalPath,
        string? languageProfile,
        CancellationToken cancellationToken = default) =>
        ExtractAsync(physicalPath, cancellationToken);
}

public sealed class LocalOcrProcessClient :
    ILocalOcrProcessClient,
    IAsyncDisposable
{
    private readonly PeopleAiWorkerOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LocalOcrProcessClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private Process? _process;

    public LocalOcrProcessClient(
        IOptions<PeopleAiWorkerOptions> options,
        IWebHostEnvironment environment,
        ILogger<LocalOcrProcessClient> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public bool IsEnabled => _options.IsUsable;

    public async Task EnsureReadyAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException(
                "People AI local OCR worker is disabled.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureStartedAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<LocalOcrResponse> ExtractAsync(
        string physicalPath,
        CancellationToken cancellationToken = default) =>
        ExtractAsync(
            physicalPath,
            _options.Language,
            cancellationToken);

    public async Task<LocalOcrResponse> ExtractAsync(
        string physicalPath,
        string? languageProfile,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException(
                "People AI local OCR worker is disabled.");
        }

        if (!File.Exists(physicalPath))
        {
            throw new FileNotFoundException(
                "Protected onboarding document was not found.",
                physicalPath);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureStartedAsync(cancellationToken);

            var requestId = Guid.NewGuid().ToString("N");
            var request = JsonSerializer.Serialize(new
            {
                requestId,
                path = physicalPath,
                language = string.IsNullOrWhiteSpace(languageProfile)
                    ? _options.Language
                    : languageProfile.Trim()
            });

            await _process!.StandardInput.WriteLineAsync(request);
            await _process.StandardInput.FlushAsync();

            using var timeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(
                Math.Clamp(_options.JobTimeoutSeconds, 30, 900)));

            while (true)
            {
                var line = await _process.StandardOutput
                    .ReadLineAsync(timeout.Token);

                if (line is null)
                {
                    throw new InvalidOperationException(
                        "Local OCR process ended before returning a result.");
                }

                LocalOcrResponse? response;
                try
                {
                    response = JsonSerializer.Deserialize<LocalOcrResponse>(
                        line,
                        _json);
                }
                catch (JsonException)
                {
                    _logger.LogDebug(
                        "Ignored non-JSON output from local OCR process.");
                    continue;
                }

                if (response is null ||
                    !string.Equals(
                        response.RequestId,
                        requestId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                return response;
            }
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            StopProcess();
            throw new TimeoutException(
                "Local OCR job exceeded the configured timeout.");
        }
        catch
        {
            StopProcess();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStartedAsync(
        CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        StopProcess();

        var configuredPython = _options.PythonExecutable.Trim();
        var pythonPath = configuredPython.IndexOfAny(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || Path.IsPathRooted(configuredPython)
                ? Path.GetFullPath(configuredPython)
                : configuredPython;

        var scriptPath = Path.IsPathRooted(_options.ScriptPath)
            ? Path.GetFullPath(_options.ScriptPath)
            : Path.GetFullPath(Path.Combine(
                _environment.ContentRootPath,
                _options.ScriptPath));

        if (Path.IsPathRooted(pythonPath) && !File.Exists(pythonPath))
        {
            throw new FileNotFoundException(
                "Configured People AI Python executable was not found.",
                pythonPath);
        }

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                "Configured People AI OCR worker script was not found.",
                scriptPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            WorkingDirectory = _environment.ContentRootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false)
        };

        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--serve");
        startInfo.Environment["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True";
        startInfo.Environment["FLAGS_minloglevel"] = "2";
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PEOPLE_AI_OCR_MAX_IMAGE_SIDE"] =
            Math.Clamp(_options.MaxImageSide, 1200, 4096).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["PEOPLE_AI_PDF_MAX_PAGES"] =
            Math.Clamp(_options.PdfMaxPages, 1, 100).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["PEOPLE_AI_PDF_RENDER_DPI"] =
            Math.Clamp(_options.PdfRenderDpi, 120, 300).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["PEOPLE_AI_OCR_DEVICE"] =
            string.IsNullOrWhiteSpace(_options.Device)
                ? "auto"
                : _options.Device.Trim().ToLowerInvariant();
        startInfo.Environment["PEOPLE_AI_OCR_LANGUAGE"] =
            string.IsNullOrWhiteSpace(_options.Language)
                ? "ar"
                : _options.Language.Trim();

        if (!string.IsNullOrWhiteSpace(_options.TempDirectory))
        {
            var tempDirectory = Path.IsPathRooted(_options.TempDirectory)
                ? Path.GetFullPath(_options.TempDirectory)
                : Path.GetFullPath(Path.Combine(
                    _environment.ContentRootPath,
                    _options.TempDirectory));
            startInfo.Environment["PEOPLE_AI_OCR_TEMP_DIRECTORY"] =
                tempDirectory;
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException(
                "Failed to start local OCR process.");
        }

        _process = process;
        _ = PumpStandardErrorAsync(process);

        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(
            Math.Clamp(_options.StartupTimeoutSeconds, 30, 600)));

        while (true)
        {
            var line = await process.StandardOutput
                .ReadLineAsync(timeout.Token);

            if (line is null)
            {
                throw new InvalidOperationException(
                    "Local OCR process exited during startup.");
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty(
                        "ready",
                        out var ready) &&
                    ready.ValueKind == JsonValueKind.False)
                {
                    var root = document.RootElement;
                    var errorType = root.TryGetProperty(
                        "errorType",
                        out var errorTypeValue)
                        ? errorTypeValue.GetString()
                        : "StartupError";
                    var error = root.TryGetProperty(
                        "error",
                        out var errorValue)
                        ? errorValue.GetString()
                        : "Unknown OCR startup failure.";

                    throw new InvalidOperationException(
                        $"People AI OCR startup failed ({errorType}): {error}");
                }

                if (document.RootElement.TryGetProperty(
                        "ready",
                        out ready) &&
                    ready.ValueKind == JsonValueKind.True)
                {
                    var root = document.RootElement;
                    var device = root.TryGetProperty("device", out var deviceValue)
                        ? deviceValue.GetString()
                        : null;
                    var language = root.TryGetProperty("language", out var languageValue)
                        ? languageValue.GetString()
                        : null;
                    var model = root.TryGetProperty("model", out var modelValue)
                        ? modelValue.GetString()
                        : null;

                    _logger.LogInformation(
                        "People AI local OCR process is ready. Model={Model} Device={Device} Language={Language}.",
                        model,
                        device,
                        language);
                    return;
                }
            }
            catch (JsonException)
            {
                // Model startup diagnostics must never be interpreted as data.
            }
        }
    }

    private async Task PumpStandardErrorAsync(Process process)
    {
        try
        {
            while (!process.HasExited)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line is null)
                {
                    break;
                }

                // Python/Paddle diagnostics can contain local file paths in tracebacks.
                // Keep raw stderr visible only in Development; Production/Staging logs receive
                // a content-free diagnostic marker so protected paths/PII cannot leak.
                if (_environment.IsDevelopment())
                {
                    _logger.LogDebug("Local OCR runtime: {Message}", line);
                }
                else
                {
                    _logger.LogDebug("Local OCR runtime diagnostic received.");
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Local OCR stderr pump stopped.");
        }
    }

    private void StopProcess()
    {
        var process = _process;
        _process = null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
        finally
        {
            process.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        StopProcess();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
