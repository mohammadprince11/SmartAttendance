using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages;

/// <summary>
/// صفحة التحقق العامّة من أصالة وثيقة صادرة (منشئ الوثائق — م٤).
///
/// ⭐ **عامّة عمداً**: يفحصها طرفٌ ثالث (بنك · سفارة) لا حساب له بالنظام، وهو
/// جوهر الميزة — بدونها تبقى الوثيقة ورقةً تُزوَّر بـWord.
///
/// المنطق كلّه بـ<see cref="DocumentVerificationPolicy"/>، وهذه الصفحة إدخالٌ وعرض.
/// </summary>
[AllowAnonymous]
public class VerifyModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public VerifyModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    [BindProperty] public string? Factor { get; set; }

    public bool Submitted { get; private set; }
    public bool Success { get; private set; }
    public string? Message { get; private set; }

    /// <summary>ما يُعرض عند النجاح — **أدنى ما يكفي** لا نصّ الوثيقة ولا راتبها.</summary>
    public string? DocumentType { get; private set; }
    public string? EmployeeName { get; private set; }
    public DateOnly? IssuedOn { get; private set; }
    public DateOnly? ValidUntil { get; private set; }
    public string? RequiredFactorLabel { get; private set; }

    public void OnGet()
    {
        // الرمز قد يأتي بالرابط (من رمز QR لاحقاً)، فيُملأ الحقل ويبقى العامل مطلوباً.
    }

    public async Task OnPostAsync()
    {
        Submitted = true;

        var document = await DocumentTemplateStore.FindByVerifyTokenAsync(_db, Token);
        var factorOk = false;

        if (document is not null)
        {
            RequiredFactorLabel = DocumentVerificationPolicy.FactorLabel(document.VerifyFactor);

            factorOk = document.VerifyFactor switch
            {
                DocumentVerificationPolicy.FactorPin =>
                    DocumentVerificationPolicy.PinMatches(
                        Factor, document.VerifyToken ?? string.Empty,
                        await DocumentTemplateStore.LoadPinHashAsync(_db, document.Id)),

                DocumentVerificationPolicy.FactorNationalId =>
                    DocumentVerificationPolicy.NationalIdMatches(Factor, await NationalIdOfAsync(document.EmployeeId)),

                // بلا عامل: الرمز وحده يكفي — خيارٌ متاح لكن غير موصى به،
                // ولذلك تُحذّر منه شاشة القالب.
                _ => true
            };
        }

        var result = DocumentVerificationPolicy.Evaluate(
            document is not null, factorOk, document?.RevokedAt, document?.VerifyValidUntil,
            DateOnly.FromDateTime(DateTime.Today));

        Success = DocumentVerificationPolicy.IsSuccess(result);
        Message = DocumentVerificationPolicy.Message(result);

        // ⚠️ البيانات تُكشف **عند النجاح فقط** — وحتى حينها بأدنى قدر.
        if (Success && document is not null)
        {
            DocumentType = document.TemplateName;
            EmployeeName = document.EmployeeName;
            IssuedOn = document.IssuedOn;
            ValidUntil = document.VerifyValidUntil;
        }
    }

    private async Task<string?> NationalIdOfAsync(int employeeId) =>
        await HrmsDatabase.ScalarAsync<string>(
            _db,
            "SELECT TOP 1 NationalId FROM Employees WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", employeeId));
}
