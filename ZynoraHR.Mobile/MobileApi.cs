using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZynoraHR.Mobile;

public sealed class MobileApi
{
    private const string TokenKey = "zynora.mobile.api_token";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri(AppSettings.BaseUrl),
        Timeout = TimeSpan.FromSeconds(25)
    };

    public async Task<bool> HasSessionAsync()
    {
        try { return !string.IsNullOrWhiteSpace(await SecureStorage.Default.GetAsync(TokenKey)); }
        catch { return false; }
    }

    public async Task<MobileLoginResult> LoginAsync(
        string username,
        string password,
        string? twoFactorCode = null,
        string? recoveryCode = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new
                {
                    username = username.Trim(),
                    password,
                    twoFactorCode,
                    recoveryCode
                },
                options: Json)
        };
        ApplyLanguageHeader(request);

        using var response = await _http.SendAsync(request);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);

        if (response.StatusCode == HttpStatusCode.Unauthorized &&
            result?.RequiresTwoFactor == true)
        {
            return new MobileLoginResult(
                Success: false,
                RequiresTwoFactor: true,
                Message: result.Message ?? "أدخل رمز المصادقة الثنائية.");
        }

        if (!response.IsSuccessStatusCode)
            throw new MobileApiException(result?.Message ?? await ErrorAsync(response));

        if (string.IsNullOrWhiteSpace(result?.Token))
            throw new MobileApiException("استجابة تسجيل الدخول غير صالحة.");

        await SecureStorage.Default.SetAsync(TokenKey, result.Token);
        return new MobileLoginResult(true, false, null);
    }

    public async Task LogoutAsync()
    {
        try
        {
            using var request=await AuthorizedAsync(HttpMethod.Post,"api/v1/auth/logout");
            using var response=await _http.SendAsync(request);
        }
        finally { SecureStorage.Default.Remove(TokenKey); }
    }

    public Task<MobileTwoFactorStatus> TwoFactorStatusAsync() =>
        SendAsync<MobileTwoFactorStatus>(HttpMethod.Get, "api/v1/auth/2fa/status");

    public Task<MobileTwoFactorSetup> BeginTwoFactorSetupAsync(string currentPassword) =>
        SendAsync<MobileTwoFactorSetup>(
            HttpMethod.Post,
            "api/v1/auth/2fa/setup",
            new { currentPassword });

    public Task<MobileTwoFactorAction> EnableTwoFactorAsync(string code) =>
        SendAsync<MobileTwoFactorAction>(
            HttpMethod.Post,
            "api/v1/auth/2fa/enable",
            new { code });

    public Task<MobileTwoFactorAction> DisableTwoFactorAsync(
        string currentPassword,
        string? code,
        string? recoveryCode) =>
        SendAsync<MobileTwoFactorAction>(
            HttpMethod.Post,
            "api/v1/auth/2fa/disable",
            new { currentPassword, code, recoveryCode });

    public Task<MobileTwoFactorAction> RegenerateTwoFactorRecoveryCodesAsync(
        string currentPassword,
        string? code,
        string? recoveryCode) =>
        SendAsync<MobileTwoFactorAction>(
            HttpMethod.Post,
            "api/v1/auth/2fa/recovery-codes",
            new { currentPassword, code, recoveryCode });

    public void ClearSession() => SecureStorage.Default.Remove(TokenKey);

    public Task<EmployeeProfile> ProfileAsync() =>
        SendAsync<EmployeeProfile>(HttpMethod.Get,"api/v1/me");

    public Task<List<MobileTeamMember>> TeamAsync() =>
        SendAsync<List<MobileTeamMember>>(
            HttpMethod.Get,
            "api/v1/me/team");

    public Task<List<MobileApprovalItem>> ApprovalsAsync() =>
        SendAsync<List<MobileApprovalItem>>(
            HttpMethod.Get,
            "api/v1/me/approvals");

    public Task<ApiMessage> ApproveAsync(
        int id,
        string? note,
        IReadOnlyCollection<string>? approvedFieldKeys = null) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            $"api/v1/me/approvals/{id}/approve",
            new
            {
                note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                approvedFieldKeys
            });

    public Task<ApiMessage> RejectApprovalAsync(
        int id,
        string? note) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            $"api/v1/me/approvals/{id}/reject",
            new
            {
                note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            });

    public Task<ApiMessage> ReturnApprovalAsync(
        int id,
        string? note) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            $"api/v1/me/approvals/{id}/return",
            new
            {
                note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            });

    public Task<List<MobileIdentityDocument>> IdentityDocumentsAsync() =>
        SendAsync<List<MobileIdentityDocument>>(
            HttpMethod.Get,
            "api/v1/me/identity-documents");

    public async Task<byte[]?> ProfilePhotoAsync()
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, "api/v1/me/photo");
        using var response = await _http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            SecureStorage.Default.Remove(TokenKey);
            throw new MobileSessionExpiredException();
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new MobileApiException(await ErrorAsync(response));

        return await response.Content.ReadAsByteArrayAsync();
    }

    public Task<List<AttendanceDay>> AttendanceAsync() =>
        SendAsync<List<AttendanceDay>>(HttpMethod.Get,"api/v1/me/attendance?days=7");

    public Task<List<LeaveBalance>> LeaveBalancesAsync() =>
        SendAsync<List<LeaveBalance>>(HttpMethod.Get,"api/v1/me/leave-balance");

    public Task<List<MobileAnnouncement>> AnnouncementsAsync() =>
        SendAsync<List<MobileAnnouncement>>(
            HttpMethod.Get,
            "api/v1/me/announcements?take=5");

    public Task<List<MobilePoll>> PollsAsync() =>
        SendAsync<List<MobilePoll>>(
            HttpMethod.Get,
            "api/v1/me/polls");

    public Task<ApiMessage> VotePollAsync(int id, int optionId) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            $"api/v1/me/polls/{id}/vote",
            new { optionId });

    public Task<List<MobileSurveySummary>> SurveysAsync() =>
        SendAsync<List<MobileSurveySummary>>(
            HttpMethod.Get,
            "api/v1/me/surveys");

    public Task<MobileSurveyDetail> SurveyAsync(int id) =>
        SendAsync<MobileSurveyDetail>(
            HttpMethod.Get,
            $"api/v1/me/surveys/{id}");

    public Task<ApiMessage> SubmitSurveyAsync(
        int id,
        Guid submissionToken,
        IReadOnlyDictionary<int, string?> answers) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            $"api/v1/me/surveys/{id}",
            new
            {
                submissionToken,
                answers = answers.Select(item => new
                {
                    fieldId = item.Key,
                    value = item.Value
                }).ToArray()
            });

    public Task<List<MobileFeedbackItem>> FeedbackAsync() =>
        SendAsync<List<MobileFeedbackItem>>(
            HttpMethod.Get,
            "api/v1/me/feedback");

    public Task<ApiMessage> SubmitFeedbackAsync(
        string type,
        string priority,
        string title,
        string message) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            "api/v1/me/feedback",
            new { type, priority, title, message });

    public Task<List<MobileDisciplinaryItem>> DisciplineAsync() =>
        SendAsync<List<MobileDisciplinaryItem>>(
            HttpMethod.Get,
            "api/v1/me/discipline");

    public Task<List<MobileBiometricKey>> BiometricKeysAsync() =>
        SendAsync<List<MobileBiometricKey>>(
            HttpMethod.Get,
            "api/v1/me/biometric-keys");

    public Task<WebAuthnRegistrationOptions> BeginBiometricRegistrationAsync() =>
        SendAsync<WebAuthnRegistrationOptions>(
            HttpMethod.Post,
            "api/v1/webauthn/register/options");

    public async Task<ApiMessage> CompleteBiometricRegistrationAsync(
        string key,
        string registrationResponseJson,
        string? deviceLabel)
    {
        using var document = JsonDocument.Parse(registrationResponseJson);
        var attestation = document.RootElement.Clone();

        return await SendAsync<ApiMessage>(
            HttpMethod.Post,
            "api/v1/webauthn/register/complete",
            new
            {
                key,
                label = string.IsNullOrWhiteSpace(deviceLabel)
                    ? null
                    : deviceLabel.Trim(),
                attestation
            });
    }

    public Task<MobileCompensation> CompensationAsync() =>
        SendAsync<MobileCompensation>(
            HttpMethod.Get,
            "api/v1/me/compensation");

    public Task<WebAuthnRegistrationOptions> BeginBiometricPunchAsync() =>
        SendAsync<WebAuthnRegistrationOptions>(
            HttpMethod.Post,
            "api/v1/webauthn/punch/options");

    public async Task<string> CompleteBiometricPunchAsync(
        string key,
        string assertionResponseJson)
    {
        using var document = JsonDocument.Parse(assertionResponseJson);
        var assertion = document.RootElement.Clone();

        var response = await SendAsync<WebAuthnProofResponse>(
            HttpMethod.Post,
            "api/v1/webauthn/punch/verify",
            new { key, assertion });

        if (string.IsNullOrWhiteSpace(response.Token))
            throw new MobileApiException("لم يرجع الخادم إثباتاً بيولوجياً صالحاً.");

        return response.Token;
    }

    public Task<ApiMessage> PunchAsync(
        string type,
        double lat,
        double lng,
        string? bioToken = null) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            "api/v1/me/online-punch",
            new
            {
                punchType = type == "Out" ? "Out" : "In",
                latitude = lat,
                longitude = lng,
                bioToken
            });

    public Task<List<MobileRequest>> RequestsAsync() =>
        SendAsync<List<MobileRequest>>(HttpMethod.Get,"api/v1/me/requests");

    public Task<RequestCatalogResponse> RequestTypesAsync() =>
        SendAsync<RequestCatalogResponse>(HttpMethod.Get,"api/v1/me/request-types");

    public async Task<ApiMessage> SubmitRequestAsync(
        MobileRequestType requestType,
        DateTime fromDate,
        DateTime toDate,
        TimeSpan? startTime,
        TimeSpan? endTime,
        string? reason,
        FileResult? attachment)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(requestType.Id.ToString()), "RequestTypeId");
        content.Add(new StringContent(fromDate.ToString("yyyy-MM-dd")), "FromDate");
        content.Add(new StringContent(toDate.ToString("yyyy-MM-dd")), "ToDate");

        if (startTime is { } start)
            content.Add(new StringContent($"{(int)start.TotalHours:00}:{start.Minutes:00}"), "StartTime");
        if (endTime is { } end)
            content.Add(new StringContent($"{(int)end.TotalHours:00}:{end.Minutes:00}"), "EndTime");
        if (!string.IsNullOrWhiteSpace(reason))
            content.Add(new StringContent(reason.Trim()), "Reason");

        if (attachment is not null)
        {
            var stream = await attachment.OpenReadAsync();
            var fileContent = new StreamContent(stream);
            content.Add(fileContent, "Attachment", attachment.FileName);
        }

        return await SendContentAsync<ApiMessage>(
            HttpMethod.Post, "api/v1/me/requests/create", content);
    }

    public Task<ApiMessage> CancelRequestAsync(int requestId,string? reason=null) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            $"api/v1/me/requests/{requestId}/cancel",
            new { reason=string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() });

    public Task<List<MissingPunchItem>> MissingPunchesAsync() =>
        SendAsync<List<MissingPunchItem>>(HttpMethod.Get,"api/v1/me/missing-punch");

    public Task<List<DayPunchItem>> DayPunchesAsync(DateTime date) =>
        SendAsync<List<DayPunchItem>>(
            HttpMethod.Get,
            $"api/v1/me/punches?date={Uri.EscapeDataString(date.ToString("yyyy-MM-dd"))}");

    public Task<ApiMessage> SubmitMissingPunchAsync(
        DateTime date,TimeSpan time,string? reason) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            "api/v1/me/missing-punch",
            new {
                date=date.ToString("yyyy-MM-dd"),
                time=$"{(int)time.TotalHours:00}:{time.Minutes:00}",
                reason=string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
            });

    public Task<List<DataChangeField>> DataChangeFieldsAsync() =>
        SendAsync<List<DataChangeField>>(HttpMethod.Get, "api/v1/me/data-change/fields");

    public Task<ApiMessage> SubmitDataChangeAsync(
        IEnumerable<DataChangeSubmissionField> fields,
        string? reason) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            "api/v1/me/data-change",
            new
            {
                fields = fields.Select(field => new
                {
                    key = field.Key,
                    newValue = field.NewValue
                }).ToArray(),
                reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
            });

    public async Task<ApiMessage> SubmitDataChangeMultipartAsync(
        IEnumerable<DataChangeSubmissionField> fields,
        string? reason,
        FileResult? photo)
    {
        using var content = new MultipartFormDataContent();
        var fieldsJson = JsonSerializer.Serialize(
            fields.Select(field => new
            {
                key = field.Key,
                newValue = field.NewValue
            }).ToArray(),
            Json);

        content.Add(new StringContent(fieldsJson), "FieldsJson");

        if (!string.IsNullOrWhiteSpace(reason))
            content.Add(new StringContent(reason.Trim()), "Reason");

        if (photo is not null)
        {
            var stream = await photo.OpenReadAsync();
            var fileContent = new StreamContent(stream);
            content.Add(fileContent, "Photo", photo.FileName);
        }

        return await SendContentAsync<ApiMessage>(
            HttpMethod.Post,
            "api/v1/me/data-change/multipart",
            content);
    }

    public Task<FinancialCatalogResponse> FinancialCatalogAsync() =>
        SendAsync<FinancialCatalogResponse>(HttpMethod.Get, "api/v1/me/financial/catalog");

    public Task<ApiMessage> SubmitFinancialAsync(
        string kind,
        decimal amount,
        int installmentCount,
        int startYear,
        int startMonth,
        string? reason) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            "api/v1/me/financial",
            new
            {
                kind,
                amount,
                installmentCount,
                startYear,
                startMonth,
                reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
            });

    public Task<ShiftCatalogResponse> ShiftCatalogAsync() =>
        SendAsync<ShiftCatalogResponse>(HttpMethod.Get, "api/v1/me/shift/catalog");

    public Task<ApiMessage> SubmitShiftAsync(
        int shiftTypeId,
        DateTime fromDate,
        DateTime toDate,
        string? reason) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            "api/v1/me/shift",
            new
            {
                shiftTypeId,
                fromDate = fromDate.ToString("yyyy-MM-dd"),
                toDate = toDate.ToString("yyyy-MM-dd"),
                reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
            });

    private async Task<T> SendContentAsync<T>(
        HttpMethod method,
        string path,
        HttpContent content)
    {
        using var request = await AuthorizedAsync(method, path);
        request.Content = content;
        using var response = await _http.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            SecureStorage.Default.Remove(TokenKey);
            throw new MobileSessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
            throw new MobileApiException(await ErrorAsync(response));

        return await response.Content.ReadFromJsonAsync<T>(Json)
            ?? throw new MobileApiException("لم تصل بيانات صالحة من الخادم.");
    }

    private async Task<T> SendAsync<T>(HttpMethod method,string path,object? body=null)
    {
        using var request=await AuthorizedAsync(method,path,body);
        using var response=await _http.SendAsync(request);

        if(response.StatusCode==HttpStatusCode.Unauthorized)
        {
            SecureStorage.Default.Remove(TokenKey);
            throw new MobileSessionExpiredException();
        }

        if(!response.IsSuccessStatusCode)
            throw new MobileApiException(await ErrorAsync(response));

        return await response.Content.ReadFromJsonAsync<T>(Json)
            ?? throw new MobileApiException("لم تصل بيانات صالحة من الخادم.");
    }

    private async Task<HttpRequestMessage> AuthorizedAsync(
        HttpMethod method,string path,object? body=null)
    {
        string? token;
        try { token=await SecureStorage.Default.GetAsync(TokenKey); }
        catch { token=null; }

        if(string.IsNullOrWhiteSpace(token))
            throw new MobileSessionExpiredException();

        var request=new HttpRequestMessage(method,path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        ApplyLanguageHeader(request);
        if(body is not null) request.Content=JsonContent.Create(body,options:Json);
        return request;
    }

    private static void ApplyLanguageHeader(HttpRequestMessage request)
    {
        request.Headers.AcceptLanguage.Clear();
        request.Headers.AcceptLanguage.Add(
            new StringWithQualityHeaderValue(UiLocalization.CurrentCultureCode));
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var error=await response.Content.ReadFromJsonAsync<ApiError>(Json);
            if(!string.IsNullOrWhiteSpace(error?.Message)) return error.Message.Trim();
        }
        catch {}

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "بيانات الدخول غير صحيحة أو انتهت الجلسة.",
            HttpStatusCode.Forbidden => "لا تملك صلاحية تنفيذ هذه العملية.",
            _ => $"تعذر الاتصال بخدمة ZYNORA حالياً. (HTTP {(int)response.StatusCode})"
        };
    }
}

