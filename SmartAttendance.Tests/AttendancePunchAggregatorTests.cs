using SmartAttendance.Infrastructure.Services;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// انحدار حضور: المُجمِّع يحدّد أول بصمة وآخرها لكل يوم — ومنهما يُشتقّ الحضور
/// والانصراف ثم الراتب. أي خطأ هنا خطأ بالقسيمة.
/// </summary>
public class AttendancePunchAggregatorTests
{
    private static DateOnly Day(int d) => new(2026, 1, d);

    [Fact]
    public void First_and_last_punch_are_captured_regardless_of_input_order()
    {
        var aggregator = new AttendancePunchAggregator();
        var date = Day(21);

        // ترتيب مبعثر عمداً — ملفات الأجهزة ليست مرتّبة دائماً.
        aggregator.Add("1001", date, new DateTime(2026, 1, 21, 17, 6, 0), "OUT", "M1");
        aggregator.Add("1001", date, new DateTime(2026, 1, 21, 9, 15, 0), "IN", "M1");
        aggregator.Add("1001", date, new DateTime(2026, 1, 21, 13, 0, 0), "OUT", "M2");

        var day = Assert.Single(aggregator.Days);

        Assert.Equal(3, day.Value.PunchCount);
        Assert.Equal(new DateTime(2026, 1, 21, 9, 15, 0), new DateTime(day.Value.FirstPunchTicks));
        Assert.Equal(new DateTime(2026, 1, 21, 17, 6, 0), new DateTime(day.Value.LastPunchTicks));
    }

    [Fact]
    public void Punches_split_by_employee_and_by_day()
    {
        var aggregator = new AttendancePunchAggregator();

        aggregator.Add("1001", Day(21), new DateTime(2026, 1, 21, 8, 0, 0), "IN", "M1");
        aggregator.Add("1001", Day(22), new DateTime(2026, 1, 22, 8, 0, 0), "IN", "M1");
        aggregator.Add("1002", Day(21), new DateTime(2026, 1, 21, 8, 0, 0), "IN", "M1");

        Assert.Equal(3, aggregator.DayCount);
        Assert.Equal(2, aggregator.EmployeeCount);
    }

    /// <summary>رقم الموظف يُطابَق بلا حساسية حالة — «e1» و«E1» موظف واحد.</summary>
    [Fact]
    public void Employee_number_matching_ignores_case()
    {
        var aggregator = new AttendancePunchAggregator();

        aggregator.Add("e1", Day(21), new DateTime(2026, 1, 21, 8, 0, 0), "IN", "M1");
        aggregator.Add("E1", Day(21), new DateTime(2026, 1, 21, 17, 0, 0), "OUT", "M1");

        var day = Assert.Single(aggregator.Days);
        Assert.Equal(2, day.Value.PunchCount);
        Assert.Equal(1, aggregator.EmployeeCount);
    }

    [Fact]
    public void Function_types_and_machines_are_reported_without_duplicates()
    {
        var aggregator = new AttendancePunchAggregator();
        var date = Day(21);

        aggregator.Add("1001", date, new DateTime(2026, 1, 21, 8, 0, 0), "IN", "ZK-01");
        aggregator.Add("1001", date, new DateTime(2026, 1, 21, 12, 0, 0), "OUT", "ZK-01");
        aggregator.Add("1001", date, new DateTime(2026, 1, 21, 17, 0, 0), "OUT", "ZK-02");

        var day = Assert.Single(aggregator.Days);

        Assert.Equal("IN, OUT", aggregator.DescribeFunctions(day.Value.FunctionMask));
        Assert.Equal("ZK-01, ZK-02", aggregator.DescribeMachines(day.Value.MachineMask));
    }

    [Fact]
    public void Blank_function_and_machine_are_ignored()
    {
        var aggregator = new AttendancePunchAggregator();

        aggregator.Add("1001", Day(21), new DateTime(2026, 1, 21, 8, 0, 0), null, "   ");

        var day = Assert.Single(aggregator.Days);
        Assert.Equal(string.Empty, aggregator.DescribeFunctions(day.Value.FunctionMask));
        Assert.Equal(string.Empty, aggregator.DescribeMachines(day.Value.MachineMask));
    }

    /// <summary>
    /// الكتالوج مقيّد بـ32 (قناع بت). ما بعدها يُهمَل بلا انهيار ولا خلط —
    /// وهذا مقصود: الحقلان عرضٌ توضيحي لا يدخلان الاستيراد.
    /// </summary>
    [Fact]
    public void Catalog_overflow_is_ignored_not_corrupted()
    {
        var aggregator = new AttendancePunchAggregator();
        var date = Day(21);

        for (var i = 0; i < AttendancePunchAggregator.CatalogCapacity + 5; i++)
        {
            aggregator.Add("1001", date, new DateTime(2026, 1, 21, 8, 0, 0), "IN", $"M{i}");
        }

        var day = Assert.Single(aggregator.Days);
        var machines = aggregator.DescribeMachines(day.Value.MachineMask).Split(", ");

        Assert.Equal(AttendancePunchAggregator.CatalogCapacity, machines.Length);
        Assert.Equal(AttendancePunchAggregator.CatalogCapacity + 5, day.Value.PunchCount);
    }

    [Fact]
    public void Key_round_trips_employee_index_and_date()
    {
        var date = new DateOnly(2026, 8, 20);
        var key = AttendancePunchAggregator.BuildKey(9999, date);

        Assert.Equal(9999, AttendancePunchAggregator.EmployeeIndexOf(key));
        Assert.Equal(date, AttendancePunchAggregator.DateOf(key));
    }

    /// <summary>
    /// حارس المقياس: 10,000 موظف × 200 يوم × بصمتين = 4 ملايين بصمة تُجمَّع
    /// إلى مليونَي سجلّ يومي. النسخة السابقة كانت تبني كائن عرضٍ لكل مجموعة
    /// فيموت الخادم؛ هنا يبقى المحفوظ خلاصةً ثابتة الحجم لكل يوم.
    /// </summary>
    [Fact]
    public void Aggregate_count_tracks_days_not_punches()
    {
        var aggregator = new AttendancePunchAggregator();
        var start = new DateTime(2026, 1, 1, 8, 0, 0);

        for (var employee = 0; employee < 50; employee++)
        {
            for (var day = 0; day < 40; day++)
            {
                var stamp = start.AddDays(day);
                var date = DateOnly.FromDateTime(stamp);

                aggregator.Add($"E{employee}", date, stamp, "IN", "M1");
                aggregator.Add($"E{employee}", date, stamp.AddHours(9), "OUT", "M1");
                aggregator.Add($"E{employee}", date, stamp.AddHours(4), "OUT", "M1");
            }
        }

        Assert.Equal(50 * 40, aggregator.DayCount);
        Assert.Equal(50, aggregator.EmployeeCount);
    }
}
