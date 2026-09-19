using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

public sealed record ProtectedOnboardingAsset(
    long AssetId,
    string StorageKey,
    string OriginalFileName,
    long SizeBytes,
    string Sha256,
    string MalwareScanStatus);

public static class ProtectedOnboardingAssetErrorCodes
{
    public const string FileRequired = "FILE_REQUIRED";
    public const string FileEmpty = "FILE_EMPTY";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string InvalidContext = "INVALID_CONTEXT";
    public const string SessionInvalid = "SESSION_INVALID";
    public const string ExtensionNotAllowed = "EXTENSION_NOT_ALLOWED";
    public const string SignatureMismatch = "SIGNATURE_MISMATCH";
    public const string MalwareThreat = "MALWARE_THREAT";
    public const string MalwareScanUnavailable = "MALWARE_SCAN_UNAVAILABLE";
    public const string MalwareScanError = "MALWARE_SCAN_ERROR";
    public const string StorageFailed = "STORAGE_FAILED";
}

public sealed record ProtectedOnboardingAssetSaveResult(
    ProtectedOnboardingAsset? Asset,
    string? ErrorCode);

public sealed record PromotedEmployeeFile(
    long AssetId,
    string StoredPath,
    string NewStorageKey,
    string OriginalStorageKey,
    string OriginalFileName);

public interface IOnboardingProtectedAssetService
{
    Task<ProtectedOnboardingAssetSaveResult> SaveAsync(
        IFormFile? file,
        int companyId,
        long sessionId,
        string category,
        int? createdBySystemUserId,
        CancellationToken cancellationToken = default);

    Task<PromotedEmployeeFile?> PromoteToEmployeeAsync(
        long assetId,
        int companyId,
        long sessionId,
        int employeeId,
        string category,
        CancellationToken cancellationToken = default);

    void TryDeleteOnboardingSource(string? storageKey);
}

public sealed class OnboardingProtectedAssetService : IOnboardingProtectedAssetService
{
    public const string RootFolderName = "ProtectedPeopleAssets";

    public static string ResolveRoot(string contentRootPath) =>
        Path.Combine(contentRootPath, "App_Data", RootFolderName);

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly IFileThreatScanner _threatScanner;
    private readonly MalwareScanningOptions _malwareOptions;
    private readonly ILogger<OnboardingProtectedAssetService> _logger;

    public OnboardingProtectedAssetService(
        ApplicationDbContext db,
        IWebHostEnvironment environment,
        IFileThreatScanner threatScanner,
        IOptions<MalwareScanningOptions> malwareOptions,
        ILogger<OnboardingProtectedAssetService> logger)
    {
        _db = db;
        _environment = environment;
        _threatScanner = threatScanner;
        _malwareOptions = malwareOptions.Value;
        _logger = logger;
    }

    private string Root => ResolveRoot(_environment.ContentRootPath);

    public async Task<ProtectedOnboardingAssetSaveResult> SaveAsync(
        IFormFile? file,
        int companyId,
        long sessionId,
        string category,
        int? createdBySystemUserId,
        CancellationToken cancellationToken = default)
    {
        if (file is null)
        {
            return new(null, ProtectedOnboardingAssetErrorCodes.FileRequired);
        }

        if (companyId <= 0 || sessionId <= 0)
        {
            return new(null, ProtectedOnboardingAssetErrorCodes.InvalidContext);
        }

        if (file.Length <= 0)
        {
            return new(null, ProtectedOnboardingAssetErrorCodes.FileEmpty);
        }

        if (file.Length > ProtectedFileStore.MaxFileSizeBytes)
        {
            _logger.LogInformation(
                "Rejected onboarding asset because size {SizeBytes} exceeds limit {LimitBytes}.",
                file.Length,
                ProtectedFileStore.MaxFileSizeBytes);
            return new(null, ProtectedOnboardingAssetErrorCodes.FileTooLarge);
        }

        var sessionExists = await HrmsDatabase.ScalarAsync<int>(
            _db,
            """
SELECT COUNT(*)
FROM dbo.EmployeeOnboardingSessions
WHERE Id = @SessionId
  AND CompanyId = @CompanyId
  AND Status NOT IN ('Completed', 'Cancelled');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            });

        if (sessionExists != 1)
        {
            return new(null, ProtectedOnboardingAssetErrorCodes.SessionInvalid);
        }

        var extension = Path.GetExtension(file.FileName);
        if (!ProtectedFileStore.IsAllowedExtension(extension))
        {
            return new(null, ProtectedOnboardingAssetErrorCodes.ExtensionNotAllowed);
        }

        if (!await UploadSignatureValidator.IsValidForExtensionAsync(file, extension))
        {
            _logger.LogInformation(
                "Rejected onboarding asset because binary signature does not match extension {Extension}.",
                extension);
            return new(null, ProtectedOnboardingAssetErrorCodes.SignatureMismatch);
        }

        var scan = await FileThreatPolicy.ScanUploadAsync(
            _threatScanner,
            file,
            cancellationToken);

        if (!FileThreatPolicy.CanStore(_malwareOptions, scan))
        {
            _logger.LogWarning(
                "Rejected onboarding asset due to malware verdict {Verdict}.",
                scan.Verdict);

            var errorCode = scan.Verdict switch
            {
                FileThreatScanVerdict.Threat =>
                    ProtectedOnboardingAssetErrorCodes.MalwareThreat,
                FileThreatScanVerdict.Unavailable =>
                    ProtectedOnboardingAssetErrorCodes.MalwareScanUnavailable,
                _ => ProtectedOnboardingAssetErrorCodes.MalwareScanError
            };

            return new(null, errorCode);
        }

        string sha256;
        await using (var hashStream = file.OpenReadStream())
        using (var sha = SHA256.Create())
        {
            sha256 = Convert.ToHexString(
                await sha.ComputeHashAsync(hashStream, cancellationToken))
                .ToLowerInvariant();
        }
        var safeCategory = Sanitize(category);
        var storageKey =
            $"company-{companyId}/onboarding-{sessionId}/{safeCategory}_{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

        await using (var source = file.OpenReadStream())
        {
            if (!await ProtectedFileStore.SaveAsync(
                    Root,
                    storageKey,
                    source,
                    cancellationToken))
            {
                return new(null, ProtectedOnboardingAssetErrorCodes.StorageFailed);
            }
        }

        try
        {
            var assetId = await HrmsDatabase.ScalarAsync<long>(
                _db,
                """
INSERT INTO dbo.ProtectedFileAssets
    (CompanyId, OwnerType, OwnerId, StorageKey, OriginalFileName,
     MimeType, Extension, SizeBytes, Sha256, SignatureValidationStatus,
     MalwareScanStatus, CreatedBySystemUserId)
OUTPUT INSERTED.Id
VALUES
    (@CompanyId, 'OnboardingSession', @OwnerId, @StorageKey, @OriginalFileName,
     @MimeType, @Extension, @SizeBytes, @Sha256, 'Valid',
     @MalwareStatus, @CreatedBySystemUserId);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                    HrmsDatabase.AddParameter(command, "@OwnerId", sessionId);
                    HrmsDatabase.AddParameter(command, "@StorageKey", storageKey);
                    HrmsDatabase.AddParameter(
                        command, "@OriginalFileName", Path.GetFileName(file.FileName));
                    HrmsDatabase.AddParameter(
                        command, "@MimeType", ProtectedFileStore.ContentTypeFor(extension));
                    HrmsDatabase.AddParameter(command, "@Extension", extension.ToLowerInvariant());
                    HrmsDatabase.AddParameter(command, "@SizeBytes", file.Length);
                    HrmsDatabase.AddParameter(command, "@Sha256", sha256);
                    HrmsDatabase.AddParameter(command, "@MalwareStatus", scan.Verdict.ToString());
                    HrmsDatabase.AddParameter(
                        command, "@CreatedBySystemUserId",
                        (object?)createdBySystemUserId ?? DBNull.Value);
                });