public class MobileApiException(string message) : Exception(message);
public sealed class MobileSessionExpiredException()
    : MobileApiException("انتهت جلسة الدخول. سجل الدخول مرة أخرى.");

public sealed class LoginResponse
{
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("requiresTwoFactor")] public bool RequiresTwoFactor { get; set; }
}

public sealed record MobileLoginResult(
    bool Success,
    bool RequiresTwoFactor,
    string? Message);

public sealed class MobileTwoFactorStatus
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("setupInProgress")] public bool SetupInProgress { get; set; }
    [JsonPropertyName("recoveryCodesRemaining")] public int RecoveryCodesRemaining { get; set; }
}

public sealed class MobileTwoFactorSetup
{
    [JsonPropertyName("secret")] public string Secret { get; set; } = "";
    [JsonPropertyName("otpAuthUri")] public string OtpAuthUri { get; set; } = "";
    [JsonPropertyName("digits")] public int Digits { get; set; }
    [JsonPropertyName("periodSeconds")] public int PeriodSeconds { get; set; }
}

public sealed class MobileTwoFactorAction
{
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("recoveryCodes")] public List<string> RecoveryCodes { get; set; } = new();
    [JsonPropertyName("requiresLogin")] public bool RequiresLogin { get; set; }
}

public sealed class EmployeeProfile
{
    [JsonPropertyName("employeeNo")] public string EmployeeNo { get; set; } = "";
    [JsonPropertyName("fullName")] public string FullName { get; set; } = "";
    [JsonPropertyName("position")] public string Position { get; set; } = "";
    [JsonPropertyName("positionEn")] public string PositionEn { get; set; } = "";
    [JsonPropertyName("department")] public string Department { get; set; } = "";
    [JsonPropertyName("departmentEn")] public string DepartmentEn { get; set; } = "";
    [JsonPropertyName("branch")] public string Branch { get; set; } = "";
    [JsonPropertyName("branchEn")] public string BranchEn { get; set; } = "";
    [JsonPropertyName("phone")] public string Phone { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("personalEmail")] public string PersonalEmail { get; set; } = "";
    [JsonPropertyName("nationalId")] public string NationalId { get; set; } = "";
    [JsonPropertyName("hireDate")] public string? HireDate { get; set; }
    [JsonPropertyName("joiningDate")] public string? JoiningDate { get; set; }
    [JsonPropertyName("birthDate")] public string? BirthDate { get; set; }
    [JsonPropertyName("country")] public string Country { get; set; } = "";
    [JsonPropertyName("gender")] public string Gender { get; set; } = "";
    [JsonPropertyName("maritalStatus")] public string MaritalStatus { get; set; } = "";
    [JsonPropertyName("nationality")] public string Nationality { get; set; } = "";
    [JsonPropertyName("employmentStatus")] public string EmploymentStatus { get; set; } = "";
    [JsonPropertyName("workType")] public string WorkType { get; set; } = "";
    [JsonPropertyName("jobGrade")] public string JobGrade { get; set; } = "";
    [JsonPropertyName("firstNameEn")] public string FirstNameEn { get; set; } = "";
    [JsonPropertyName("secondNameEn")] public string SecondNameEn { get; set; } = "";
    [JsonPropertyName("thirdNameEn")] public string ThirdNameEn { get; set; } = "";
    [JsonPropertyName("lastNameEn")] public string LastNameEn { get; set; } = "";
    [JsonPropertyName("passportNo")] public string PassportNo { get; set; } = "";
    [JsonPropertyName("hasPhoto")] public bool HasPhoto { get; set; }
    [JsonPropertyName("photoUrl")] public string? PhotoUrl { get; set; }
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }

    public string EnglishName =>
        string.Join(" ", new[] { FirstNameEn, SecondNameEn, ThirdNameEn, LastNameEn }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public string DisplayName => UiLocalization.Data(FullName, EnglishName);
    public string DisplayPosition => UiLocalization.Data(Position, PositionEn);
    public string DisplayDepartment => UiLocalization.Data(Department, DepartmentEn);
    public string DisplayBranch => UiLocalization.Data(Branch, BranchEn);
    public string DisplayCountry => UiLocalization.SystemData(Country);
    public string DisplayGender => UiLocalization.SystemData(Gender);
    public string DisplayMaritalStatus => UiLocalization.SystemData(MaritalStatus);
    public string DisplayNationality => UiLocalization.SystemData(Nationality);
    public string DisplayEmploymentStatus => UiLocalization.SystemData(EmploymentStatus);
    public string DisplayWorkType => UiLocalization.SystemData(WorkType);
    public string DisplayJobGrade => UiLocalization.Data(JobGrade);

    public string PreferredEmail =>
        !string.IsNullOrWhiteSpace(Email) ? Email : PersonalEmail;
}

