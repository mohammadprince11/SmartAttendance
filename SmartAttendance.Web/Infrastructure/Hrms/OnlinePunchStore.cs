using System.Globalization;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// البصمات عبر الإنترنت (نمط كيان — قسم 36.ج بدراسة الحضور): بصم ذاتي مباشر من
/// المتصفح/الجوال. تُخزَّن كسجلات AttendanceRecords بمصدر «موبايل» (Source=2) فتدخل
/// اشتقاق اليومية كأي بصمة عند «تحديث الحضور». دخول ⟶ CheckIn (CheckOut فارغ)،
/// خروج ⟶ CheckOut (مع CheckIn=نفس الوقت لأن العمود غير قابل للتفريغ)، فنوع البصمة
/// يُستنتج من امتلاء CheckOut. صفحة الإدارة تعرضها وتحذف المختار منها.
/// </summary>
public static class OnlinePunchStore
{
    public const int MobileSource = 2;

    public sealed class OnlinePunch
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public DateTime PunchAt { get; set; }
        public string PunchType { get; set; } = "In";           // In | Out (مستنتج)
        public int? PunchSemanticId { get; set; }
        public string SemanticName { get; set; } = string.Empty;

        public string PunchTypeText => PunchType == "Out" ? "ختم خروج" : "ختم دخول";
    }

    /// <summary>نافذة منع التكرار (ثوانٍ): بصمتان أونلاين خلالها = ضغطة مكرّرة تُرفَض.</summary>
    public const int DebounceSeconds = 60;

    /// <summary>
    /// مفتاح إعداد «أقل عدد ساعات بين الحضور والانصراف» بجدول الإعدادات العام
    /// (<c>NexoraHrSettings</c>). يعدّله أدمن النظام من صفحة «إعدادات الحضور».
    /// </summary>
    public const string MinCheckoutHoursKey = "Attendance.OnlinePunch.MinCheckoutHours";

    /// <summary>القيمة الافتراضية إن لم يُضبط الإعداد بعد (٤ ساعات).</summary>
    public const double DefaultMinCheckoutHours = 4;

    /// <summary>
    /// مفتاح إعداد «إنفاذ النطاق الجغرافي للبصم الأونلاين»: حين يُفعَّل، الموظف المسنَد
    /// لمواقع جغرافية (<c>EmployeeGeoLocations</c>) لا يُبصّم إلا داخل نصف قطر أحدها.
    /// الافتراضي معطّل (سلوك ما قبل الميزة)، وموظف بلا مواقع مسنَدة لا تنطبق عليه القاعدة.
    /// </summary>
    public const string EnforceGeofenceKey = "Attendance.OnlinePunch.EnforceGeofence";

    /// <summary>هل إنفاذ النطاق الجغرافي مفعّل؟ (داينمك من إعدادات الحضور، الافتراضي لا)</summary>
    public static async Task<bool> GetEnforceGeofenceAsync(ApplicationDbContext db) =>
        await HrSettingsStore.GetAsync(db, EnforceGeofenceKey, "0") == "1";

    /// <summary>تفعيل/تعطيل إنفاذ النطاق الجغرافي.</summary>
    public static Task SetEnforceGeofenceAsync(ApplicationDbContext db, bool enabled) =>
        HrSettingsStore.SetAsync(db, EnforceGeofenceKey, enabled ? "1" : "0");

    /// <summary>مسافة هافرساين بالأمتار بين إحداثيّين. نقية قابلة للاختبار.</summary>
    public static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadius = 6371000d;
        var dLat = (lat2 - lat1) * Math.PI / 180d;
        var dLng = (lng2 - lng1) * Math.PI / 180d;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180d) * Math.Cos(lat2 * Math.PI / 180d)
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return earthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>هل الإحداثيان داخل نصف قطر أيٍّ من النطاقات؟ نقية قابلة للاختبار.</summary>
    public static bool IsInsideAny(
        IEnumerable<(double Latitude, double Longitude, int RadiusMeters)> zones,
        double latitude, double longitude) =>
        zones.Any(z => DistanceMeters(z.Latitude, z.Longitude, latitude, longitude)
                       <= Math.Max(10, z.RadiusMeters));

    /// <summary>حالة محاولة تسجيل البصمة عبر الإنترنت.</summary>
    public enum PunchStatus
    {
        /// <summary>سُجّلت البصمة.</summary>
        Recorded,
        /// <summary>رُفضت لأنها مكرّرة خلال نافذة <see cref="DebounceSeconds"/>.</summary>
        Duplicate,
        /// <summary>رُفضت لأن الانصراف قبل مرور المدة الدنيا من آخر حضور مفتوح.</summary>
        TooSoonForCheckout,
        /// <summary>رُفضت لأن الموظف خارج نطاقاته الجغرافية المسنَدة (أو بلا إحداثيات والإنفاذ مفعّل).</summary>
        OutsideGeofence
    }

    /// <summary>نتيجة محاولة تسجيل بصمة عبر الإنترنت (تحمل تفاصيل الرفض للرسائل).</summary>
    public sealed record PunchResult(
        PunchStatus Status,
        int RecordId,
        double MinCheckoutHours = 0,
        double HoursSinceCheckIn = 0)
    {
        /// <summary>الساعات المتبقّية قبل السماح بالانصراف (صفر إن انتهت المدة).</summary>
        public double HoursRemaining => Math.Max(0, MinCheckoutHours - HoursSinceCheckIn);
    }

    /// <summary>يقرأ «أقل عدد ساعات» المسموح بينها بين الحضور والانصراف (داينمك من الإعدادات).</summary>
    public static async Task<double> GetMinCheckoutHoursAsync(ApplicationDbContext db)
    {
        var raw = await HrSettingsStore.GetAsync(
            db, MinCheckoutHoursKey, DefaultMinCheckoutHours.ToString(CultureInfo.InvariantCulture));
        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : DefaultMinCheckoutHours;
    }

    /// <summary>يضبط «أقل عدد ساعات» بين الحضور والانصراف (يُثبَّت ضمن [0..24]).</summary>
    public static Task SetMinCheckoutHoursAsync(ApplicationDbContext db, double hours)
        => HrSettingsStore.SetAsync(
            db, MinCheckoutHoursKey, Math.Clamp(hours, 0, 24).ToString(CultureInfo.InvariantCulture));

    /// <summary>يصوغ مدّة بالساعات إلى نص عربي («س ساعة ود دقيقة»).</summary>
    public static string FormatDuration(double hours)
    {
        var totalMinutes = Math.Max(1, (int)Math.Round(hours * 60));
        var h = totalMinutes / 60;
        var m = totalMinutes % 60;
        if (h > 0 && m > 0) return $"{h} ساعة و{m} دقيقة";
        return h > 0 ? $"{h} ساعة" : $"{m} دقيقة";
    }

    /// <summary>
    /// تسجيل بصمة عبر الإنترنت (بصم ذاتي). يرجع <see cref="PunchResult"/> يوضّح الحالة:
    /// سُجّلت، أو رُفضت لأنها مكرّرة خلال <see cref="DebounceSeconds"/> ثانية، أو رُفضت لأن
    /// الانصراف قبل مرور المدة الدنيا (قاعدة جازمة تمنع بصم خروج خاطئ عقب الحضور مباشرة).
    /// </summary>
    public static async Task<PunchResult> RecordAsync(
        ApplicationDbContext db, int employeeId, string punchType, DateTime punchAt, int? semanticId,
        double? latitude = null, double? longitude = null)
    {
        await HrmsDatabase.EnsureCreatedAsync(db);

        // إنفاذ النطاق الجغرافي (محروس بالإعداد، الافتراضي معطّل): الموظف المسنَد لمواقع
        // نشطة يُرفَض بصمه خارج نصف قطر أحدها — وغياب الإحداثيات = رفض أيضاً (وإلا
        // يُلتَفّ على القاعدة برفض إذن الموقع). موظف بلا إسنادات لا تنطبق عليه القاعدة.
        if (await GetEnforceGeofenceAsync(db))
        {
            await GeoLocationStore.EnsureAsync(db);
            var zones = await HrmsDatabase.QueryAsync(
                db,
                """
SELECT g.Latitude, g.Longitude, g.RadiusMeters
FROM EmployeeGeoLocations eg
INNER JOIN GeoLocations g ON g.Id = eg.GeoLocationId AND g.IsActive = 1
WHERE eg.EmployeeId = @Emp;
""",
                command => HrmsDatabase.AddParameter(command, "@Emp", employeeId),
                reader => (
                    Latitude: (double)(reader["Latitude"] is decimal la ? la : 0),
                    Longitude: (double)(reader["Longitude"] is decimal lo ? lo : 0),
                    RadiusMeters: HrmsDatabase.GetInt(reader, "RadiusMeters")));

            if (zones.Count > 0
                && (latitude is not { } lat || longitude is not { } lng || !IsInsideAny(zones, lat, lng)))
            {
                return new PunchResult(PunchStatus.OutsideGeofence, 0);
            }
        }

        // قاعدة صارمة: ارفض بصمة أونلاين مكرّرة خلال نافذة قصيرة (نقرة مزدوجة/شبكة بطيئة).
        var recent = await HrmsDatabase.ScalarAsync<int>(
            db,
            $"""
SELECT COUNT(1) FROM AttendanceRecords
WHERE EmployeeId = @Emp AND Source = @Src AND ISNULL(IsDeleted, 0) = 0
      AND ABS(DATEDIFF(second, CheckIn, @At)) < {DebounceSeconds};
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                HrmsDatabase.AddParameter(command, "@Src", MobileSource);
                HrmsDatabase.AddParameter(command, "@At", punchAt);
            });
        if (recent > 0) return new PunchResult(PunchStatus.Duplicate, 0);

        var isOut = punchType == "Out";

        // قاعدة جازمة: لا انصراف إلا بعد مرور المدة الدنيا من آخر حضور مفتوح (CheckOut فارغ).
        // المدة داينمك من إعدادات الحضور؛ صفر = القاعدة معطّلة.
        if (isOut)
        {
            var minHours = await GetMinCheckoutHoursAsync(db);
            if (minHours > 0)
            {
                // ثوانِ منذ آخر حضور أونلاين مفتوح (CheckOut فارغ)؛ NULL ⟵ لا حضور مفتوح ⟵ يُسمح.
                var secondsSinceCheckIn = await HrmsDatabase.ScalarAsync<int>(
                    db,
                    """
SELECT TOP 1 DATEDIFF(second, CheckIn, @At) FROM AttendanceRecords
WHERE EmployeeId = @Emp AND Source = @Src AND ISNULL(IsDeleted, 0) = 0 AND CheckOut IS NULL
ORDER BY CheckIn DESC;
""",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                        HrmsDatabase.AddParameter(command, "@Src", MobileSource);
                        HrmsDatabase.AddParameter(command, "@At", punchAt);
                    });

                if (secondsSinceCheckIn is { } seconds)
                {
                    var elapsedHours = Math.Max(0, seconds) / 3600d;
                    if (elapsedHours < minHours)
                        return new PunchResult(PunchStatus.TooSoonForCheckout, 0, minHours, elapsedHours);
                }
            }
        }

        var attendanceSemanticId = await PunchSemanticStore.AttendanceSemanticIdAsync(db);
        // دلالة الحضور تُخزَّن NULL (يقرأها المحلل)؛ غيرها كبصمة أخرى بمعرّف الدلالة.
        int? storedSemantic = (semanticId == null || semanticId == attendanceSemanticId) ? null : semanticId;

        var newId = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO AttendanceRecords
    (EmployeeId, AttendanceDate, CheckIn, CheckOut, Source, Status, DeviceId, Notes, CreatedAt, IsDeleted, PunchSemanticId)
VALUES
    (@Emp, @Date, @CheckIn, @CheckOut, @Src, 1, NULL, N'بصمة عبر الإنترنت', SYSUTCDATETIME(), 0, @Semantic);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                HrmsDatabase.AddParameter(command, "@Date", DateOnly.FromDateTime(punchAt));
                HrmsDatabase.AddParameter(command, "@CheckIn", punchAt);
                HrmsDatabase.AddParameter(command, "@CheckOut", isOut ? punchAt : (object)DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Src", MobileSource);
                HrmsDatabase.AddParameter(command, "@Semantic", (object?)storedSemantic ?? DBNull.Value);
            });

        return new PunchResult(PunchStatus.Recorded, newId);
    }

    public sealed class Filter
    {
        public int? EmployeeId { get; set; }
        public string? Search { get; set; }
        public string? PunchType { get; set; }
        public string? Department { get; set; }
        public string? Branch { get; set; }
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public int Top { get; set; } = 500;
    }

    public static async Task<List<OnlinePunch>> ListAsync(ApplicationDbContext db, Filter filter)
    {
        await HrmsDatabase.EnsureCreatedAsync(db);

        var rows = await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT TOP {Math.Clamp(filter.Top, 1, 2000)}
    ar.Id, ar.EmployeeId, ISNULL(e.EmployeeNo, N'') AS EmployeeNo, ISNULL(e.FullName, N'') AS FullName,
    ISNULL(d.Name, N'') AS DepartmentName, ISNULL(b.Name, N'') AS BranchName,
    ar.CheckIn, ar.CheckOut, ar.PunchSemanticId, ISNULL(ps.Name, N'حضور') AS SemanticName
FROM AttendanceRecords ar
INNER JOIN Employees e ON e.Id = ar.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
LEFT JOIN PunchSemantics ps ON ps.Id = ar.PunchSemanticId
WHERE ISNULL(ar.IsDeleted, 0) = 0 AND ar.Source = @Src
ORDER BY ar.CheckIn DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@Src", MobileSource),
            reader =>
            {
                var checkOut = HrmsDatabase.GetDateTime(reader, "CheckOut");
                return new OnlinePunch
                {
                    Id = HrmsDatabase.GetInt(reader, "Id"),
                    EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                    EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                    EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                    Department = HrmsDatabase.GetString(reader, "DepartmentName"),
                    Branch = HrmsDatabase.GetString(reader, "BranchName"),
                    PunchAt = checkOut ?? HrmsDatabase.GetDateTime(reader, "CheckIn") ?? default,
                    PunchType = checkOut.HasValue ? "Out" : "In",
                    PunchSemanticId = HrmsDatabase.GetNullableInt(reader, "PunchSemanticId"),
                    SemanticName = HrmsDatabase.GetString(reader, "SemanticName")
                };
            });

        if (filter.EmployeeId is > 0) rows = rows.Where(r => r.EmployeeId == filter.EmployeeId).ToList();
        if (!string.IsNullOrWhiteSpace(filter.PunchType)) rows = rows.Where(r => r.PunchType == filter.PunchType).ToList();
        if (!string.IsNullOrWhiteSpace(filter.Department)) rows = rows.Where(r => r.Department == filter.Department).ToList();
        if (!string.IsNullOrWhiteSpace(filter.Branch)) rows = rows.Where(r => r.Branch == filter.Branch).ToList();
        if (filter.From is { } f) rows = rows.Where(r => DateOnly.FromDateTime(r.PunchAt) >= f).ToList();
        if (filter.To is { } t) rows = rows.Where(r => DateOnly.FromDateTime(r.PunchAt) <= t).ToList();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var v = filter.Search.Trim();
            rows = rows.Where(r =>
                r.EmployeeNo.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                r.EmployeeName.Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return rows;
    }

    /// <summary>حذف بصمات عبر الإنترنت مختارة (نمط كيان «حذف العناصر المختارة»).</summary>
    public static async Task<int> DeleteManyAsync(ApplicationDbContext db, IReadOnlyCollection<int> ids)
    {
        await HrmsDatabase.EnsureCreatedAsync(db);
        if (ids.Count == 0) return 0;
        var total = 0;
        foreach (var chunk in ids.Chunk(200))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            total += await HrmsDatabase.ScalarAsync<int>(
                db,
                $"UPDATE AttendanceRecords SET IsDeleted=1, UpdatedAt=SYSUTCDATETIME() WHERE Source=@Src AND Id IN ({inList}); SELECT @@ROWCOUNT;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Src", MobileSource);
                    for (var i = 0; i < chunk.Length; i++) HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                });
        }
        return total;
    }
}
