namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>يربط المسارات الحية بمفاتيح شجرة أدوار الصفحات ويستنتج فعل CRUD من الطلب.</summary>
public static class PageAccessRouteCatalog
{
    private sealed record Route(string Prefix, string PageCode);

    // الأطول أولاً: صفحات ملف الموظف المتخصصة يجب ألا تسقط على People.Directory.
    private static readonly IReadOnlyList<Route> Routes = new List<Route>
    {
        new("/employees/peopledashboard", "People.Dashboard"),
        new("/employees/profile", "People.Profile"), new("/employees/edit", "People.Profile"),
        new("/employees/evaluations", "People.Evaluations"),
        new("/employees/temporaryheads", "People.TemporaryHeads"),
        new("/employees/financialinfo", "Payroll.FinancialInfo"),
        new("/contracts", "People.Contracts"), new("/forms", "People.Forms"),
        new("/acknowledgments", "People.Acknowledgments"),
        new("/companydocuments", "People.CompanyDocuments"),
        new("/employeeupdates", "People.Updates"), new("/engagement", "People.Engagement"),
        new("/violations", "People.Violations"), new("/assetsmanagement", "People.Assets"),
        new("/employeetasks", "People.Tasks"), new("/leavebalances", "People.LeaveBalances"),
        new("/leaverequests", "People.LeaveRequests"), new("/approvals", "People.Approvals"),
        new("/alerts", "People.Alerts"), new("/documents", "People.Documents"),
        new("/employeedocuments", "People.Documents"), new("/organization", "People.Organization"),
        new("/orgstructures", "People.OrgStructures"), new("/peoplereports", "People.Reports"),
        new("/badgecenter", "People.Cards"), new("/employees", "People.Directory"),

        new("/companies", "Setup.Company"), new("/branches", "Setup.Branches"),
        new("/departments", "Setup.Departments"), new("/positions", "Setup.Positions"),
        new("/useraccess", "Identity.Users"), new("/employeepermissions", "Identity.Permissions"),
        new("/accessroles", "Identity.AccessRoles"),

        new("/disciplinaryrules", "HrSettings.Disciplinary"),
        new("/employeeprofilesettings", "HrSettings.ProfileFields"),
        new("/hrsettings/approvaltemplates", "HrSettings.ApprovalTemplates"),
        new("/hrsettings/entityfields", "HrSettings.EntityFields"),
        new("/hrsettings/employeegroups", "HrSettings.EmployeeGroups"),
        new("/hrsettings/lookups", "HrSettings.Lookups"),
        new("/hrsettings/violationconfiguration", "HrSettings.ViolationConfiguration"),
        new("/hrsettings/formulas", "HrSettings.Formulas"),
        new("/hrsettings/probationperiod", "HrSettings.ProbationPeriod"),
        new("/hrsettings/noticeperiod", "HrSettings.NoticePeriod"),
        new("/hrsettings/selfservicesettings", "HrSettings.SelfService"),
        new("/hrsettings/terminationreasons", "HrSettings.TerminationReasons"),
        new("/hrsettings/notificationcenter", "HrSettings.Notifications"),
        new("/hrsettings/employeecodeschema", "HrSettings.EmployeeCodeSchema"),
        new("/hrsettings/fieldcontrol", "HrSettings.FieldControl"),

        new("/attendanceoperations", "Attendance.Operations"),
        new("/attendancerecords", "Attendance.Records"),
        new("/attendanceimports", "Attendance.Imports"),
        new("/attendanceprocessing", "Attendance.Processing"),
        new("/attendancecorrections", "Attendance.Corrections"),
        new("/shifttypes", "Attendance.ShiftTypes"),
        new("/attendancesettings", "Attendance.Settings"),
        new("/dayattendance", "Attendance.DayAttendance"),
        new("/workfromhome", "Attendance.WorkFromHome"),
        new("/attendancedashboard", "Attendance.Dashboard"),
        new("/shiftrules", "Attendance.ShiftRules"), new("/periodrules", "Attendance.PeriodRules"),
        new("/attendancerecommendations", "Attendance.Recommendations"),
        new("/missingpunchrequests", "Attendance.MissingPunch"),
        new("/employeeonlinepunches", "Attendance.OnlinePunches"),
        new("/shiftassignments", "Attendance.Assignments"),
        new("/attendanceviewer", "Attendance.Viewer"),
        new("/monthattendance", "Attendance.MonthAttendance"),
        new("/weekattendance", "Attendance.WeekAttendance"),
        new("/attendancereports", "Attendance.Reports"), new("/devices", "Attendance.Devices"),
        new("/shiftoverrides", "Attendance.ShiftOverrides"),
        new("/roster", "Attendance.Roster"), new("/biometrickeys", "Attendance.BiometricKeys"),
        new("/holidays", "Attendance.Holidays"),
        new("/employeegeolocations", "Attendance.GeoLocations"),

        new("/payroll/runs", "Payroll.Runs"), new("/payroll/transactions", "Payroll.Transactions"),
        new("/payroll/overtime", "Payroll.Overtime"),
        new("/payroll/salarydaysadjustment", "Payroll.SalaryDaysAdjustment"),
        new("/payroll/leaveencashment", "Payroll.LeaveEncashment"),
        new("/payroll/raises", "Payroll.Raises"), new("/payroll/endofservice", "Payroll.EndOfService"),
        new("/payrollprovisions", "Payroll.Provisions"), new("/payroll/salaryitems", "Payroll.SalaryItems"),
        new("/payroll/settings", "Payroll.Settings"), new("/banktemplates", "Payroll.BankTemplates"),
        new("/payroll/taxsocial", "Payroll.TaxSocial"), new("/payroll/payment", "Payroll.Payment"),
        new("/payrollreports", "Payroll.Reports"),
        new("/payroll/analytics", "Payroll.Analytics"),
        new("/payroll/payslipinquiry", "Payroll.PayslipInquiry"),
        new("/payroll/simulator", "Payroll.Simulator"),
        new("/payroll/transactionsauditor", "Payroll.Auditor"),
        new("/payroll/salarysheet", "Payroll.SalarySheet"),
        new("/payroll/financialrequests", "Payroll.FinancialRequests"),
        new("/payroll/loans", "Payroll.Loans"), new("/payroll/salaryscale", "Payroll.SalaryScale"),
        new("/payroll/terminationsettlement", "Payroll.TerminationSettlement")
    }.OrderByDescending(route => route.Prefix.Length).ToList();