public sealed class MobileTeamMember
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("employeeNo")] public string EmployeeNo { get; set; } = "";
    [JsonPropertyName("fullName")] public string FullName { get; set; } = "";
    [JsonPropertyName("fullNameEn")] public string FullNameEn { get; set; } = "";
    [JsonPropertyName("position")] public string Position { get; set; } = "";
    [JsonPropertyName("positionEn")] public string PositionEn { get; set; } = "";
    [JsonPropertyName("department")] public string Department { get; set; } = "";
    [JsonPropertyName("departmentEn")] public string DepartmentEn { get; set; } = "";
    [JsonPropertyName("branch")] public string Branch { get; set; } = "";
    [JsonPropertyName("branchEn")] public string BranchEn { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("phone")] public string Phone { get; set; } = "";
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
    [JsonPropertyName("checkIn")] public string? CheckIn { get; set; }
    [JsonPropertyName("checkOut")] public string? CheckOut { get; set; }

    public string DisplayName => UiLocalization.Data(FullName, FullNameEn);
    public string DisplayPosition => UiLocalization.Data(Position, PositionEn);
    public string DisplayDepartment => UiLocalization.Data(Department, DepartmentEn);
    public string DisplayBranch => UiLocalization.Data(Branch, BranchEn);

    public string AttendanceToday =>
        string.IsNullOrWhiteSpace(CheckIn) && string.IsNullOrWhiteSpace(CheckOut)
            ? UiLocalization.T("لا توجد بصمات اليوم")
            : $"{CheckIn ?? "—"}  →  {CheckOut ?? "—"}";
}

