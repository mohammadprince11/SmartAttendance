using System.Data.Common;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Theming;

/// <summary>
/// Self-healing persistence for the Branding &amp; Theme Engine (Phase P5),
/// following the Hrms store pattern (idempotent CREATE TABLE, raw ADO via
/// <see cref="HrmsDatabase"/>, zero EF migrations, no change to Company).
///
/// Tables:
///  - CompanyBrandingProfiles: the current editable brand input + assets per company.
///  - ThemeVersions: immutable compiled outputs with a lifecycle
///    (Draft → Validated → Published → Archived / Rejected). Publishing archives
///    the previously published row, so rollback is republishing an older one.
///  - UserAppearancePreferences: per-user appearance opt-ins (used from P7).
/// </summary>
public static class ThemeStore
{
    public const string StatusDraft = "Draft";
    public const string StatusValidated = "Validated";
    public const string StatusPublished = "Published";
    public const string StatusArchived = "Archived";
    public const string StatusRejected = "Rejected";

    public sealed class BrandingProfile
    {
        public int CompanyId { get; set; }
        public string PrimaryHex { get; set; } = string.Empty;
        public string? SecondaryHex { get; set; }
        public string? AccentHex { get; set; }
        public string? DisplayName { get; set; }
        public string? LogoPath { get; set; }
        public string? FaviconPath { get; set; }
        public string? LoginBackgroundPath { get; set; }
    }

    public sealed class ThemeVersion
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string Status { get; set; } = StatusDraft;
        public string CompiledCss { get; set; } = string.Empty;
        public string ValidationLevel { get; set; } = string.Empty;
        public string? ValidationJson { get; set; }
        public string? BrandingSnapshotJson { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
    }

