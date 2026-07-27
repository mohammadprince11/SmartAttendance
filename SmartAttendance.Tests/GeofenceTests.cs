using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>اختبارات إنفاذ النطاق الجغرافي للبصم الأونلاين — هافرساين + العضوية بالنطاقات. نقي.</summary>
public class GeofenceTests
{
    // بغداد: برج الجمهورية تقريباً
    private const double Lat = 33.3152, Lng = 44.3661;

    [Fact]
    public void Distance_SamePoint_Zero()
    {
        Assert.Equal(0, OnlinePunchStore.DistanceMeters(Lat, Lng, Lat, Lng), 3);
    }

    [Fact]
    public void Distance_KnownOffset_About111KmPerDegreeLat()
    {
        var d = OnlinePunchStore.DistanceMeters(Lat, Lng, Lat + 1, Lng);
        Assert.InRange(d, 110_000, 112_000);
    }

    [Fact]
    public void Inside_WithinRadius_True()
    {
        // ~55م شمالاً (0.0005 درجة عرض) داخل نطاق 100م
        var zones = new[] { (Lat, Lng, 100) };
        Assert.True(OnlinePunchStore.IsInsideAny(zones, Lat + 0.0005, Lng));
    }

    [Fact]
    public void Inside_OutsideRadius_False()
    {
        // ~555م شمالاً خارج نطاق 100م
        var zones = new[] { (Lat, Lng, 100) };
        Assert.False(OnlinePunchStore.IsInsideAny(zones, Lat + 0.005, Lng));
    }

    [Fact]
    public void Inside_AnyOfMultipleZones_True()
    {
        var zones = new[] { (Lat, Lng, 50), (Lat + 0.01, Lng, 200) };
        Assert.True(OnlinePunchStore.IsInsideAny(zones, Lat + 0.0995 / 10, Lng));
    }

    [Fact]
    public void Inside_TinyRadius_ClampedToTenMeters()
    {
        // نصف قطر 0 يُثبَّت لحد أدنى 10م — نقطة على بعد ~5م تُقبل
        var zones = new[] { (Lat, Lng, 0) };
        Assert.True(OnlinePunchStore.IsInsideAny(zones, Lat + 0.00004, Lng));
    }
}