public sealed class MobileApprovalItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("employeeId")] public int EmployeeId { get; set; }
    [JsonPropertyName("employeeNo")] public string EmployeeNo { get; set; } = "";
    [JsonPropertyName("employeeName")] public string EmployeeName { get; set; } = "";
    [JsonPropertyName("employeeNameEn")] public string EmployeeNameEn { get; set; } = "";
    [JsonPropertyName("position")] public string Position { get; set; } = "";
    [JsonPropertyName("positionEn")] public string PositionEn { get; set; } = "";
    [JsonPropertyName("department")] public string Department { get; set; } = "";
    [JsonPropertyName("departmentEn")] public string DepartmentEn { get; set; } = "";
    [JsonPropertyName("branch")] public string Branch { get; set; } = "";
    [JsonPropertyName("branchEn")] public string BranchEn { get; set; } = "";
    [JsonPropertyName("requestType")] public string RequestType { get; set; } = "";
    [JsonPropertyName("requestTypeEn")] public string RequestTypeEn { get; set; } = "";
    [JsonPropertyName("fromDate")] public string? FromDate { get; set; }
    [JsonPropertyName("toDate")] public string? ToDate { get; set; }
    [JsonPropertyName("startTime")] public string? StartTime { get; set; }
    [JsonPropertyName("endTime")] public string? EndTime { get; set; }
    [JsonPropertyName("daysCount")] public decimal? DaysCount { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }
    [JsonPropertyName("currentStep")] public string CurrentStep { get; set; } = "";
    [JsonPropertyName("commentRequiredOnReject")] public bool CommentRequiredOnReject { get; set; }
    [JsonPropertyName("dataChangeFields")] public List<MobileApprovalField> DataChangeFields { get; set; } = new();
    [JsonPropertyName("financial")] public MobileApprovalFinancial? Financial { get; set; }

    public string DisplayEmployeeName => UiLocalization.Data(EmployeeName, EmployeeNameEn);
    public string DisplayPosition => UiLocalization.Data(Position, PositionEn);
    public string DisplayDepartment => UiLocalization.Data(Department, DepartmentEn);
    public string DisplayBranch => UiLocalization.Data(Branch, BranchEn);
    public string DisplayRequestType => UiLocalization.Data(RequestType, RequestTypeEn);

    public string DateRange =>
        string.IsNullOrWhiteSpace(ToDate) ||
        string.Equals(FromDate, ToDate, StringComparison.Ordinal)
            ? (FromDate ?? "—")
            : $"{FromDate ?? "—"} → {ToDate}";

    public string TimeRange =>
        string.IsNullOrWhiteSpace(StartTime) && string.IsNullOrWhiteSpace(EndTime)
            ? string.Empty
            : $"{StartTime ?? "—"} → {EndTime ?? "—"}";
}

