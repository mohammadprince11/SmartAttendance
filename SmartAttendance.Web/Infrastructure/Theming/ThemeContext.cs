namespace SmartAttendance.Web.Infrastructure.Theming;

/// <summary>
/// Resolved theme for the current request. When <see cref="CompiledCss"/> is
/// empty the application renders the ZYNORA Default (the token contract in
/// zynora-theme-contract.css); a non-empty value is the machine-compiled
/// company override injected into the document head.
/// </summary>
public sealed class ThemeContext
{
    /// <summary>Company the theme was resolved for, or null when unresolved.</summary>
    public int? CompanyId { get; init; }

    /// <summary>
    /// Immutable version stamp for the resolved theme. Part of the cache key so a
    /// published change invalidates cleanly. "default" means the ZYNORA fallback.
    /// </summary>
    public string Version { get; init; } = DefaultVersion;

    /// <summary>
    /// Compiled <c>:root</c> override CSS produced by the theme compiler. Empty
    /// for the ZYNORA Default. Populated from the persisted published version in
    /// a later phase; never contains raw user-authored CSS.
    /// </summary>
    public string CompiledCss { get; init; } = string.Empty;

    /// <summary>True when a company override should be injected into the head.</summary>
    public bool HasCompanyOverride => !string.IsNullOrWhiteSpace(CompiledCss);

    /// <summary>Company display name from the published branding snapshot, or null.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Company logo path from the published branding snapshot, or null.</summary>
    public string? LogoPath { get; init; }

    /// <summary>Company favicon path from the published branding snapshot, or null.</summary>
    public string? FaviconPath { get; init; }

    public const string DefaultVersion = "default";

    /// <summary>The safe fallback: no company, no override, ZYNORA Default skin.</summary>
    public static readonly ThemeContext Default = new();
}
