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

    public async Task LoginAsync(string username,string password)
    {
        using var response=await _http.PostAsJsonAsync(
            "api/v1/auth/login",
            new { username=username.Trim(), password },
            Json);

        if(!response.IsSuccessStatusCode)
            throw new MobileApiException(await ErrorAsync(response));

        var result=await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
        if(string.IsNullOrWhiteSpace(result?.Token))
            throw new MobileApiException("استجابة تسجيل الدخول غير صالحة.");

        await SecureStorage.Default.SetAsync(TokenKey,result.Token);
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

    public Task<EmployeeProfile> ProfileAsync() =>
        SendAsync<EmployeeProfile>(HttpMethod.Get,"api/v1/me");

    public Task<List<AttendanceDay>> AttendanceAsync() =>
        SendAsync<List<AttendanceDay>>(HttpMethod.Get,"api/v1/me/attendance?days=7");

    public Task<List<LeaveBalance>> LeaveBalancesAsync() =>
        SendAsync<List<LeaveBalance>>(HttpMethod.Get,"api/v1/me/leave-balance");

    public Task<List<MobileAnnouncement>> AnnouncementsAsync() =>
        SendAsync<List<MobileAnnouncement>>(
            HttpMethod.Get,
            "api/v1/me/announcements?take=5");

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
        if(body is not null) request.Content=JsonContent.Create(body,options:Json);
        return request;
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

public sealed record LoginResponse([property: JsonPropertyName("token")] string Token);

public sealed class EmployeeProfile
{
    [JsonPropertyName("employeeNo")] public string EmployeeNo { get; set; } = "";
    [JsonPropertyName("fullName")] public string FullName { get; set; } = "";
    [JsonPropertyName("position")] public string Position { get; set; } = "";
    [JsonPropertyName("department")] public string Department { get; set; } = "";
    [JsonPropertyName("branch")] public string Branch { get; set; } = "";
    [JsonPropertyName("isActive")] public bool IsActive { get; set; }
}

public sealed class AttendanceDay
{
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("checkIn")] public string? CheckIn { get; set; }
    [JsonPropertyName("checkOut")] public string? CheckOut { get; set; }

    public string AttendanceTimeline =>
        $"{CheckIn ?? "—"}  →  {CheckOut ?? "—"}";
}

public sealed class LeaveBalance
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("unit")] public string Unit { get; set; } = "";
    [JsonPropertyName("entitled")] public decimal Entitled { get; set; }
    [JsonPropertyName("used")] public decimal Used { get; set; }
    [JsonPropertyName("remaining")] public decimal Remaining { get; set; }

    public string BalanceSummary =>
        $"مستخدم {Used:0.##} من {Entitled:0.##}";
}

public sealed class MobileCompensation
{
    [JsonPropertyName("hasData")] public bool HasData { get; set; }
    [JsonPropertyName("basicSalary")] public decimal BasicSalary { get; set; }
    [JsonPropertyName("allowances")] public decimal Allowances { get; set; }
    [JsonPropertyName("deductions")] public decimal Deductions { get; set; }
    [JsonPropertyName("net")] public decimal Net { get; set; }
    [JsonPropertyName("paymentMethod")] public string PaymentMethod { get; set; } = "";
    [JsonPropertyName("bankName")] public string BankName { get; set; } = "";
    [JsonPropertyName("bankAccount")] public string BankAccount { get; set; } = "";
    [JsonPropertyName("currency")] public string Currency { get; set; } = "IQD";
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

    public string Meta =>
        string.Join(
            " · ",
            new[] { Category, PublishDate }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
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
}

public sealed class DataChangeField
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("currentValue")] public string? CurrentValue { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "text";
    [JsonPropertyName("options")] public List<DataChangeOption> Options { get; set; } = new();
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
    [JsonPropertyName("needsTime")] public bool NeedsTime { get; set; }
    [JsonPropertyName("attachmentRequired")] public bool AttachmentRequired { get; set; }
    [JsonPropertyName("attachmentLabel")] public string? AttachmentLabel { get; set; }
    [JsonPropertyName("reasonRequired")] public bool ReasonRequired { get; set; }
    [JsonPropertyName("allowedDays")] public int? AllowedDays { get; set; }
    [JsonPropertyName("effectCode")] public string? EffectCode { get; set; }
    [JsonPropertyName("hasBalance")] public bool HasBalance { get; set; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Category) ? Name : $"{Name} · {Category}";
}

public sealed class MobileRequest
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("requestTypeId")] public int? RequestTypeId { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("fromDate")] public string? FromDate { get; set; }
    [JsonPropertyName("toDate")] public string? ToDate { get; set; }
    [JsonPropertyName("startTime")] public string? StartTime { get; set; }
    [JsonPropertyName("endTime")] public string? EndTime { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("hasAttachment")] public bool HasAttachment { get; set; }
    [JsonPropertyName("createdAt")] public string? CreatedAt { get; set; }

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

    public string AttachmentText => HasAttachment ? "📎 مرفق" : string.Empty;

    public string StatusLabel =>
        Status.ToLowerInvariant() switch
        {
            "pending" => "قيد المراجعة",
            "approved" => "مقبول",
            "rejected" => "مرفوض",
            "returned" => "معاد للتعديل",
            "draft" => "مسودة",
            "cancelled" => "ملغي",
            "canceled" => "ملغي",
            _ => Status
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
}

public sealed class MissingPunchItem
{
    [JsonPropertyName("refNo")] public string RefNo { get; set; } = "";
    [JsonPropertyName("punchAt")] public string PunchAt { get; set; } = "";
    [JsonPropertyName("punchType")] public string PunchType { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
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