public sealed class MobileApprovalField
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("oldValue")] public string OldValue { get; set; } = "";
    [JsonPropertyName("newValue")] public string NewValue { get; set; } = "";
    [JsonPropertyName("decision")] public string Decision { get; set; } = "Approved";

    public string DisplayLabel => UiLocalization.Data(Label);
}

public sealed class MobileApprovalFinancial
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("kindLabel")] public string KindLabel { get; set; } = "";
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("installmentCount")] public int InstallmentCount { get; set; }
    [JsonPropertyName("period")] public string Period { get; set; } = "";
    [JsonPropertyName("raiseType")] public string RaiseType { get; set; } = "";
    [JsonPropertyName("paymentType")] public string PaymentType { get; set; } = "";
    [JsonPropertyName("taxable")] public bool Taxable { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }

    public string DisplayKindLabel => UiLocalization.Data(KindLabel);
    public string DisplayPeriod => UiLocalization.Data(Period);
    public string DisplayPaymentType => UiLocalization.Data(PaymentType);
}

public sealed class MobileIdentityDocument
{
    [JsonPropertyName("documentType")] public string DocumentType { get; set; } = "";
    [JsonPropertyName("countryCode")] public string CountryCode { get; set; } = "";
    [JsonPropertyName("documentNumber")] public string DocumentNumber { get; set; } = "";
    [JsonPropertyName("nationalNumber")] public string NationalNumber { get; set; } = "";
    [JsonPropertyName("familyNumber")] public string FamilyNumber { get; set; } = "";
    [JsonPropertyName("issueDate")] public string? IssueDate { get; set; }
    [JsonPropertyName("expiryDate")] public string? ExpiryDate { get; set; }
    [JsonPropertyName("verificationStatus")] public string VerificationStatus { get; set; } = "";
    [JsonPropertyName("originalVerificationStatus")] public string OriginalVerificationStatus { get; set; } = "";

