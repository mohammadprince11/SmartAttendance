using Xunit;

namespace SmartAttendance.Tests;

public sealed class AccessControlSurfaceTests
{
    [Fact]
    public void AccessControl_IsAdminOnlyAndUsesUnifiedRoleAssignments()
    {
        var root = FindRoot();
        var model = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web", "Pages", "AccessControl", "Index.cshtml.cs"));
        var page = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web", "Pages", "AccessControl", "Index.cshtml"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", model, StringComparison.Ordinal);
        Assert.Contains("ReplaceUserRolesAsync", model, StringComparison.Ordinal);
        Assert.Contains("AccessRoleStore.ReplaceGrantsAsync", model, StringComparison.Ordinal);
        Assert.Contains("_accessRoleService.ResolveAsync", model, StringComparison.Ordinal);
        Assert.Contains("SystemUserPermissions", model, StringComparison.Ordinal);

        Assert.Contains("الأدوار والصلاحيات", model, StringComparison.Ordinal);
        Assert.Contains("إسناد المستخدمين", model, StringComparison.Ordinal);
        Assert.Contains("نطاق البيانات", model, StringComparison.Ordinal);
        Assert.Contains("الحقول الحساسة", model, StringComparison.Ordinal);
        Assert.Contains("الوصول الفعلي", model, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedPermissionSurfaces_DoNotReturnToNavigation()
    {
        var root = FindRoot();
        var layout = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web", "Pages", "Shared", "_Layout.cshtml"));
        var settings = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web", "Pages", "Settings", "Index.cshtml"));

        Assert.Contains("/AccessControl/Index", layout, StringComparison.Ordinal);
        Assert.Contains("/AccessControl/Index", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("/AccessRoles", layout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/EmployeePermissions", layout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/AccessRoles", settings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/EmployeePermissions", settings, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccessControl_UsesZynoraIdentityTokensWithoutPrivatePalette()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web", "Pages", "AccessControl", "Index.cshtml"));
        var css = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web", "wwwroot", "css", "pages", "access-control.css"));

        Assert.Contains("zy-breadcrumb", page, StringComparison.Ordinal);
        Assert.Contains("zy-page-header", page, StringComparison.Ordinal);
        Assert.Contains("zy-btn zy-btn--primary", page, StringComparison.Ordinal);
        Assert.Contains("var(--color-primary", css, StringComparison.Ordinal);
        Assert.Contains("var(--color-surface", css, StringComparison.Ordinal);
        Assert.Contains("var(--radius", css, StringComparison.Ordinal);
        Assert.DoesNotContain("#", css, StringComparison.Ordinal);
        Assert.DoesNotContain("rgba(", css, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void BulkSelection_ProvidesRowColumnAndModuleToggles()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web", "Pages", "AccessControl", "Index.cshtml"));
        var script = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web", "wwwroot", "js", "access-control-bulk-select.js"));

        Assert.Contains("js-ac-module-all", page, StringComparison.Ordinal);
        Assert.Contains("js-ac-column-all", page, StringComparison.Ordinal);
        Assert.Contains("js-ac-row-all", page, StringComparison.Ordinal);
        Assert.Contains("js-ac-permission", page, StringComparison.Ordinal);
        Assert.Contains("indeterminate", script, StringComparison.Ordinal);
        Assert.Contains("syncTable", script, StringComparison.Ordinal);
    }
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
