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

    public async Task LoginAsync(string username, string password)
    {
        using var response = await _http.PostAsJsonAsync(
            "api/v1/auth/login",
            new { username = username.Trim(), password },
            Json);

        if (!response.IsSuccessStatusCode)
            throw new MobileApiException(await ErrorAsync(response));

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
        if (string.IsNullOrWhiteSpace(result?.Token))
            throw new MobileApiException("استجابة تسجيل الدخول غير صالحة.");

        await SecureStorage.Default.SetAsync(TokenKey, result.Token);
    }

    public async Task LogoutAsync()
    {
        try
        {
            using var request = await AuthorizedAsync(HttpMethod.Post, "api/v1/auth/logout");
            using var response = await _http.SendAsync(request);
        }
        finally
        {
            SecureStorage.Default.Remove(TokenKey);
        }
    }

    public Task<EmployeeProfile> ProfileAsync() =>
        SendAsync<EmployeeProfile>(HttpMethod.Get, "api/v1/me");

    public Task<List<AttendanceDay>> AttendanceAsync() =>
        SendAsync<List<AttendanceDay>>(HttpMethod.Get, "api/v1/me/attendance?days=7");

    public Task<List<LeaveBalance>> LeaveBalancesAsync() =>
        SendAsync<List<LeaveBalance>>(HttpMethod.Get, "api/v1/me/leave-balance");

    public Task<ApiMessage> PunchAsync(string type, double lat, double lng) =>
        SendAsync<ApiMessage>(
            HttpMethod.Post,
            "api/v1/me/online-punch",
            new { punchType = type == "Out" ? "Out" : "In", latitude = lat, longitude = lng });

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var request = await AuthorizedAsync(method, path, body);
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

    private async Task<HttpRequestMessage> AuthorizedAsync(
        HttpMethod method, string path, object? body = null)
    {
        string? token;
        try { token = await SecureStorage.Default.GetAsync(TokenKey); }
        catch { token = null; }

        if (string.IsNullOrWhiteSpace(token))
            throw new MobileSessionExpiredException();

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        return request;
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var e = await response.Content.ReadFromJsonAsync<ApiError>(Json);
            if (!string.IsNullOrWhiteSpace(e?.Message)) return e.Message.Trim();
        }
        catch { }

        return response.StatusCode == HttpStatusCode.Unauthorized
            ? "بيانات الدخول غير صحيحة أو انتهت الجلسة."
            : "تعذر الاتصال بخدمة ZYNORA حالياً.";
    }
}

public class MobileApiException(string message) : Exception(message);
public sealed class MobileSessionExpiredException()
    : MobileApiException("انتهت جلسة الدخول. سجل الدخول مرة أخرى.");

public sealed record LoginResponse(
    [property: JsonPropertyName("token")] string Token);

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
}

public sealed class LeaveBalance
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("unit")] public string Unit { get; set; } = "";
    [JsonPropertyName("entitled")] public decimal Entitled { get; set; }
    [JsonPropertyName("used")] public decimal Used { get; set; }
    [JsonPropertyName("remaining")] public decimal Remaining { get; set; }
}

public sealed class ApiMessage
{
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

public sealed class ApiError
{
    [JsonPropertyName("message")] public string? Message { get; set; }
}