    public string DisplayName => DocumentType switch
    {
        "NationalId" => UiLocalization.T("البطاقة الوطنية"),
        "Passport" => UiLocalization.T("جواز السفر"),
        "Residence" => UiLocalization.T("الإقامة"),
        _ => string.IsNullOrWhiteSpace(DocumentType)
            ? UiLocalization.T("مستند")
            : UiLocalization.Data(DocumentType)
    };

    public string PrimaryNumber =>
        !string.IsNullOrWhiteSpace(DocumentNumber)
            ? DocumentNumber
            : !string.IsNullOrWhiteSpace(NationalNumber)
                ? NationalNumber
                : "—";

    public string ExpirySummary =>
        string.IsNullOrWhiteSpace(ExpiryDate)
            ? UiLocalization.T("بدون تاريخ انتهاء")
            : UiLocalization.T($"ينتهي {ExpiryDate}");
}

public sealed class AttendanceDay
{
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("checkIn")] public string? CheckIn { get; set; }
    [JsonPropertyName("checkOut")] public string? CheckOut { get; set; }

    public string DisplayStatus => UiLocalization.SystemData(Status);

    public string AttendanceTimeline =>
        $"{CheckIn ?? "—"}  →  {CheckOut ?? "—"}";
}

public sealed class LeaveBalance
{
    [JsonPropertyName("requestTypeId")] public int RequestTypeId { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("typeEn")] public string? TypeEn { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("categoryEn")] public string? CategoryEn { get; set; }
    [JsonPropertyName("unit")] public string Unit { get; set; } = "";

    public string DisplayType => RequestTypeId switch
    {
        1 => UiLocalization.T("إجازة سنوية"),
        2 => UiLocalization.T("إجازة مرضية"),
        _ => UiLocalization.Data(Type, TypeEn)
    };

    [JsonPropertyName("entitled")] public decimal Entitled { get; set; }
    [JsonPropertyName("used")] public decimal Used { get; set; }
    [JsonPropertyName("remaining")] public decimal Remaining { get; set; }

    public string BalanceSummary =>
        string.Format(
            CultureInfo.CurrentCulture,
            UiLocalization.T("مستخدم {0} من {1}"),
            Used.ToString("0.##", CultureInfo.CurrentCulture),
            Entitled.ToString("0.##", CultureInfo.CurrentCulture));
}

public sealed class MobileCompensation
{
    [JsonPropertyName("hasData")] public bool HasData { get; set; }
    [JsonPropertyName("basicSalary")] public decimal BasicSalary { get; set; }
    [JsonPropertyName("allowances")] public decimal Allowances { get; set; }
    [JsonPropertyName("deductions")] public decimal Deductions { get; set; }
    [JsonPropertyName("gross")] public decimal Gross { get; set; }
    [JsonPropertyName("net")] public decimal Net { get; set; }
    [JsonPropertyName("taxAmount")] public decimal TaxAmount { get; set; }
    [JsonPropertyName("gosiEmployee")] public decimal GosiEmployee { get; set; }
    [JsonPropertyName("otherDeductions")] public decimal OtherDeductions { get; set; }
    [JsonPropertyName("paymentMethod")] public string PaymentMethod { get; set; } = "";
    [JsonPropertyName("bankName")] public string BankName { get; set; } = "";
    [JsonPropertyName("bankAccount")] public string BankAccount { get; set; } = "";
    [JsonPropertyName("currency")] public string Currency { get; set; } = "IQD";
    [JsonPropertyName("payrollRunId")] public int? PayrollRunId { get; set; }
    [JsonPropertyName("payrollYear")] public int? PayrollYear { get; set; }
    [JsonPropertyName("payrollMonth")] public int? PayrollMonth { get; set; }
    [JsonPropertyName("payrollStatus")] public string PayrollStatus { get; set; } = "";
    [JsonPropertyName("workDays")] public decimal WorkDays { get; set; }
    [JsonPropertyName("absentDays")] public decimal AbsentDays { get; set; }

    public string PayrollPeriod =>
        PayrollYear is int year && PayrollMonth is int month
            ? $"{year:D4}-{month:D2}"
            : "—";
}