            return new ProtectedOnboardingAssetSaveResult(
                new ProtectedOnboardingAsset(
                    assetId,
                    storageKey,
                    Path.GetFileName(file.FileName),
                    file.Length,
                    sha256,
                    scan.Verdict.ToString()),
                null);
        }
        catch
        {
            ProtectedFileStore.TryDelete(Root, storageKey);
            throw;
        }
    }

    public async Task<PromotedEmployeeFile?> PromoteToEmployeeAsync(
        long assetId,
        int companyId,
        long sessionId,
        int employeeId,
        string category,
        CancellationToken cancellationToken = default)
    {
        if (assetId <= 0 || companyId <= 0 || sessionId <= 0 || employeeId <= 0)
        {
            return null;
        }

        var rows = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 1 StorageKey, OriginalFileName, Extension
FROM dbo.ProtectedFileAssets
WHERE Id = @AssetId
  AND CompanyId = @CompanyId
  AND OwnerType = 'OnboardingSession'
  AND OwnerId = @SessionId
  AND DeletedAt IS NULL;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@AssetId", assetId);
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(command, "@SessionId", sessionId);
            },
            reader => new
            {
                StorageKey = HrmsDatabase.GetString(reader, "StorageKey"),
                OriginalFileName = HrmsDatabase.GetString(reader, "OriginalFileName"),
                Extension = HrmsDatabase.GetString(reader, "Extension")
            });

        var asset = rows.FirstOrDefault();
        if (asset is null ||
            !ProtectedFileStore.TryResolvePhysicalPath(
                Root,
                asset.StorageKey,
                out var sourcePath) ||
            !File.Exists(sourcePath))
        {
            return null;
        }

        var extension = asset.Extension;
        if (!ProtectedFileStore.IsAllowedExtension(extension))
        {
            extension = Path.GetExtension(asset.OriginalFileName);
        }

        if (!ProtectedFileStore.IsAllowedExtension(extension))
        {
            return null;
        }

        var employeeRoot =
            ProtectedFileStore.ResolveRoot(_environment.ContentRootPath);
        var newKey = ProtectedFileStore.BuildStorageKey(
            employeeId,
            Sanitize(category),
            extension);

        await using var source = File.OpenRead(sourcePath);
        var saved = await ProtectedFileStore.SaveAsync(
            employeeRoot,
            newKey,
            source,
            cancellationToken);

        if (!saved)
        {
            return null;
        }

        return new PromotedEmployeeFile(
            assetId,
            ProtectedFileReference.ToStoredPath(newKey),
            newKey,
            asset.StorageKey,
            asset.OriginalFileName);
    }

    public void TryDeleteOnboardingSource(string? storageKey)
    {
        if (!string.IsNullOrWhiteSpace(storageKey))
        {
            ProtectedFileStore.TryDelete(Root, storageKey);
        }
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "document";
        }

        var cleaned = new string(value
            .Trim()
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .ToArray());

        return cleaned.Length == 0
            ? "document"
            : cleaned[..Math.Min(cleaned.Length, 40)];
    }
}
