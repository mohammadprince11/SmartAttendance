namespace SmartAttendance.Domain.Enums;

public enum AnnouncementStatus
{
    Pending = 1,
    Scheduled = 2,
    Published = 3,
    Expired = 4,
    Archived = 5
}

public enum AnnouncementExpirationBehavior
{
    Archive = 1,
    KeepVisibleAsExpired = 2
}

public enum AnnouncementAudienceType
{
    All = 1,
    Company = 2,
    Branch = 3,
    Department = 4,
    Position = 5,
    Employee = 6
}

public enum AnnouncementChannelType
{
    EmployeeWall = 1,
    InSystemNotification = 2
}

public enum AnnouncementSignatureType
{
    SavedSignature = 1,
    HumanResources = 2,
    CompanyManagement = 3,
    CustomText = 4,
    None = 5
}

public enum AnnouncementCommentStatus
{
    Published = 1,
    Hidden = 2,
    Deleted = 3
}

public enum AnnouncementReactionType
{
    Like = 1,
    Support = 2,
    Celebrate = 3,
    Thanks = 4,
    Sad = 5
}

public enum UserNotificationType
{
    Announcement = 1
}
