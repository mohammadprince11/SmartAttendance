namespace SmartAttendance.Infrastructure.Services;

/// <summary>
/// يجمّع بصمات أجهزة الحضور **بصمةً بصمة** إلى سجلّ يوميّ لكل موظف، بذاكرة
/// تتناسب مع عدد الأيام لا عدد البصمات.
///
/// السبب: ملف حقيقي لـ10,000 موظف عبر سبعة أشهر = 2.9 مليون بصمة. بناء كائن
/// عرضٍ لكل مجموعة قبل الاقتطاع كان يقتل الخادم. هنا لا يُحتفظ إلا بالخلاصة:
/// أول بصمة وآخرها وعددها، ونوعا الوظيفة والجهاز كأقنعة بت على كتالوجَين
/// صغيرين — 28 بايتاً للسجلّ اليومي بدل كائنٍ كامل.
///
/// النتيجة مُجمَّعة بلا ترتيب؛ الترتيب والاقتطاع مسؤولية المستدعي.
/// </summary>
public sealed class AttendancePunchAggregator
{
    /// <summary>سعة كتالوجَي الوظيفة والجهاز — قناع البت 32 خانة.</summary>
    public const int CatalogCapacity = 32;

    private readonly Dictionary<string, int> _employeeIndex =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _employeeNumbers = new();
    private readonly List<string> _functionTypes = new();
    private readonly List<string> _machineNames = new();

    private readonly Dictionary<long, DayAggregate> _days = new();

    /// <summary>خلاصة يومٍ واحد لموظف واحد — بنية قيمة (struct) بلا تخصيص كومة.</summary>
    public struct DayAggregate
    {
        public long FirstPunchTicks;
        public long LastPunchTicks;
        public int PunchCount;
        public uint FunctionMask;
        public uint MachineMask;
    }

    public int DayCount => _days.Count;

    public int EmployeeCount => _employeeNumbers.Count;

    public IEnumerable<KeyValuePair<long, DayAggregate>> Days => _days;

    public bool TryGetDay(long key, out DayAggregate day) =>
        _days.TryGetValue(key, out day);

    /// <summary>
    /// يضيف بصمة. <paramref name="attendanceDate"/> هو **يوم العمل** بعد أي
    /// تعديل لمناوبة ليلية — التعديل مسؤولية المستدعي لأنه سياسة لا تجميع.
    /// </summary>
    public void Add(
        string employeeNo,
        DateOnly attendanceDate,
        DateTime punchTime,
        string? functionType,
        string? machineName)
    {
        var employeeIndex = Intern(_employeeIndex, _employeeNumbers, employeeNo);
        var key = BuildKey(employeeIndex, attendanceDate);
        var ticks = punchTime.Ticks;

        var functionBit = CatalogBit(_functionTypes, functionType);
        var machineBit = CatalogBit(_machineNames, machineName);

        if (_days.TryGetValue(key, out var aggregate))
        {
            if (ticks < aggregate.FirstPunchTicks) aggregate.FirstPunchTicks = ticks;
            if (ticks > aggregate.LastPunchTicks) aggregate.LastPunchTicks = ticks;
            aggregate.PunchCount++;
            aggregate.FunctionMask |= functionBit;
            aggregate.MachineMask |= machineBit;
        }
        else
        {
            aggregate = new DayAggregate
            {
                FirstPunchTicks = ticks,
                LastPunchTicks = ticks,
                PunchCount = 1,
                FunctionMask = functionBit,
                MachineMask = machineBit
            };
        }

        _days[key] = aggregate;
    }

    public static long BuildKey(int employeeIndex, DateOnly date) =>
        ((long)employeeIndex << 32) | (uint)date.DayNumber;

    public static int EmployeeIndexOf(long key) => (int)(key >> 32);

    public static DateOnly DateOf(long key) =>
        DateOnly.FromDayNumber((int)(key & 0xFFFFFFFF));

    public string EmployeeNumberAt(int index) => _employeeNumbers[index];

    public string DescribeFunctions(uint mask) => Describe(_functionTypes, mask);

    public string DescribeMachines(uint mask) => Describe(_machineNames, mask);

    private static int Intern(
        Dictionary<string, int> index,
        List<string> values,
        string value)
    {
        if (index.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var added = values.Count;
        values.Add(value);
        index[value] = added;
        return added;
    }

    /// <summary>
    /// يعيد بت الكتالوج للقيمة. بعد امتلاء الكتالوج تُهمَل القيم الجديدة —
    /// حقلا «الأجهزة» و«أنواع البصمة» عرضٌ توضيحي لا يؤثر بالاستيراد، وتضخيمهما
    /// بلا حدّ يعيد المشكلة التي بُني هذا الصنف لحلّها.
    /// </summary>
    private static uint CatalogBit(List<string> catalog, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0u;
        }

        var trimmed = value.Trim();

        for (var i = 0; i < catalog.Count; i++)
        {
            if (string.Equals(catalog[i], trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return 1u << i;
            }
        }

        if (catalog.Count >= CatalogCapacity)
        {
            return 0u;
        }

        catalog.Add(trimmed);
        return 1u << (catalog.Count - 1);
    }

    private static string Describe(List<string> catalog, uint mask)
    {
        if (mask == 0u)
        {
            return string.Empty;
        }

        var parts = new List<string>();

        for (var i = 0; i < catalog.Count; i++)
        {
            if ((mask & (1u << i)) != 0u)
            {
                parts.Add(catalog[i]);
            }
        }

        return string.Join(", ", parts);
    }
}
