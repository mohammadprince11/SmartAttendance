using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات التحقق من أصالة الوثائق.
///
/// **الأهمّ أمنياً** <see cref="NotFoundAndFactorMismatch_ShareTheSameMessage"/>:
/// لو ميّزت الصفحة بين «رمز غير موجود» و«عامل خاطئ» لصارت أداةَ تخمينٍ تكشف أي
/// الرموز صادرة فعلاً — ومنها يُبنى هجوم عدّ للوثائق.
///
/// و<see cref="RevocationOutranksExpiry"/>: وثيقة سُحبت وانتهت يجب أن تُعلَن
/// **مسحوبة** — السحب واقعة إدارية والانتهاء مجرّد مرور زمن.
/// </summary>
public class DocumentVerificationPolicyTests
{
    private static readonly DateOnly Today = new(2026, 8, 1);

    // ── الرمز ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Token_AvoidsVisuallyAmbiguousCharacters()
    {
        // الرمز يُنسخ من ورقة بالعين؛ 0/O و1/I/L تُنتج «الرمز لا يعمل» وهو صحيح.
        for (var i = 0; i < 200; i++)
        {
            var token = DocumentVerificationPolicy.NewToken();
            Assert.DoesNotContain('0', token);
            Assert.DoesNotContain('O', token);
            Assert.DoesNotContain('1', token);
            Assert.DoesNotContain('I', token);
            Assert.DoesNotContain('L', token);
        }
    }

    [Fact]
    public void Token_IsGroupedForEasyCopying()
    {
        var token = DocumentVerificationPolicy.NewToken();

        Assert.Equal(3, token.Split('-').Length);
        Assert.All(token.Split('-'), part => Assert.Equal(4, part.Length));
    }

    [Fact]
    public void Tokens_AreNotRepeated()
    {
        var tokens = Enumerable.Range(0, 300).Select(_ => DocumentVerificationPolicy.NewToken()).ToHashSet();

        Assert.Equal(300, tokens.Count);
    }

    [Fact]
    public void NormalizeToken_IgnoresDashesSpacesAndCase()
    {
        Assert.Equal("ABCDEFGH", DocumentVerificationPolicy.NormalizeToken("abcd-efgh"));
        Assert.Equal("ABCDEFGH", DocumentVerificationPolicy.NormalizeToken(" ABCD EFGH "));
        Assert.Equal(string.Empty, DocumentVerificationPolicy.NormalizeToken(null));
    }

    // ── الـPIN ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Pin_IsSixDigits()
    {
        for (var i = 0; i < 100; i++)
        {
            var pin = DocumentVerificationPolicy.NewPin();
            Assert.Equal(6, pin.Length);
            Assert.All(pin, c => Assert.True(char.IsDigit(c)));
        }
    }

