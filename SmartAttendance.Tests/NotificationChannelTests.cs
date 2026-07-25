using System.Net.Mail;
using SmartAttendance.Web.Infrastructure.Notifications;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>اختبارات نقية لقناة الإشعارات الخارجية (SMTP) — بلا شبكة ولا قاعدة بيانات.</summary>
public class NotificationChannelTests
{
    // --- أولوية عنوان المستلم: بريد العمل ثم الشخصي ثم فارغ ---

    [Fact]
    public void ResolveEmail_PrefersWorkEmail()
    {
        Assert.Equal("work@corp.com",
            NotificationDispatcher.ResolveEmail("work@corp.com", "me@home.com"));
    }

    [Fact]
    public void ResolveEmail_FallsBackToPersonal_WhenWorkMissing()
    {
        Assert.Equal("me@home.com", NotificationDispatcher.ResolveEmail("", "me@home.com"));
        Assert.Equal("me@home.com", NotificationDispatcher.ResolveEmail(null, "me@home.com"));
        Assert.Equal("me@home.com", NotificationDispatcher.ResolveEmail("   ", "me@home.com"));
    }

    [Fact]
    public void ResolveEmail_ReturnsEmpty_WhenBothMissing()
    {
        Assert.Equal(string.Empty, NotificationDispatcher.ResolveEmail(null, null));
        Assert.Equal(string.Empty, NotificationDispatcher.ResolveEmail("", "  "));
    }

    [Fact]
    public void ResolveEmail_TrimsWhitespace()
    {
        Assert.Equal("a@b.com", NotificationDispatcher.ResolveEmail("  a@b.com  ", null));
    }

    // --- صلاحية إعداد SMTP للإرسال الفعلي ---

    [Fact]
    public void Options_NotUsable_WhenDisabled()
    {
        var o = new SmtpOptions { Enabled = false, Host = "smtp.x.com", FromAddress = "a@x.com" };
        Assert.False(o.IsUsable);
    }

    [Fact]
    public void Options_NotUsable_WhenHostOrFromMissing()
    {
        Assert.False(new SmtpOptions { Enabled = true, Host = "", FromAddress = "a@x.com" }.IsUsable);
        Assert.False(new SmtpOptions { Enabled = true, Host = "smtp.x.com", FromAddress = "" }.IsUsable);
    }

    [Fact]
    public void Options_Usable_WhenEnabledWithHostAndFrom()
    {
        var o = new SmtpOptions { Enabled = true, Host = "smtp.x.com", FromAddress = "a@x.com" };
        Assert.True(o.IsUsable);
    }

    // --- مرسِل No-Op لا يُرسِل شيئاً ويعيد فشلاً واضحاً ---

    [Fact]
    public async Task NoOpSender_IsDisabled_AndReturnsFailure()
    {
        var sender = new NoOpEmailSender();
        Assert.False(sender.IsEnabled);
        var result = await sender.SendAsync(new EmailMessage("a@b.com", "s", "body"));
        Assert.False(result.Sent);
        Assert.NotNull(result.Error);
    }

    // --- بناء رسالة البريد من الإعداد (منطق نقي) ---

    [Fact]
    public void BuildMail_SetsFromToSubjectAndUtfBody()
    {
        var options = new SmtpOptions { FromAddress = "hr@corp.com", FromName = "الموارد البشرية" };
        var msg = new EmailMessage("emp@corp.com", "ملخص الحضور", "لديك يوم غياب");

        using var mail = SmtpEmailSender.BuildMail(options, msg);

        Assert.Equal("hr@corp.com", mail.From!.Address);
        Assert.Equal("الموارد البشرية", mail.From.DisplayName);
        Assert.Single(mail.To);
        Assert.Equal("emp@corp.com", mail.To[0].Address);
        Assert.Equal("ملخص الحضور", mail.Subject);
        Assert.Equal("لديك يوم غياب", mail.Body);
        Assert.False(mail.IsBodyHtml);
        Assert.Equal(System.Text.Encoding.UTF8, mail.BodyEncoding);
    }

    // --- نتائج EmailResult ---

    [Fact]
    public void EmailResult_OkAndFail_CarryExpectedState()
    {
        Assert.True(EmailResult.Ok.Sent);
        Assert.Null(EmailResult.Ok.Error);

        var fail = EmailResult.Fail("boom");
        Assert.False(fail.Sent);
        Assert.Equal("boom", fail.Error);
    }

    // --- قناة Web-Push (VAPID) ---

    [Fact]
    public void Vapid_NotUsable_WhenAnyKeyMissing()
    {
        Assert.False(new VapidOptions { Subject = "mailto:hr@x.com", PublicKey = "pub", PrivateKey = "" }.IsUsable);
        Assert.False(new VapidOptions { Subject = "", PublicKey = "pub", PrivateKey = "priv" }.IsUsable);
        Assert.False(new VapidOptions { Subject = "mailto:hr@x.com", PublicKey = "", PrivateKey = "priv" }.IsUsable);
    }

    [Fact]
    public void Vapid_Usable_WhenAllPresent()
    {
        Assert.True(new VapidOptions { Subject = "mailto:hr@x.com", PublicKey = "pub", PrivateKey = "priv" }.IsUsable);
    }

    [Fact]
    public async Task NoOpWebPush_IsDisabled_AndDeliversNothing()
    {
        var sender = new NoOpWebPushSender();
        Assert.False(sender.IsEnabled);
        Assert.Equal(0, await sender.SendToEmployeeAsync(null!, 5, new PushPayload("t", "b")));
    }

    [Fact]
    public void PushPayload_Defaults_UrlToNull()
    {
        var p = new PushPayload("عنوان", "نص");
        Assert.Null(p.Url);
        Assert.Equal("عنوان", p.Title);
    }
}
