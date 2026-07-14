namespace SmartAttendance.Application.Common.Security;

public static class AnnouncementPermissionCodes
{
    public const string Create = "Announcements.Create";
    public const string Publish = "Announcements.Publish";
    public const string Archive = "Announcements.Archive";
    public const string Delete = "Announcements.Delete";
    public const string ManageTemplates = "Announcements.ManageTemplates";
    public const string ManageSignatures = "Announcements.ManageSignatures";
    public const string ModerateComments = "Announcements.ModerateComments";
    public const string ViewReadReports = "Announcements.ViewReadReports";

    public static IReadOnlyCollection<string> All { get; } = new[]
    {
        Create,
        Publish,
        Archive,
        Delete,
        ManageTemplates,
        ManageSignatures,
        ModerateComments,
        ViewReadReports
    };
}
