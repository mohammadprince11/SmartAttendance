using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Services;

public sealed class AnnouncementService : IAnnouncementService
{
    private readonly ApplicationDbContext _dbContext;

    public AnnouncementService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AnnouncementManagementItem>> GetManagementListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AnnouncementGroups
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.Contents)
            .Include(x => x.AudienceRules)
                .ThenInclude(x => x.Company)
            .Include(x => x.AudienceRules)
                .ThenInclude(x => x.Branch)
            .Include(x => x.AudienceRules)
                .ThenInclude(x => x.Department)
            .Include(x => x.AudienceRules)
                .ThenInclude(x => x.Position)
            .Include(x => x.AudienceRules)
                .ThenInclude(x => x.Employee)
            .Include(x => x.Recipients)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.Contents.Any(content =>
                    !content.IsDeleted &&
                    (content.Title.Contains(term) ||
                     content.Body.Contains(term) ||
                     (content.Category != null && content.Category.Contains(term)))));
        }

        var groups = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        return groups
            .Select(group =>
            {
                var content = SelectContent(group.Contents, "ar");

                return new AnnouncementManagementItem
                {
                    Id = group.Id,
                    Title = content?.Title ?? string.Empty,
                    Body = content?.Body ?? string.Empty,
                    Category = content?.Category ?? "عام",
                    Status = group.Status,
                    PublishDate = group.PublishDate,
                    CreatedAtUtc = group.CreatedAt,
                    CreatedBy = group.CreatedBy ?? group.CreatedBySystemUser?.UserName ?? string.Empty,
                    AudienceSummary = BuildAudienceSummary(group.AudienceRules),
                    RecipientCount = group.Recipients.Count(x => !x.IsDeleted),
                    IsLegacy = group.LegacyAnnouncementId.HasValue
                };
            })
            .ToList();
    }

    public async Task<AnnouncementOperationResult> CreateAsync(
        AnnouncementCreateRequest request,
        AnnouncementActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRequest(request);
        var validationMessage = ValidateCreateRequest(normalized);

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            return AnnouncementOperationResult.Fail(validationMessage);
        }

        var actorUser = await ResolveActorUserAsync(actor.UserName, cancellationToken);

        if (!await HasPermissionAsync(actor, actorUser, AnnouncementPermissionCodes.Create, cancellationToken))
        {
            return AnnouncementOperationResult.Fail("ليس لديك صلاحية إنشاء الإعلانات.");
        }

        if (normalized.PublishNow &&
            !await HasPermissionAsync(actor, actorUser, AnnouncementPermissionCodes.Publish, cancellationToken))
        {
            return AnnouncementOperationResult.Fail("ليس لديك صلاحية نشر الإعلانات.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var utcNow = DateTime.UtcNow;
            var baghdadDate = DateOnly.FromDateTime(GetBaghdadNow().DateTime);

            var group = new AnnouncementGroup
            {
                TranslationGroupId = Guid.NewGuid(),
                CreatedBySystemUserId = actorUser?.Id,
                Status = AnnouncementStatus.Pending,
                CommentsEnabled = normalized.CommentsEnabled,
                ReactionsEnabled = normalized.ReactionsEnabled,
                CreatedAt = utcNow,
                CreatedBy = actor.UserName
            };

            group.Contents.Add(new AnnouncementContent
            {
                LanguageCode = normalized.LanguageCode,
                Title = normalized.Title,
                Body = normalized.Body,
                Category = normalized.Category,
                SignatureType = AnnouncementSignatureType.None,
                CreatedAt = utcNow,
                CreatedBy = actor.UserName
            });

            AddAudienceRules(group, normalized, utcNow);

            group.Channels.Add(new AnnouncementChannel
            {
                ChannelType = AnnouncementChannelType.EmployeeWall,
                IsEnabled = true,
                CreatedAt = utcNow
            });

            group.Channels.Add(new AnnouncementChannel
            {
                ChannelType = AnnouncementChannelType.InSystemNotification,
                IsEnabled = true,
                CreatedAt = utcNow
            });

            _dbContext.AnnouncementGroups.Add(group);
            await _dbContext.SaveChangesAsync(cancellationToken);

            AddAudit(
                group,
                actorUser?.Id,
                actor,
                "Create",
                new
                {
                    normalized.LanguageCode,
                    normalized.Title,
                    normalized.Category,
                    normalized.PublishNow
                },
                utcNow);

            if (normalized.PublishNow)
            {
                var publishResult = await PublishInternalAsync(
                    group,
                    actor,
                    actorUser?.Id,
                    baghdadDate,
                    utcNow,
                    cancellationToken);

                if (!publishResult.Success)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return publishResult;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return AnnouncementOperationResult.Ok(
                normalized.PublishNow
                    ? "تم إنشاء الإعلان ونشره على حائط الموظفين المستهدفين."
                    : "تم حفظ الإعلان كمسودة.",
                group.Id);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AnnouncementOperationResult> PublishAsync(
        int announcementId,
        AnnouncementActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var actorUser = await ResolveActorUserAsync(actor.UserName, cancellationToken);

        if (!await HasPermissionAsync(actor, actorUser, AnnouncementPermissionCodes.Publish, cancellationToken))
        {
            return AnnouncementOperationResult.Fail("ليس لديك صلاحية نشر الإعلانات.");
        }

        var group = await _dbContext.AnnouncementGroups
            .Include(x => x.Contents)
            .Include(x => x.AudienceRules)
            .Include(x => x.Recipients)
            .Include(x => x.Channels)
            .Include(x => x.Notifications)
                .ThenInclude(x => x.Recipients)
            .FirstOrDefaultAsync(
                x => x.Id == announcementId && !x.IsDeleted,
                cancellationToken);

        if (group == null)
        {
            return AnnouncementOperationResult.Fail("الإعلان غير موجود.");
        }

        if (group.Status != AnnouncementStatus.Pending &&
            group.Status != AnnouncementStatus.Scheduled)
        {
            return AnnouncementOperationResult.Fail("يمكن نشر المسودات أو الإعلانات المجدولة فقط.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var utcNow = DateTime.UtcNow;
            var baghdadDate = DateOnly.FromDateTime(GetBaghdadNow().DateTime);

            var result = await PublishInternalAsync(
                group,
                actor,
                actorUser?.Id,
                baghdadDate,
                utcNow,
                cancellationToken);

            if (!result.Success)
            {
                await transaction.RollbackAsync(cancellationToken);
                return result;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return AnnouncementOperationResult.Ok("تم نشر الإعلان بنجاح.", group.Id);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<AnnouncementOperationResult> ArchiveAsync(
        int announcementId,
        AnnouncementActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var actorUser = await ResolveActorUserAsync(actor.UserName, cancellationToken);

        if (!await HasPermissionAsync(actor, actorUser, AnnouncementPermissionCodes.Archive, cancellationToken))
        {
            return AnnouncementOperationResult.Fail("ليس لديك صلاحية أرشفة الإعلانات.");
        }

        var group = await _dbContext.AnnouncementGroups
            .Include(x => x.Channels)
            .FirstOrDefaultAsync(
                x => x.Id == announcementId && !x.IsDeleted,
                cancellationToken);

        if (group == null)
        {
            return AnnouncementOperationResult.Fail("الإعلان غير موجود.");
        }

        if (group.Status == AnnouncementStatus.Archived)
        {
            return AnnouncementOperationResult.Ok("الإعلان مؤرشف مسبقاً.", group.Id);
        }

        var utcNow = DateTime.UtcNow;
        group.Status = AnnouncementStatus.Archived;
        group.ArchivedAtUtc = utcNow;
        group.UpdatedAt = utcNow;
        group.UpdatedBy = actor.UserName;

        foreach (var channel in group.Channels.Where(x => !x.IsDeleted))
        {
            channel.IsEnabled = false;
            channel.UpdatedAt = utcNow;
        }

        AddAudit(
            group,
            actorUser?.Id,
            actor,
            "Archive",
            new { group.Status, group.ArchivedAtUtc },
            utcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return AnnouncementOperationResult.Ok("تمت أرشفة الإعلان وإيقاف ظهوره على الحائط.", group.Id);
    }

    public async Task<IReadOnlyList<EmployeeAnnouncementItem>> GetEmployeeFeedAsync(
        int employeeId,
        CancellationToken cancellationToken = default)
    {
        var groups = await _dbContext.AnnouncementGroups
            .AsNoTracking()
            .Where(group =>
                !group.IsDeleted &&
                (group.Status == AnnouncementStatus.Published ||
                 (group.Status == AnnouncementStatus.Expired &&
                  group.ExpirationBehavior == AnnouncementExpirationBehavior.KeepVisibleAsExpired)) &&
                group.Recipients.Any(recipient =>
                    !recipient.IsDeleted &&
                    recipient.EmployeeId == employeeId) &&
                group.Channels.Any(channel =>
                    !channel.IsDeleted &&
                    channel.ChannelType == AnnouncementChannelType.EmployeeWall &&
                    channel.IsEnabled))
            .Include(x => x.Contents)
            .Include(x => x.ReadReceipts.Where(receipt =>
                !receipt.IsDeleted &&
                receipt.EmployeeId == employeeId))
            .OrderByDescending(x => x.PublishedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        return groups
            .Select(group =>
            {
                var content = SelectContent(group.Contents, "ar");
                var receipt = group.ReadReceipts.FirstOrDefault();

                return new EmployeeAnnouncementItem
                {
                    Id = group.Id,
                    Title = content?.Title ?? string.Empty,
                    Body = content?.Body ?? string.Empty,
                    Category = content?.Category ?? "عام",
                    PublishDate = group.PublishDate,
                    IsRead = receipt != null,
                    FirstReadAtUtc = receipt?.FirstReadAtUtc
                };
            })
            .ToList();
    }

    public async Task<AnnouncementOperationResult> MarkReadAsync(
        int announcementId,
        int employeeId,
        CancellationToken cancellationToken = default)
    {
        var isRecipient = await _dbContext.AnnouncementRecipients
            .AsNoTracking()
            .AnyAsync(
                x => !x.IsDeleted &&
                     x.AnnouncementGroupId == announcementId &&
                     x.EmployeeId == employeeId,
                cancellationToken);

        if (!isRecipient)
        {
            return AnnouncementOperationResult.Fail("الإعلان غير موجه لهذا الموظف.");
        }

        var utcNow = DateTime.UtcNow;
        var receipt = await _dbContext.AnnouncementReadReceipts
            .FirstOrDefaultAsync(
                x => !x.IsDeleted &&
                     x.AnnouncementGroupId == announcementId &&
                     x.EmployeeId == employeeId,
                cancellationToken);

        if (receipt == null)
        {
            receipt = new AnnouncementReadReceipt
            {
                AnnouncementGroupId = announcementId,
                EmployeeId = employeeId,
                FirstReadAtUtc = utcNow,
                LastOpenedAtUtc = utcNow,
                ConfirmedAtUtc = utcNow,
                OpenCount = 1,
                CreatedAt = utcNow
            };

            _dbContext.AnnouncementReadReceipts.Add(receipt);
        }
        else
        {
            receipt.LastOpenedAtUtc = utcNow;
            receipt.ConfirmedAtUtc = utcNow;
            receipt.OpenCount += 1;
            receipt.UpdatedAt = utcNow;
        }

        var notificationRecipients = await _dbContext.UserNotificationRecipients
            .Where(x =>
                !x.IsDeleted &&
                x.EmployeeId == employeeId &&
                x.UserNotification.AnnouncementGroupId == announcementId)
            .ToListAsync(cancellationToken);

        foreach (var notificationRecipient in notificationRecipients)
        {
            notificationRecipient.IsRead = true;
            notificationRecipient.ReadAtUtc ??= utcNow;
            notificationRecipient.UpdatedAt = utcNow;
        }

        receipt.NotificationReadAtUtc = notificationRecipients.Count > 0 ? utcNow : null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return AnnouncementOperationResult.Ok("تم تسجيل قراءة الإعلان.", announcementId);
    }

    private async Task<AnnouncementOperationResult> PublishInternalAsync(
        AnnouncementGroup group,
        AnnouncementActorContext actor,
        int? actorSystemUserId,
        DateOnly baghdadDate,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var recipientIds = await ResolveRecipientIdsAsync(group.AudienceRules, cancellationToken);

        if (recipientIds.Count == 0)
        {
            return AnnouncementOperationResult.Fail("لا يوجد موظفون فعالون ضمن الجمهور المحدد.");
        }

        foreach (var recipient in group.Recipients.ToList())
        {
            _dbContext.AnnouncementRecipients.Remove(recipient);
        }

        group.Recipients.Clear();

        var sourceSummary = BuildAudienceSummary(group.AudienceRules);

        foreach (var employeeId in recipientIds)
        {
            group.Recipients.Add(new AnnouncementRecipient
            {
                EmployeeId = employeeId,
                ResolvedAtUtc = utcNow,
                SourceSummary = sourceSummary,
                CreatedAt = utcNow
            });
        }

        group.Status = AnnouncementStatus.Published;
        group.PublishDate = baghdadDate;
        group.PublishedAtUtc = utcNow;
        group.AudienceCapturedAtUtc = utcNow;
        group.UpdatedAt = utcNow;
        group.UpdatedBy = actor.UserName;

        foreach (var channel in group.Channels.Where(x => !x.IsDeleted))
        {
            channel.IsEnabled = true;
            channel.ActivatedAtUtc = utcNow;
            channel.UpdatedAt = utcNow;
        }

        if (!group.Notifications.Any(x =>
                !x.IsDeleted &&
                x.NotificationType == UserNotificationType.Announcement))
        {
            var arabic = SelectContent(group.Contents, "ar");
            var english = SelectContent(group.Contents, "en");

            var notification = new UserNotification
            {
                AnnouncementGroupId = group.Id,
                NotificationType = UserNotificationType.Announcement,
                TitleAr = arabic?.Title,
                MessageAr = arabic?.Body,
                TitleEn = english?.Title,
                MessageEn = english?.Body,
                Url = "/EmployeePortal/Index?tab=home",
                CreatedAtUtc = utcNow,
                CreatedAt = utcNow
            };

            foreach (var employeeId in recipientIds)
            {
                notification.Recipients.Add(new UserNotificationRecipient
                {
                    EmployeeId = employeeId,
                    IsRead = false,
                    CreatedAt = utcNow
                });
            }

            group.Notifications.Add(notification);
        }

        AddAudit(
            group,
            actorSystemUserId,
            actor,
            "Publish",
            new
            {
                group.Status,
                group.PublishDate,
                RecipientCount = recipientIds.Count
            },
            utcNow);

        return AnnouncementOperationResult.Ok("تم نشر الإعلان.", group.Id);
    }

    private async Task<List<int>> ResolveRecipientIdsAsync(
        IEnumerable<AnnouncementAudienceRule> audienceRules,
        CancellationToken cancellationToken)
    {
        var rules = audienceRules
            .Where(x => !x.IsDeleted)
            .ToList();

        var includes = rules.Where(x => !x.IsExcluded).ToList();
        var excludes = rules.Where(x => x.IsExcluded).ToList();

        var activeEmployees = _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.IsActive && !x.IsDeleted);

        IQueryable<Employee> includedQuery;

        if (includes.Any(x => x.AudienceType == AnnouncementAudienceType.All))
        {
            includedQuery = activeEmployees;
        }
        else
        {
            var companyIds = includes
                .Where(x => x.AudienceType == AnnouncementAudienceType.Company && x.CompanyId.HasValue)
                .Select(x => x.CompanyId!.Value)
                .Distinct()
                .ToArray();

            var branchIds = includes
                .Where(x => x.AudienceType == AnnouncementAudienceType.Branch && x.BranchId.HasValue)
                .Select(x => x.BranchId!.Value)
                .Distinct()
                .ToArray();

            var departmentIds = includes
                .Where(x => x.AudienceType == AnnouncementAudienceType.Department && x.DepartmentId.HasValue)
                .Select(x => x.DepartmentId!.Value)
                .Distinct()
                .ToArray();

            var positionIds = includes
                .Where(x => x.AudienceType == AnnouncementAudienceType.Position && x.PositionId.HasValue)
                .Select(x => x.PositionId!.Value)
                .Distinct()
                .ToArray();

            var employeeIds = includes
                .Where(x => x.AudienceType == AnnouncementAudienceType.Employee && x.EmployeeId.HasValue)
                .Select(x => x.EmployeeId!.Value)
                .Distinct()
                .ToArray();

            includedQuery = activeEmployees;

            if (companyIds.Length > 0)
            {
                includedQuery = includedQuery.Where(x => companyIds.Contains(x.Branch.CompanyId));
            }

            var hasDimension =
                branchIds.Length > 0 ||
                departmentIds.Length > 0 ||
                positionIds.Length > 0 ||
                employeeIds.Length > 0;

            if (hasDimension)
            {
                includedQuery = includedQuery.Where(x =>
                    branchIds.Contains(x.BranchId) ||
                    departmentIds.Contains(x.DepartmentId) ||
                    (x.PositionId.HasValue && positionIds.Contains(x.PositionId.Value)) ||
                    employeeIds.Contains(x.Id));
            }
            else if (companyIds.Length == 0)
            {
                return new List<int>();
            }
        }

        var includedIds = await includedQuery
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (excludes.Count == 0 || includedIds.Count == 0)
        {
            return includedIds;
        }

        var excludedCompanyIds = excludes
            .Where(x => x.AudienceType == AnnouncementAudienceType.Company && x.CompanyId.HasValue)
            .Select(x => x.CompanyId!.Value)
            .Distinct()
            .ToArray();

        var excludedBranchIds = excludes
            .Where(x => x.AudienceType == AnnouncementAudienceType.Branch && x.BranchId.HasValue)
            .Select(x => x.BranchId!.Value)
            .Distinct()
            .ToArray();

        var excludedDepartmentIds = excludes
            .Where(x => x.AudienceType == AnnouncementAudienceType.Department && x.DepartmentId.HasValue)
            .Select(x => x.DepartmentId!.Value)
            .Distinct()
            .ToArray();

        var excludedPositionIds = excludes
            .Where(x => x.AudienceType == AnnouncementAudienceType.Position && x.PositionId.HasValue)
            .Select(x => x.PositionId!.Value)
            .Distinct()
            .ToArray();

        var excludedEmployeeIds = excludes
            .Where(x => x.AudienceType == AnnouncementAudienceType.Employee && x.EmployeeId.HasValue)
            .Select(x => x.EmployeeId!.Value)
            .Distinct()
            .ToArray();

        var excludeAll = excludes.Any(x => x.AudienceType == AnnouncementAudienceType.All);

        if (excludeAll)
        {
            return new List<int>();
        }

        var excludedIds = await activeEmployees
            .Where(x =>
                excludedCompanyIds.Contains(x.Branch.CompanyId) ||
                excludedBranchIds.Contains(x.BranchId) ||
                excludedDepartmentIds.Contains(x.DepartmentId) ||
                (x.PositionId.HasValue && excludedPositionIds.Contains(x.PositionId.Value)) ||
                excludedEmployeeIds.Contains(x.Id))
            .Select(x => x.Id)
            .Distinct()
            .ToListAsync(cancellationToken);

        var excludedSet = excludedIds.ToHashSet();

        return includedIds
            .Where(x => !excludedSet.Contains(x))
            .ToList();
    }

    private static void AddAudienceRules(
        AnnouncementGroup group,
        AnnouncementCreateRequest request,
        DateTime utcNow)
    {
        var order = 0;

        void AddRule(
            AnnouncementAudienceType audienceType,
            bool isExcluded,
            int? companyId = null,
            int? branchId = null,
            int? departmentId = null,
            int? positionId = null,
            int? employeeId = null)
        {
            order += 10;

            group.AudienceRules.Add(new AnnouncementAudienceRule
            {
                AudienceType = audienceType,
                IsExcluded = isExcluded,
                CompanyId = companyId,
                BranchId = branchId,
                DepartmentId = departmentId,
                PositionId = positionId,
                EmployeeId = employeeId,
                DisplayOrder = order,
                CreatedAt = utcNow
            });
        }

        if (request.AllEmployees)
        {
            AddRule(AnnouncementAudienceType.All, false);
        }
        else
        {
            foreach (var id in request.CompanyIds)
            {
                AddRule(AnnouncementAudienceType.Company, false, companyId: id);
            }

            foreach (var id in request.BranchIds)
            {
                AddRule(AnnouncementAudienceType.Branch, false, branchId: id);
            }

            foreach (var id in request.DepartmentIds)
            {
                AddRule(AnnouncementAudienceType.Department, false, departmentId: id);
            }

            foreach (var id in request.PositionIds)
            {
                AddRule(AnnouncementAudienceType.Position, false, positionId: id);
            }

            foreach (var id in request.EmployeeIds)
            {
                AddRule(AnnouncementAudienceType.Employee, false, employeeId: id);
            }
        }

        foreach (var id in request.ExcludedCompanyIds)
        {
            AddRule(AnnouncementAudienceType.Company, true, companyId: id);
        }

        foreach (var id in request.ExcludedBranchIds)
        {
            AddRule(AnnouncementAudienceType.Branch, true, branchId: id);
        }

        foreach (var id in request.ExcludedDepartmentIds)
        {
            AddRule(AnnouncementAudienceType.Department, true, departmentId: id);
        }

        foreach (var id in request.ExcludedPositionIds)
        {
            AddRule(AnnouncementAudienceType.Position, true, positionId: id);
        }

        foreach (var id in request.ExcludedEmployeeIds)
        {
            AddRule(AnnouncementAudienceType.Employee, true, employeeId: id);
        }
    }

    private async Task<SystemUser?> ResolveActorUserAsync(
        string userName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        return await _dbContext.SystemUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.IsActive &&
                     !x.IsDeleted &&
                     x.UserName == userName,
                cancellationToken);
    }

    private async Task<bool> HasPermissionAsync(
        AnnouncementActorContext actor,
        SystemUser? actorUser,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        if (actor.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (actorUser == null)
        {
            return false;
        }

        var utcNow = DateTime.UtcNow;

        var activeRules = _dbContext.SystemUserPermissions
            .AsNoTracking()
            .Where(assignment =>
                assignment.SystemUserId == actorUser.Id &&
                assignment.Permission.IsActive &&
                assignment.Permission.Code == permissionCode &&
                (!assignment.ValidFromUtc.HasValue || assignment.ValidFromUtc.Value <= utcNow) &&
                (!assignment.ValidToUtc.HasValue || assignment.ValidToUtc.Value > utcNow));

        var denied = await activeRules.AnyAsync(
            assignment =>
                assignment.Effect == PermissionEffect.Deny &&
                assignment.ScopeType == PeopleDataScopeType.All,
            cancellationToken);

        if (denied)
        {
            return false;
        }

        return await activeRules.AnyAsync(
            assignment =>
                assignment.Effect == PermissionEffect.Allow &&
                assignment.ScopeType == PeopleDataScopeType.All,
            cancellationToken);
    }

    private void AddAudit(
        AnnouncementGroup group,
        int? actorSystemUserId,
        AnnouncementActorContext actor,
        string action,
        object values,
        DateTime utcNow)
    {
        _dbContext.AnnouncementAuditLogs.Add(new AnnouncementAuditLog
        {
            AnnouncementGroupId = group.Id,
            TranslationGroupId = group.TranslationGroupId,
            EntityName = nameof(AnnouncementGroup),
            EntityId = group.Id.ToString(),
            Action = action,
            NewValuesJson = JsonSerializer.Serialize(values),
            SystemUserId = actorSystemUserId,
            UserName = actor.UserName,
            IpAddress = actor.IpAddress,
            OccurredAtUtc = utcNow,
            CreatedAt = utcNow
        });
    }

    private static AnnouncementCreateRequest NormalizeRequest(AnnouncementCreateRequest request)
    {
        static int[] NormalizeIds(IReadOnlyCollection<int> values) =>
            values.Where(x => x > 0).Distinct().ToArray();

        return new AnnouncementCreateRequest
        {
            LanguageCode = string.IsNullOrWhiteSpace(request.LanguageCode)
                ? "ar"
                : request.LanguageCode.Trim().ToLowerInvariant(),
            Title = request.Title?.Trim() ?? string.Empty,
            Body = request.Body?.Trim() ?? string.Empty,
            Category = string.IsNullOrWhiteSpace(request.Category)
                ? "عام"
                : request.Category.Trim(),
            PublishNow = request.PublishNow,
            CommentsEnabled = request.CommentsEnabled,
            ReactionsEnabled = request.ReactionsEnabled,
            AllEmployees = request.AllEmployees,
            CompanyIds = NormalizeIds(request.CompanyIds),
            BranchIds = NormalizeIds(request.BranchIds),
            DepartmentIds = NormalizeIds(request.DepartmentIds),
            PositionIds = NormalizeIds(request.PositionIds),
            EmployeeIds = NormalizeIds(request.EmployeeIds),
            ExcludedCompanyIds = NormalizeIds(request.ExcludedCompanyIds),
            ExcludedBranchIds = NormalizeIds(request.ExcludedBranchIds),
            ExcludedDepartmentIds = NormalizeIds(request.ExcludedDepartmentIds),
            ExcludedPositionIds = NormalizeIds(request.ExcludedPositionIds),
            ExcludedEmployeeIds = NormalizeIds(request.ExcludedEmployeeIds)
        };
    }

    private static string? ValidateCreateRequest(AnnouncementCreateRequest request)
    {
        if (request.LanguageCode is not ("ar" or "en"))
        {
            return "لغة الإعلان غير مدعومة.";
        }

        if (string.IsNullOrWhiteSpace(request.Title) ||
            string.IsNullOrWhiteSpace(request.Body))
        {
            return "يرجى إدخال عنوان ووصف الإعلان.";
        }

        var hasAudience =
            request.AllEmployees ||
            request.CompanyIds.Count > 0 ||
            request.BranchIds.Count > 0 ||
            request.DepartmentIds.Count > 0 ||
            request.PositionIds.Count > 0 ||
            request.EmployeeIds.Count > 0;

        if (!hasAudience)
        {
            return "يرجى تحديد جمهور الإعلان.";
        }

        return null;
    }

    private static AnnouncementContent? SelectContent(
        IEnumerable<AnnouncementContent> contents,
        string requestedLanguage)
    {
        var activeContents = contents
            .Where(x => !x.IsDeleted)
            .ToList();

        return activeContents.FirstOrDefault(x =>
                   x.LanguageCode.Equals(requestedLanguage, StringComparison.OrdinalIgnoreCase))
               ?? activeContents.FirstOrDefault(x =>
                   x.LanguageCode.Equals("ar", StringComparison.OrdinalIgnoreCase))
               ?? activeContents.FirstOrDefault();
    }

    private static string BuildAudienceSummary(IEnumerable<AnnouncementAudienceRule> audienceRules)
    {
        var includeRules = audienceRules
            .Where(x => !x.IsDeleted && !x.IsExcluded)
            .OrderBy(x => x.DisplayOrder)
            .ToList();

        if (includeRules.Any(x => x.AudienceType == AnnouncementAudienceType.All))
        {
            return "جميع الموظفين";
        }

        var parts = new List<string>();

        var companyNames = includeRules
            .Where(x => x.AudienceType == AnnouncementAudienceType.Company)
            .Select(x => x.Company?.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        var branchNames = includeRules
            .Where(x => x.AudienceType == AnnouncementAudienceType.Branch)
            .Select(x => x.Branch?.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        var departmentNames = includeRules
            .Where(x => x.AudienceType == AnnouncementAudienceType.Department)
            .Select(x => x.Department?.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        var positionNames = includeRules
            .Where(x => x.AudienceType == AnnouncementAudienceType.Position)
            .Select(x => x.Position?.ArabicName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        var employeeNames = includeRules
            .Where(x => x.AudienceType == AnnouncementAudienceType.Employee)
            .Select(x => x.Employee?.FullName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        if (companyNames.Count > 0)
        {
            parts.Add($"شركة: {string.Join("، ", companyNames)}");
        }

        if (branchNames.Count > 0)
        {
            parts.Add($"فرع: {string.Join("، ", branchNames)}");
        }

        if (departmentNames.Count > 0)
        {
            parts.Add($"قسم: {string.Join("، ", departmentNames)}");
        }

        if (positionNames.Count > 0)
        {
            parts.Add($"مسمى: {string.Join("، ", positionNames)}");
        }

        if (employeeNames.Count > 0)
        {
            parts.Add(employeeNames.Count == 1
                ? $"موظف: {employeeNames[0]}"
                : $"موظفون محددون: {employeeNames.Count}");
        }

        return parts.Count == 0
            ? "جمهور محدد"
            : string.Join(" | ", parts);
    }

    private static DateTimeOffset GetBaghdadNow()
    {
        var zone = FindBaghdadTimeZone();
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
    }

    private static TimeZoneInfo FindBaghdadTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Arabic Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Baghdad");
        }
    }
}