public sealed class MobileAnnouncement
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("body")] public string Body { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("publishDate")] public string? PublishDate { get; set; }
    [JsonPropertyName("isRead")] public bool IsRead { get; set; }
    [JsonPropertyName("firstReadAtUtc")] public DateTime? FirstReadAtUtc { get; set; }

    public string DisplayCategory => UiLocalization.Data(Category);

    public string Meta =>
        string.Join(
            " · ",
            new[] { Category, PublishDate }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class MobilePoll
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("question")] public string Question { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("publishDate")] public DateTime? PublishDate { get; set; }
    [JsonPropertyName("hasVoted")] public bool HasVoted { get; set; }
    [JsonPropertyName("options")] public List<MobilePollOption> Options { get; set; } = new();
}

public sealed class MobilePollOption
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("displayOrder")] public int DisplayOrder { get; set; }
}

public class MobileSurveySummary
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("nameEn")] public string? NameEn { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("formType")] public string FormType { get; set; } = "";
    [JsonPropertyName("typeLabel")] public string TypeLabel { get; set; } = "";
    [JsonPropertyName("submitted")] public bool Submitted { get; set; }

    public string DisplayName => UiLocalization.Data(Name, NameEn);
    public string DisplayTypeLabel => UiLocalization.Data(TypeLabel);
}

public sealed class MobileSurveyDetail : MobileSurveySummary
{
    [JsonPropertyName("groups")] public List<MobileSurveyGroup> Groups { get; set; } = new();
    [JsonPropertyName("fields")] public List<MobileSurveyField> Fields { get; set; } = new();
}

public sealed class MobileSurveyGroup
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("nameEn")] public string? NameEn { get; set; }
    [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }

    public string DisplayName => UiLocalization.Data(Name, NameEn);
}

public sealed class MobileSurveyField
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("groupId")] public int? GroupId { get; set; }
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("labelEn")] public string? LabelEn { get; set; }
    [JsonPropertyName("controlType")] public string ControlType { get; set; } = "";
    [JsonPropertyName("controlLabel")] public string ControlLabel { get; set; } = "";
    [JsonPropertyName("options")] public List<string> Options { get; set; } = new();
    [JsonPropertyName("scale")] public int Scale { get; set; }
    [JsonPropertyName("required")] public bool Required { get; set; }
    [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }

    public string DisplayLabel => UiLocalization.Data(Label, LabelEn);
    public string DisplayControlLabel => UiLocalization.Data(ControlLabel);
}

public sealed class MobileFeedbackItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("priority")] public string Priority { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("adminReply")] public string AdminReply { get; set; } = "";
    [JsonPropertyName("repliedBy")] public string RepliedBy { get; set; } = "";
    [JsonPropertyName("repliedAt")] public DateTime? RepliedAt { get; set; }
    [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }

    public string DisplayType => UiLocalization.Data(Type);
    public string DisplayPriority => UiLocalization.Data(Priority);

    public string StatusText =>
        Status.ToLowerInvariant() switch
        {
            "open" => UiLocalization.T("مفتوح"),
            "pending" => UiLocalization.T("قيد المتابعة"),
            "resolved" => UiLocalization.T("تمت المعالجة"),
            "closed" => UiLocalization.T("مغلق"),
            _ => UiLocalization.Data(Status)
        };
}

public sealed class MobileDisciplinaryItem
{
    [JsonPropertyName("referenceNo")] public string ReferenceNo { get; set; } = "";
    [JsonPropertyName("eventDate")] public string? EventDate { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("actionStatus")] public string ActionStatus { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("finalPenaltyAction")] public string FinalPenaltyAction { get; set; } = "";
    [JsonPropertyName("deductionAmount")] public decimal DeductionAmount { get; set; }
    [JsonPropertyName("replyStatus")] public string ReplyStatus { get; set; } = "";
    [JsonPropertyName("employeeReply")] public string EmployeeReply { get; set; } = "";

    public string DisplayCategory => UiLocalization.Data(Category);
    public string DisplayTitle => UiLocalization.Data(Title);
    public string DisplayActionStatus => UiLocalization.Data(ActionStatus);
    public string DisplayStatus => UiLocalization.Data(Status);
    public string DisplayFinalPenaltyAction => UiLocalization.Data(FinalPenaltyAction);
    public string DisplayReplyStatus => UiLocalization.Data(ReplyStatus);
}

public sealed class MobileBiometricKey
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("deviceLabel")] public string? DeviceLabel { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("statusText")] public string StatusText { get; set; } = "";
    [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("approvedAt")] public DateTime? ApprovedAt { get; set; }
    [JsonPropertyName("lastUsedAt")] public DateTime? LastUsedAt { get; set; }

    public string DisplayStatusText => UiLocalization.Data(StatusText);
}

public sealed class WebAuthnRegistrationOptions
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("options")] public JsonElement Options { get; set; }
}

public sealed class WebAuthnProofResponse
{
    [JsonPropertyName("token")] public string Token { get; set; } = "";
}

public sealed class DataChangeOption
{
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";

    public string DisplayLabel => UiLocalization.Data(Label, Value);
}

public sealed class DataChangeField
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("currentValue")] public string? CurrentValue { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "text";
    [JsonPropertyName("options")] public List<DataChangeOption> Options { get; set; } = new();

    public string DisplayLabel => Key switch
    {
        "Phone" => UiLocalization.T("رقم الهاتف"),
        "Email" => UiLocalization.T("البريد الإلكتروني"),
        "NationalId" => UiLocalization.T("رقم الهوية"),
        "BirthDate" => UiLocalization.T("تاريخ الميلاد"),
        "MaritalStatus" => UiLocalization.T("الحالة الاجتماعية"),
        "Nationality" => UiLocalization.T("الجنسية"),
        "Country" => UiLocalization.T("بلد الإقامة"),
        "Religion" => UiLocalization.T("الديانة"),
        "PhotoPath" => UiLocalization.T("الصورة الشخصية"),
        _ => UiLocalization.Data(Label)
    };
}

