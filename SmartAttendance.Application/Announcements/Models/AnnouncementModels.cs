using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.Announcements.Models;

public sealed class AnnouncementActorContext
{
    public string UserName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public string? IpAddress { get; init; }
}

public sealed class AnnouncementCreateRequest
{
    public string LanguageCode { get; init; } = "ar";

    public string Title { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public string Category { get; init; } = "عام";

    public bool PublishNow { get; init; }

    public bool CommentsEnabled { get; init; }

    public bool ReactionsEnabled { get; init; } = true;

    public bool AllEmployees { get; init; }

    public IReadOnlyCollection<int> CompanyIds { get; init; } = Array.Empty<int>();

    public IReadOnlyCollection<int> BranchIds { get; init; } = Array.Empty<int>();

    public IReadOnlyCollection<int> DepartmentIds { get; init; } = Array.Empty<int>();

    public IReadOnlyCollection<int> PositionIds { get; init; } = Array.Empty<int>();

    public IReadOnlyCollection<int> EmployeeIds { get; init; } = Array.Empty<int>();

    public IReadOnlyCollection<int> ExcludedCompanyIds { get; init; } = Array.Empty<int>();

    public IReadOnlyCollection<int> ExcludedBranchIds { get; init; } = Array.Empty<int>();

    public IReadOnlyCollection<int> ExcludedDepartmentIds { get; init; } = Array.Empty<int>();

    public IReadOnlyCollection<int> ExcludedPositionIds { get; init; } = Array.Empty<int>();

    public IReadOnlyCollection<int> ExcludedEmployeeIds { get; init; } = Array.Empty<int>();
}

public sealed class AnnouncementManagementItem
{
    public int Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public AnnouncementStatus Status { get; init; }

    public DateOnly? PublishDate { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string AudienceSummary { get; init; } = string.Empty;

    public int RecipientCount { get; init; }

    public bool IsLegacy { get; init; }
}

public sealed class EmployeeAnnouncementItem
{
    public int Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public DateOnly? PublishDate { get; init; }

    public bool IsRead { get; init; }

    public DateTime? FirstReadAtUtc { get; init; }
}

public sealed class AnnouncementOperationResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public int? AnnouncementId { get; init; }

    public static AnnouncementOperationResult Ok(string message, int? announcementId = null) =>
        new()
        {
            Success = true,
            Message = message,
            AnnouncementId = announcementId
        };

    public static AnnouncementOperationResult Fail(string message) =>
        new()
        {
            Success = false,
            Message = message
        };
}
