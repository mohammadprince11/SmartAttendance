using SmartAttendance.Application.Announcements.Models;

namespace SmartAttendance.Application.Announcements.Services;

public interface IAnnouncementService
{
    Task<IReadOnlyList<AnnouncementManagementItem>> GetManagementListAsync(
        string? search,
        CancellationToken cancellationToken = default);

    Task<AnnouncementOperationResult> CreateAsync(
        AnnouncementCreateRequest request,
        AnnouncementActorContext actor,
        CancellationToken cancellationToken = default);

    Task<AnnouncementOperationResult> PublishAsync(
        int announcementId,
        AnnouncementActorContext actor,
        CancellationToken cancellationToken = default);

    Task<AnnouncementOperationResult> ArchiveAsync(
        int announcementId,
        AnnouncementActorContext actor,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeAnnouncementItem>> GetEmployeeFeedAsync(
        int employeeId,
        CancellationToken cancellationToken = default);

    Task<AnnouncementOperationResult> MarkReadAsync(
        int announcementId,
        int employeeId,
        CancellationToken cancellationToken = default);
}