    [Fact]
    public void SamePinOnDifferentDocuments_HashesDifferently()
    {
        // الملح من الرمز — فلا يكشف جدول التجزئة أن وثيقتين تشتركان بنفس السرّ.
        var a = DocumentVerificationPolicy.HashPin("123456", "ABCD-EFGH-JKMN");
        var b = DocumentVerificationPolicy.HashPin("123456", "QRST-UVWX-YZ23");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void PinMatches_AcceptsCorrectPinRegardlessOfTokenFormatting()
    {
        var token = "ABCD-EFGH-JKMN";
        var hash = DocumentVerificationPolicy.HashPin("123456", token);

        Assert.True(DocumentVerificationPolicy.PinMatches("123456", "abcdefghjkmn", hash));
        Assert.True(DocumentVerificationPolicy.PinMatches(" 123456 ", token, hash));
        Assert.False(DocumentVerificationPolicy.PinMatches("123457", token, hash));
    }

    [Fact]
    public void PinMatches_RejectsEmptyInputAndMissingHash()
    {
        var hash = DocumentVerificationPolicy.HashPin("123456", "ABCD");

        Assert.False(DocumentVerificationPolicy.PinMatches(null, "ABCD", hash));
        Assert.False(DocumentVerificationPolicy.PinMatches("123456", "ABCD", null));
    }

    // ── الرقم الوطني ───────────────────────────────────────────────────────────

    [Fact]
    public void NationalId_ComparesNormalized()
    {
        Assert.True(DocumentVerificationPolicy.NationalIdMatches("199-012-345", "199012345"));
        Assert.True(DocumentVerificationPolicy.NationalIdMatches(" 199012345 ", "199012345"));
        Assert.False(DocumentVerificationPolicy.NationalIdMatches("199012346", "199012345"));
        Assert.False(DocumentVerificationPolicy.NationalIdMatches(null, "199012345"));
        Assert.False(DocumentVerificationPolicy.NationalIdMatches("199012345", null));
    }

    // ── الصلاحية والحكم ────────────────────────────────────────────────────────

    [Fact]
    public void ZeroValidDays_MeansNeverExpires()
    {
        Assert.Null(DocumentVerificationPolicy.ValidUntil(Today, 0));
        Assert.Null(DocumentVerificationPolicy.ValidUntil(Today, null));
        Assert.Equal(new DateOnly(2026, 8, 31), DocumentVerificationPolicy.ValidUntil(Today, 30));
    }

    [Fact]
    public void ValidDocument_PassesOnTheLastValidDay()
    {
        var until = new DateOnly(2026, 8, 1);

        Assert.Equal(
            DocumentVerificationPolicy.Result.Valid,
            DocumentVerificationPolicy.Evaluate(true, true, null, until, Today));

        Assert.Equal(
            DocumentVerificationPolicy.Result.Expired,
            DocumentVerificationPolicy.Evaluate(true, true, null, until, Today.AddDays(1)));
    }

    [Fact]
    public void NotFoundAndFactorMismatch_ShareTheSameMessage()
    {
        var notFound = DocumentVerificationPolicy.Evaluate(false, false, null, null, Today);
        var mismatch = DocumentVerificationPolicy.Evaluate(true, false, null, null, Today);

        Assert.NotEqual(notFound, mismatch); // التمييز داخليّ للسجلّ
        Assert.Equal(
            DocumentVerificationPolicy.Message(notFound),
            DocumentVerificationPolicy.Message(mismatch)); // ومتطابق أمام الفاحص
        Assert.False(DocumentVerificationPolicy.IsSuccess(notFound));
        Assert.False(DocumentVerificationPolicy.IsSuccess(mismatch));
    }

    [Fact]
    public void RevocationOutranksExpiry()
    {
        var result = DocumentVerificationPolicy.Evaluate(
            found: true, factorOk: true,
            revokedAt: new DateTime(2026, 7, 1),
            validUntil: new DateOnly(2026, 7, 15),
            asOf: Today);

        Assert.Equal(DocumentVerificationPolicy.Result.Revoked, result);
    }

    [Fact]
    public void WrongFactorOutranksRevocation_SoRevocationIsNotLeaked()
    {
        // عاملٌ خاطئ يجب ألا يكشف حتى أن الوثيقة مسحوبة.
        var result = DocumentVerificationPolicy.Evaluate(
            found: true, factorOk: false, revokedAt: new DateTime(2026, 7, 1), validUntil: null, asOf: Today);

        Assert.Equal(DocumentVerificationPolicy.Result.FactorMismatch, result);
    }

    [Fact]
    public void FactorLabels_AreDistinct()
    {
        Assert.Equal("الرقم الوطني", DocumentVerificationPolicy.FactorLabel(DocumentVerificationPolicy.FactorNationalId));
        Assert.Equal("رمز PIN", DocumentVerificationPolicy.FactorLabel(DocumentVerificationPolicy.FactorPin));
        Assert.Equal("بلا عامل تحقق", DocumentVerificationPolicy.FactorLabel(DocumentVerificationPolicy.FactorNone));
    }
}
