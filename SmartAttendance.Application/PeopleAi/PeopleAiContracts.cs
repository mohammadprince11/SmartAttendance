namespace SmartAttendance.Application.PeopleAi;

public enum PeopleAiDuplicateScope
{
    CurrentCompany = 1,
    AuthorizedCompanies = 2,
    WholeTenant = 3
}

public enum PeopleAiDuplicateAction
{
    WarnOnly = 1,
    RequireReview = 2,
    BlockCreation = 3
}

public enum PeopleAiReviewerMode
{
    AdminOnly = 1,
    AdminOrCreatorWithPermission = 2
}

public static class PeopleAiDocumentTypes
{
    public const string NationalId = "NationalId";
    public const string Passport = "Passport";
    public const string Residence = "Residence";
    public const string Visa = "Visa";
    public const string Contract = "Contract";
    public const string Cv = "CV";
    public const string Unknown = "Unknown";
}

public sealed record CompanyPeopleAiPolicy(
    int CompanyId,
    PeopleAiDuplicateScope DuplicateScope,
    PeopleAiDuplicateAction DuplicateAction,
    PeopleAiReviewerMode ReviewerMode,
    IReadOnlyList<string> EnabledLanguages,
    bool CloudProcessingAllowed,
    bool IsEnabled);

public sealed record PeopleAiDocumentTypeDefinition(
    int CompanyId,
    string DocumentType,
    string DisplayLabel,
    int SortOrder,
    bool IsActive);

public sealed record EmployeeDocumentPolicy(
    int CompanyId,
    string DocumentType,
    string EmployeeCategory,
    string Requirement,
    bool RequireExpiryDate,
    bool RequireOriginalVerification,
    bool IsActive);

public sealed record PeopleAiFieldPolicy(
    int CompanyId,
    string DocumentType,
    string FieldKey,
    string DisplayLabel,
    string Requirement,
    int SortOrder,
    bool AllowBulkApprove,
    bool IsActive);

public sealed record IdentityDuplicateCandidate(
    int EmployeeId,
    int CompanyId,
    string EmployeeNo,
    string DisplayName,
    string DocumentType,
    string NormalizedDocumentNumber,
    bool IsActive,
    bool IsVisibleToRequester);

public sealed record IdentityDuplicateResult(
    string DocumentType,
    string NormalizedDocumentNumber,
    PeopleAiDuplicateAction Action,
    IReadOnlyList<IdentityDuplicateCandidate> Candidates)
{
    public bool HasDuplicate => Candidates.Count > 0;
    public bool BlocksCreation =>
        HasDuplicate && Action == PeopleAiDuplicateAction.BlockCreation;
    public bool RequiresReview =>
        HasDuplicate && Action == PeopleAiDuplicateAction.RequireReview;
}

public static class IdentityDocumentNormalizer
{
    public static string NormalizeNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Trim()
            .Select(NormalizeDigit)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray();

        return new string(chars);
    }

    private static char NormalizeDigit(char value)
    {
        if (value is >= '٠' and <= '٩')
        {
            return (char)('0' + (value - '٠'));
        }

        if (value is >= '۰' and <= '۹')
        {
            return (char)('0' + (value - '۰'));
        }

        return value;
    }
}