public sealed class DataChangeSubmissionField
{
    public string Key { get; set; } = "";
    public string? NewValue { get; set; }
}

public sealed class FinancialCatalogResponse
{
    [JsonPropertyName("eligible")] public bool Eligible { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("items")] public List<FinancialCatalogItem> Items { get; set; } = new();
}

public sealed class FinancialCatalogItem
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("hint")] public string Hint { get; set; } = "";

    public string DisplayLabel => UiLocalization.Data(Label);
    public string DisplayHint => UiLocalization.Data(Hint);
}

public sealed class ShiftCatalogResponse
{
    [JsonPropertyName("eligible")] public bool Eligible { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("items")] public List<ShiftCatalogItem> Items { get; set; } = new();
}

public sealed class ShiftCatalogItem
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    public string DisplayName => UiLocalization.Data(Name);
}

public sealed class RequestCatalogResponse
{
    [JsonPropertyName("eligible")] public bool Eligible { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("canSubmitMissingPunch")] public bool CanSubmitMissingPunch { get; set; }
    [JsonPropertyName("items")] public List<MobileRequestType> Items { get; set; } = new();
}

public sealed class MobileRequestType
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("nameEn")] public string? NameEn { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("categoryEn")] public string? CategoryEn { get; set; }
    [JsonPropertyName("needsTime")] public bool NeedsTime { get; set; }
    [JsonPropertyName("attachmentRequired")] public bool AttachmentRequired { get; set; }
    [JsonPropertyName("attachmentLabel")] public string? AttachmentLabel { get; set; }
    [JsonPropertyName("reasonRequired")] public bool ReasonRequired { get; set; }
    [JsonPropertyName("allowedDays")] public int? AllowedDays { get; set; }
    [JsonPropertyName("effectCode")] public string? EffectCode { get; set; }
    [JsonPropertyName("hasBalance")] public bool HasBalance { get; set; }

    public string DisplayTypeName => UiLocalization.Data(Name, NameEn);
    public string DisplayCategory => UiLocalization.Data(Category, CategoryEn);

    public string DisplayName =>
        string.IsNullOrWhiteSpace(DisplayCategory)
            ? DisplayTypeName
            : $"{DisplayTypeName} · {DisplayCategory}";
}

public sealed class MobileRequest
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("requestTypeId")] public int? RequestTypeId { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("typeEn")] public string? TypeEn { get; set; }
    [JsonPropertyName("fromDate")] public string? FromDate { get; set; }
    [JsonPropertyName("toDate")] public string? ToDate { get; set; }
    [JsonPropertyName("startTime")] public string? StartTime { get; set; }
    [JsonPropertyName("endTime")] public string? EndTime { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("hasAttachment")] public bool HasAttachment { get; set; }
    [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }

    public string DisplayType => UiLocalization.Data(Type, TypeEn);

    public string DateRange
    {
        get
        {
            var dates =
                string.IsNullOrWhiteSpace(ToDate) ||
                string.Equals(FromDate, ToDate, StringComparison.Ordinal)
                    ? (FromDate ?? "—")
                    : $"{FromDate ?? "—"} → {ToDate}";

            return string.IsNullOrWhiteSpace(StartTime) || string.IsNullOrWhiteSpace(EndTime)
                ? dates
                : $"{dates} · {StartTime} → {EndTime}";
        }
    }

    public string AttachmentText =>
        HasAttachment ? UiLocalization.T("📎 مرفق") : string.Empty;

    public string StatusLabel =>
        Status.ToLowerInvariant() switch
        {
            "pending" => UiLocalization.T("قيد المراجعة"),
            "approved" => UiLocalization.T("مقبول"),
            "rejected" => UiLocalization.T("مرفوض"),
            "returned" => UiLocalization.T("معاد للتعديل"),
            "draft" => UiLocalization.T("مسودة"),
            "cancelled" => UiLocalization.T("ملغي"),
            "canceled" => UiLocalization.T("ملغي"),
            _ => UiLocalization.Data(Status)
        };

    public bool IsCancelable =>
        Status.Equals("Pending",StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("Returned",StringComparison.OrdinalIgnoreCase) ||
        Status.Equals("Draft",StringComparison.OrdinalIgnoreCase);
}

public sealed class DayPunchItem
{
    [JsonPropertyName("at")] public string At { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("typeText")] public string TypeText { get; set; } = "";

    public string DisplayTypeText => UiLocalization.Data(TypeText);
}

public sealed class MissingPunchItem
{
    [JsonPropertyName("refNo")] public string RefNo { get; set; } = "";
    [JsonPropertyName("punchAt")] public string PunchAt { get; set; } = "";
    [JsonPropertyName("punchType")] public string PunchType { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";

    public string DisplayPunchType => UiLocalization.Data(PunchType);
    public string DisplayStatus => UiLocalization.Data(Status);
}

public sealed class ApiMessage
{
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("requestId")] public int? RequestId { get; set; }
    [JsonPropertyName("derivedType")] public string? DerivedType { get; set; }
}

public sealed class ApiError
{
    [JsonPropertyName("message")] public string? Message { get; set; }
}