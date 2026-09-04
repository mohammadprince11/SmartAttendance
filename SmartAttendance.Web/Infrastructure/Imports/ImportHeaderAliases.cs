namespace SmartAttendance.Web.Infrastructure.Imports;

/// <summary>
/// مرادفات ترويسات قالب استيراد الموظفين — دالة نقية بلا قاعدة بيانات.
///
/// النظام عربيّ RTL، والمستخدم يترجم ترويسات القالب للعربية بطبيعة الحال قبل أن
/// يوزّعه على الأقسام لتعبئته. قبل هذه الخريطة كان الملف المترجَم يُرفض كلّه
/// برسالة «Missing required columns» رغم أن بياناته سليمة.
///
/// الفكرة: الترويسة تُطبَّع (تُسقط المسافات والنجمة وحالة الأحرف) ثم تُترجم إلى
/// الاسم الإنجليزي المعياري. فالمطابقة كلّها — التحقق من الأعمدة المطلوبة وقراءة
/// القيم — تعمل بلا تغيير في أي موضع استدعاء.
/// </summary>
public static class ImportHeaderAliases
{
    /// <summary>
    /// يُسقط كل ما ليس حرفاً أو رقماً (المسافات · النجمة · الشرطات · الأقواس)
    /// ويوحّد حالة الأحرف. «رقم الموظف *» و«رقم الموظف» و«EmployeeNo» و
    /// «employee no» كلها تصل لصيغة واحدة قابلة للمقارنة.
    /// </summary>
    public static string Normalize(string? value) =>
        new(
            (value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

    /// <summary>
    /// عربي مطبَّع ⟵ الاسم الإنجليزي المعياري. أي ترويسة غير مذكورة هنا تمرّ
    /// كما هي (فتبقى الملفات الإنجليزية والحقول المخصّصة تعمل بلا مساس).
    /// </summary>
    private static readonly Dictionary<string, string> Aliases =
        BuildAliases();

    private static Dictionary<string, string> BuildAliases()
    {
        var pairs = new (string Arabic, string Canonical)[]
        {
            // الأعمدة الأساسية — بترتيب القالب
            ("رقم الموظف", "EmployeeNo"),
            ("الاسم الكامل", "FullName"),
            ("اسم الشركة", "CompanyName"),
            ("رمز الشركة", "CompanyCode"),
            ("اسم موقع العمل", "WorkLocationName"),
            ("رمز موقع العمل", "WorkLocationCode"),
            ("اسم القسم", "DepartmentName"),
            ("رمز القسم", "DepartmentCode"),
            ("المسمى الوظيفي", "PositionName"),
            ("رمز المسمى الوظيفي", "PositionCode"),
            ("تاريخ التعيين", "HireDate"),
            ("رقم الهوية الوطنية", "NationalId"),
            ("رقم الهاتف", "Phone"),
            ("البريد الإلكتروني", "Email"),
            ("تاريخ الميلاد", "BirthDate"),
            ("الجنس", "Gender"),
            ("الحالة الاجتماعية", "MaritalStatus"),
            ("الجنسية", "Nationality"),
            ("البلد", "Country"),
            ("نوع العقد", "ContractType"),
            ("تاريخ انتهاء العقد", "ContractEndDate"),
            ("الحالة الوظيفية", "EmploymentStatus"),
            ("فعال", "IsActive"),
            ("رقم الموظف للمدير المباشر", "DirectManagerEmployeeNo"),
            ("الراتب الأساسي", "BasicSalary"),
            ("الاسم الأول عربي", "FirstName"),
            ("الاسم الثاني عربي", "SecondName"),
            ("الاسم الثالث عربي", "ThirdName"),
            ("اللقب عربي", "LastName"),
            ("الاسم الأول إنجليزي", "FirstNameEn"),
            ("الاسم الثاني إنجليزي", "SecondNameEn"),
            ("الاسم الثالث إنجليزي", "ThirdNameEn"),
            ("اللقب إنجليزي", "LastNameEn"),
            ("مواطن", "IsCitizen"),
            ("رقم جواز السفر", "PassportNo"),
            ("اسم الكفيل", "SponsorName"),
            ("الديانة", "Religion"),
            ("البلد الأم", "MotherCountry"),
            ("المدينة الأم", "MotherCity"),
            ("تاريخ المباشرة الفعلية", "JoiningDate"),
            ("نوع الدوام", "WorkType"),
            ("الدرجة الوظيفية", "JobGrade"),
            ("امتداد الهاتف", "PhoneExtension"),
            ("البريد الشخصي", "PersonalEmail"),

            // مرادفات شائعة يكتبها الناس بدل نصّ القالب حرفياً
            ("الرقم الوظيفي", "EmployeeNo"),
            ("كود الموظف", "EmployeeNo"),
            ("الاسم", "FullName"),
            ("اسم الموظف", "FullName"),
            ("الشركة", "CompanyName"),
            ("الفرع", "WorkLocationName"),
            ("اسم الفرع", "WorkLocationName"),
            ("موقع العمل", "WorkLocationName"),
            ("القسم", "DepartmentName"),
            ("المنصب", "PositionName"),
            ("الوظيفة", "PositionName"),
            ("تاريخ المباشرة", "HireDate"),
            ("تاريخ التوظيف", "HireDate"),
            ("الهاتف", "Phone"),
            ("الموبايل", "Phone"),
            ("رقم الموبايل", "Phone"),
            ("الايميل", "Email"),
            ("البريد", "Email"),
            ("رقم الهوية", "NationalId"),
            ("المدير المباشر", "DirectManagerEmployeeNo"),
            ("الراتب", "BasicSalary"),
            ("الراتب الاسمي", "BasicSalary"),
            ("رقم الجواز", "PassportNo"),
            ("الكفيل", "SponsorName"),
            ("تاريخ المباشرة الفعلي", "JoiningDate"),

            // أعمدة الورقة المرجعية — تُطابَق لتُتجاهَل كمرجع لا لتُقرأ
            // حقلاً مخصّصاً (IsReferenceHeader يفحص البادئة "ref").
            ("اسم الشركة المرجعي", "Ref Company Name"),
            ("رمز الشركة المرجعي", "Ref Company Code"),
            ("اسم موقع العمل المرجعي", "Ref Work Location Name"),
            ("رمز موقع العمل المرجعي", "Ref Work Location Code"),
            ("شركة موقع العمل المرجعية", "Ref Work Location Company"),
            ("اسم القسم المرجعي", "Ref Department Name"),
            ("رمز القسم المرجعي", "Ref Department Code"),
            ("شركة القسم المرجعية", "Ref Department Company"),
            ("اسم المسمى الوظيفي المرجعي", "Ref Position Name"),
            ("رمز المسمى الوظيفي المرجعي", "Ref Position Code"),
            ("شركة المسمى الوظيفي المرجعية", "Ref Position Company")
        };

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (arabic, canonical) in pairs)
        {
            // الأعمدة المرجعية تسبق العامّة بالترتيب المنطقي: «اسم الشركة
            // المرجعي» أطول وأدقّ من «اسم الشركة»، والمفتاح مطبَّع كامل الطول
            // فلا تتداخل — أول تعريف يفوز حمايةً من تكرارٍ عرضي.
            map.TryAdd(Normalize(arabic), canonical);
        }

        return map;
    }

    /// <summary>
    /// يُرجع الاسم الإنجليزي المعياري لترويسة عربية، أو الترويسة المطبَّعة كما
    /// هي إن لم تكن معروفة.
    /// </summary>
    public static string Canonicalize(string? header)
    {
        var normalized = Normalize(header);

        return Aliases.TryGetValue(normalized, out var canonical)
            ? Normalize(canonical)
            : normalized;
    }
}