    public sealed record PublishedTheme(
        int VersionId,
        string CompiledCss,
        string? DisplayName,
        string? LogoPath,
        string? FaviconPath);

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('CompanyBrandingProfiles', 'U') IS NULL
BEGIN
    CREATE TABLE CompanyBrandingProfiles
    (
        CompanyId int NOT NULL PRIMARY KEY,
        PrimaryHex nvarchar(9) NOT NULL,
        SecondaryHex nvarchar(9) NULL,
        AccentHex nvarchar(9) NULL,
        DisplayName nvarchar(150) NULL,
        LogoPath nvarchar(500) NULL,
        FaviconPath nvarchar(500) NULL,
        LoginBackgroundPath nvarchar(500) NULL,
        UpdatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('ThemeVersions', 'U') IS NULL
BEGIN
    CREATE TABLE ThemeVersions
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompanyId int NOT NULL,
        Status nvarchar(20) NOT NULL DEFAULT('Draft'),
        CompiledCss nvarchar(max) NOT NULL,
        ValidationLevel nvarchar(20) NOT NULL DEFAULT('Pass'),
        ValidationJson nvarchar(max) NULL,
        BrandingSnapshotJson nvarchar(max) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        PublishedAt datetime2 NULL
    );
    CREATE INDEX IX_ThemeVersions_Company_Status ON ThemeVersions (CompanyId, Status);
END;

IF OBJECT_ID('UserAppearancePreferences', 'U') IS NULL
BEGIN
    CREATE TABLE UserAppearancePreferences
    (
        UserName nvarchar(150) NOT NULL PRIMARY KEY,
        PrefsJson nvarchar(max) NULL,
        UpdatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;
""");
    }

    // ---- Branding profile --------------------------------------------------

    public static async Task<BrandingProfile?> GetBrandingProfileAsync(
        ApplicationDbContext dbContext, int companyId)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM CompanyBrandingProfiles WHERE CompanyId = @CompanyId;",
            command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId),
            ReadProfile);
        return rows.FirstOrDefault();
    }

    public static async Task SaveBrandingProfileAsync(
        ApplicationDbContext dbContext, BrandingProfile profile)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF EXISTS (SELECT 1 FROM CompanyBrandingProfiles WHERE CompanyId = @CompanyId)
    UPDATE CompanyBrandingProfiles SET
        PrimaryHex = @PrimaryHex, SecondaryHex = @SecondaryHex, AccentHex = @AccentHex,
        DisplayName = @DisplayName, LogoPath = @LogoPath, FaviconPath = @FaviconPath,
        LoginBackgroundPath = @LoginBackgroundPath, UpdatedAt = SYSUTCDATETIME()
    WHERE CompanyId = @CompanyId;
ELSE
    INSERT INTO CompanyBrandingProfiles
        (CompanyId, PrimaryHex, SecondaryHex, AccentHex, DisplayName, LogoPath, FaviconPath, LoginBackgroundPath)
    VALUES
        (@CompanyId, @PrimaryHex, @SecondaryHex, @AccentHex, @DisplayName, @LogoPath, @FaviconPath, @LoginBackgroundPath);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", profile.CompanyId);
                HrmsDatabase.AddParameter(command, "@PrimaryHex", profile.PrimaryHex);
                HrmsDatabase.AddParameter(command, "@SecondaryHex", (object?)profile.SecondaryHex ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@AccentHex", (object?)profile.AccentHex ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@DisplayName", (object?)profile.DisplayName ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@LogoPath", (object?)profile.LogoPath ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@FaviconPath", (object?)profile.FaviconPath ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@LoginBackgroundPath", (object?)profile.LoginBackgroundPath ?? DBNull.Value);
            });
    }

    // ---- Versions & lifecycle ---------------------------------------------

    /// <summary>
    /// Compiles the current branding profile into a new version row. The row is
    /// stored as Draft when compilation blocks, otherwise Validated — ready to
    /// publish. Returns null when no branding profile exists.
    /// </summary>
    public static async Task<ThemeVersion?> CompileDraftAsync(
        ApplicationDbContext dbContext, int companyId)
    {
        var profile = await GetBrandingProfileAsync(dbContext, companyId);
        if (profile is null)
        {
            return null;
        }

        var result = ThemeCompiler.Compile(
            new ThemeCompiler.BrandingInput(profile.PrimaryHex, profile.SecondaryHex, profile.AccentHex));

        var status = result.Level == ThemeCompiler.ValidationLevel.Block
            ? StatusRejected
            : StatusValidated;
        var validationJson = System.Text.Json.JsonSerializer.Serialize(result.Messages);
        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(profile);

        var id = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
INSERT INTO ThemeVersions (CompanyId, Status, CompiledCss, ValidationLevel, ValidationJson, BrandingSnapshotJson)
VALUES (@CompanyId, @Status, @CompiledCss, @ValidationLevel, @ValidationJson, @BrandingSnapshotJson);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(command, "@Status", status);
                HrmsDatabase.AddParameter(command, "@CompiledCss", result.CompiledCss);
                HrmsDatabase.AddParameter(command, "@ValidationLevel", result.Level.ToString());
                HrmsDatabase.AddParameter(command, "@ValidationJson", validationJson);
                HrmsDatabase.AddParameter(command, "@BrandingSnapshotJson", snapshotJson);
            });

        return await GetVersionAsync(dbContext, id);
    }

    /// <summary>
    /// Publishes a validated version: archives whatever is currently published
    /// for the company, then marks the target Published. Rollback is the same
    /// call against an older version id. Rejected versions cannot be published.
    /// </summary>
    public static async Task<bool> PublishAsync(
        ApplicationDbContext dbContext, int companyId, int versionId)
    {
        await EnsureAsync(dbContext);

        var version = await GetVersionAsync(dbContext, versionId);
        if (version is null || version.CompanyId != companyId || version.Status == StatusRejected)
        {
            return false;
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE ThemeVersions SET Status = 'Archived'
    WHERE CompanyId = @CompanyId AND Status = 'Published' AND Id <> @VersionId;
UPDATE ThemeVersions SET Status = 'Published', PublishedAt = SYSUTCDATETIME()
    WHERE Id = @VersionId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
                HrmsDatabase.AddParameter(command, "@VersionId", versionId);
            });

        return true;
    }

    public static async Task<ThemeVersion?> GetVersionAsync(
        ApplicationDbContext dbContext, int versionId)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ThemeVersions WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", versionId),
            ReadVersion);
        return rows.FirstOrDefault();
    }

    public static async Task<List<ThemeVersion>> ListVersionsAsync(
        ApplicationDbContext dbContext, int companyId)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM ThemeVersions WHERE CompanyId = @CompanyId ORDER BY Id DESC;",
            command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId),
            ReadVersion);
    }

    /// <summary>
    /// The compiled CSS of the company's currently published version, or null
    /// when the company has never published a theme (renders ZYNORA Default).
    /// This is the request-path read behind the runtime cache.
    /// </summary>
    public static async Task<PublishedTheme?> GetActivePublishedAsync(
        ApplicationDbContext dbContext, int companyId)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT TOP 1 Id, CompiledCss, BrandingSnapshotJson FROM ThemeVersions
    WHERE CompanyId = @CompanyId AND Status = 'Published'
    ORDER BY PublishedAt DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@CompanyId", companyId),
            reader =>
            {
                var snapshot = DeserializeSnapshot(HrmsDatabase.GetString(reader, "BrandingSnapshotJson"));
                return new PublishedTheme(
                    HrmsDatabase.GetInt(reader, "Id"),
                    HrmsDatabase.GetString(reader, "CompiledCss"),
                    snapshot?.DisplayName,
                    snapshot?.LogoPath,
                    snapshot?.FaviconPath);
            });
        return rows.FirstOrDefault();
    }

    // ---- Readers -----------------------------------------------------------

    private static BrandingProfile ReadProfile(DbDataReader reader) => new()
    {
        CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
        PrimaryHex = HrmsDatabase.GetString(reader, "PrimaryHex"),
        SecondaryHex = NullIfEmpty(HrmsDatabase.GetString(reader, "SecondaryHex")),
        AccentHex = NullIfEmpty(HrmsDatabase.GetString(reader, "AccentHex")),
        DisplayName = NullIfEmpty(HrmsDatabase.GetString(reader, "DisplayName")),
        LogoPath = NullIfEmpty(HrmsDatabase.GetString(reader, "LogoPath")),
        FaviconPath = NullIfEmpty(HrmsDatabase.GetString(reader, "FaviconPath")),
        LoginBackgroundPath = NullIfEmpty(HrmsDatabase.GetString(reader, "LoginBackgroundPath")),
    };

    private static ThemeVersion ReadVersion(DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
        Status = HrmsDatabase.GetString(reader, "Status"),
        CompiledCss = HrmsDatabase.GetString(reader, "CompiledCss"),
        ValidationLevel = HrmsDatabase.GetString(reader, "ValidationLevel"),
        ValidationJson = NullIfEmpty(HrmsDatabase.GetString(reader, "ValidationJson")),
        BrandingSnapshotJson = NullIfEmpty(HrmsDatabase.GetString(reader, "BrandingSnapshotJson")),
        CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? DateTime.UtcNow,
        PublishedAt = HrmsDatabase.GetDateTime(reader, "PublishedAt"),
    };

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static BrandingProfile? DeserializeSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<BrandingProfile>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