    public static string? ResolvePageCode(string? path, string? handler = null)
    {
        var normalized = (path ?? string.Empty).TrimEnd('/').ToLowerInvariant();
        if ((normalized == "/employees" || normalized == "/employees/index") &&
            ((handler ?? string.Empty).Contains("import", StringComparison.OrdinalIgnoreCase) ||
             (handler ?? string.Empty).Contains("template", StringComparison.OrdinalIgnoreCase)))
            return "People.Import";
        return Routes.FirstOrDefault(route =>
            normalized == route.Prefix || normalized.StartsWith(route.Prefix + "/", StringComparison.Ordinal))?.PageCode;
    }

    public static IReadOnlyCollection<string> MappedPageCodes =>
        Routes.Select(route => route.PageCode).Append("People.Import")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string ResolveAction(string method, string? path, string? handler, int? postedId = null)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) return "View";
        var operation = handler ?? string.Empty;
        if (operation.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
            operation.Contains("remove", StringComparison.OrdinalIgnoreCase)) return "Delete";
        if ((path ?? string.Empty).Contains("/create", StringComparison.OrdinalIgnoreCase) ||
            operation.Contains("create", StringComparison.OrdinalIgnoreCase) ||
            operation.Contains("add", StringComparison.OrdinalIgnoreCase)) return "Create";
        if (operation.Contains("save", StringComparison.OrdinalIgnoreCase) && postedId is not > 0)
            return "Create";
        return "Edit";
    }
}
