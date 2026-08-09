using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using SmartAttendance.Application.AttendanceImports.Services;

namespace SmartAttendance.Web.Infrastructure.Imports;

/// <summary>
/// Private, expiring and single-use staging for attendance uploads. The protected
/// token binds the opaque file id to the authenticated actor and selected company;
/// filenames are never accepted from the client as storage paths.
/// </summary>
public sealed class AttendanceImportStagingStore
{
    private const string ProtectorPurpose = "ZYNORA.AttendanceImportStaging.v1";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AbandonedFileLifetime = TimeSpan.FromHours(1);

    private readonly ITimeLimitedDataProtector _protector;
    private readonly IWebHostEnvironment _environment;

    public AttendanceImportStagingStore(
        IDataProtectionProvider dataProtectionProvider,
        IWebHostEnvironment environment)
    {
        _protector = dataProtectionProvider
            .CreateProtector(ProtectorPurpose)
            .ToTimeLimitedDataProtector();
        _environment = environment;
    }

    private string Root => Path.Combine(
        _environment.ContentRootPath,
        "App_Data",
        "AttendanceImports");

    public async Task<AttendanceStagedUpload> SaveAsync(
        IFormFile file,
        string actorKey,
        int companyId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrWhiteSpace(actorKey) || companyId <= 0)
        {
            throw new UnauthorizedAccessException("Attendance import staging requires an authenticated actor and company.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not ".csv" and not ".xlsx")
        {
            throw new InvalidOperationException("Only CSV and XLSX attendance files are supported.");
        }
        if (file.Length <= 0 || file.Length > AttendanceImportLimits.MaxCompressedBytes)
        {
            throw new InvalidOperationException("Attendance import file exceeds the configured size limit.");
        }

        Directory.CreateDirectory(Root);
        CleanupAbandoned();

        var id = Guid.NewGuid().ToString("N");
        var pendingPath = ResolveContainedPath($"{id}.pending{extension}");

        await using (var destination = new FileStream(
            pendingPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await file.CopyToAsync(destination, cancellationToken);
        }

        var payload = new StagingPayload(
            id,
            actorKey,
            companyId,
            Path.GetFileName(file.FileName),
            extension);
        var token = _protector.Protect(JsonSerializer.Serialize(payload), TokenLifetime);

        return new AttendanceStagedUpload(token, pendingPath, payload.OriginalFileName, companyId);
    }

    public AttendanceStagedUpload Claim(
        string token,
        string actorKey,
        int companyId)
    {
        var payload = Unprotect(token);
        if (!FixedEquals(payload.ActorKey, actorKey) || payload.CompanyId != companyId)
        {
            throw new UnauthorizedAccessException("The attendance import token does not belong to this user and company.");
        }

        var pendingPath = ResolveContainedPath($"{payload.Id}.pending{payload.Extension}");
        var processingPath = ResolveContainedPath($"{payload.Id}.processing{payload.Extension}");

        if (!File.Exists(pendingPath))
        {
            throw new InvalidOperationException("The attendance import token is expired, already used, or missing.");
        }

        // Atomic rename is the one-time claim: a replay cannot claim the same file.
        File.Move(pendingPath, processingPath, overwrite: false);
        return new AttendanceStagedUpload(token, processingPath, payload.OriginalFileName, companyId);
    }

    public void Delete(AttendanceStagedUpload upload)
    {
        var fullPath = Path.GetFullPath(upload.FilePath);
        EnsureContained(fullPath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    public void CleanupAbandoned()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        var cutoff = DateTime.UtcNow - AbandonedFileLifetime;
        foreach (var path in Directory.EnumerateFiles(Root, "*.*", SearchOption.TopDirectoryOnly))
        {
            var fullPath = Path.GetFullPath(path);
            EnsureContained(fullPath);
            if (File.GetLastWriteTimeUtc(fullPath) < cutoff)
            {
                try
                {
                    File.Delete(fullPath);
                }
                catch (IOException)
                {
                    // A currently claimed/importing file is left for the next cleanup.
                }
                catch (UnauthorizedAccessException)
                {
                    // Fail the upload operation only for its own file, not for stale cleanup.
                }
            }
        }
    }

    private StagingPayload Unprotect(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new UnauthorizedAccessException("Attendance import token is missing.");
        }

        try
        {
            var payload = JsonSerializer.Deserialize<StagingPayload>(_protector.Unprotect(token));
            if (payload is null ||
                !Guid.TryParseExact(payload.Id, "N", out _) ||
                payload.CompanyId <= 0 ||
                string.IsNullOrWhiteSpace(payload.ActorKey) ||
                payload.Extension is not ".csv" and not ".xlsx")
            {
                throw new UnauthorizedAccessException("Attendance import token is invalid.");
            }

            return payload;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is CryptographicException or JsonException or FormatException)
        {
            throw new UnauthorizedAccessException("Attendance import token is invalid or expired.", exception);
        }
    }

    private string ResolveContainedPath(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(Root, fileName));
        EnsureContained(path);
        return path;
    }

    private void EnsureContained(string path)
    {
        var root = Path.GetFullPath(Root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Attendance import staging path escaped its private root.");
        }
    }

    private static bool FixedEquals(string left, string right) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(left),
            System.Text.Encoding.UTF8.GetBytes(right));

    private sealed record StagingPayload(
        string Id,
        string ActorKey,
        int CompanyId,
        string OriginalFileName,
        string Extension);
}

public sealed record AttendanceStagedUpload(
    string Token,
    string FilePath,
    string OriginalFileName,
    int CompanyId);
