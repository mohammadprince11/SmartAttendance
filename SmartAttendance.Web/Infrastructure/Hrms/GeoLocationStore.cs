using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// المواقع الجغرافية للموظفين (نمط كيان — الصفحة الرابعة بقسم «حضور الموظفين»):
/// تعريف مواقع جغرافية (geofence: اسم + إحداثيات + نصف قطر) وربطها بالموظفين لتقييد
/// البصم المكاني. ملاحظة: إنفاذ التحقق وقت البصم مؤجَّل (بصماتنا الحالية بلا GPS)؛
/// هذا المخزن يوفّر التعريفات والتعيين (بيانات وواجهة).
/// </summary>
public static class GeoLocationStore
{
    public sealed class GeoLocation
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public int RadiusMeters { get; set; } = 100;
        public bool IsActive { get; set; } = true;
    }

    public sealed class EmployeeGeoRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;   // الهيكلية
        public string Branch { get; set; } = string.Empty;       // وحدة عمل/الموقع
        public int? GeoLocationId { get; set; }
        public string? GeoLocationName { get; set; }
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('GeoLocations', 'U') IS NULL
BEGIN
    CREATE TABLE GeoLocations
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name nvarchar(150) NOT NULL,
        Latitude decimal(9,6) NOT NULL DEFAULT(0),
        Longitude decimal(9,6) NOT NULL DEFAULT(0),
        RadiusMeters int NOT NULL DEFAULT(100),
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('EmployeeGeoLocations', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeGeoLocations
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        GeoLocationId int NOT NULL,
        AssignedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_EmployeeGeoLocations_Employee ON EmployeeGeoLocations (EmployeeId);
END;
""");
    }

    // ===== تعريفات المواقع =====

    public static async Task<List<GeoLocation>> ListLocationsAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT * FROM GeoLocations ORDER BY Name;",
            command => { },
            reader => new GeoLocation
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                Latitude = reader["Latitude"] is decimal la ? la : 0,
                Longitude = reader["Longitude"] is decimal lo ? lo : 0,
                RadiusMeters = HrmsDatabase.GetInt(reader, "RadiusMeters"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive")
            });
    }

    public static async Task SaveLocationAsync(ApplicationDbContext dbContext, GeoLocation loc)
    {
        await EnsureAsync(dbContext);
        if (loc.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "UPDATE GeoLocations SET Name=@Name, Latitude=@Lat, Longitude=@Lng, RadiusMeters=@Rad, IsActive=@Active WHERE Id=@Id;",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", loc.Id);
                    AddLocParams(command, loc);
                });
        }
        else
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "INSERT INTO GeoLocations (Name, Latitude, Longitude, RadiusMeters, IsActive) VALUES (@Name, @Lat, @Lng, @Rad, @Active);",
                command => AddLocParams(command, loc));
        }
    }

    public static async Task DeleteLocationAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM EmployeeGeoLocations WHERE GeoLocationId=@Id; DELETE FROM GeoLocations WHERE Id=@Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    // ===== تعيين الموظفين =====

    public static async Task<List<EmployeeGeoRow>> ListEmployeesAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT e.Id AS EmployeeId, e.EmployeeNo, e.FullName,
       ISNULL(d.Name, N'') AS DepartmentName, ISNULL(b.Name, N'') AS BranchName,
       eg.GeoLocationId, g.Name AS GeoLocationName
FROM Employees e
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
LEFT JOIN EmployeeGeoLocations eg ON eg.EmployeeId = e.Id
LEFT JOIN GeoLocations g ON g.Id = eg.GeoLocationId
WHERE e.IsActive = 1
ORDER BY e.EmployeeNo;
""",
            command => { },
            reader => new EmployeeGeoRow
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "FullName"),
                Department = HrmsDatabase.GetString(reader, "DepartmentName"),
                Branch = HrmsDatabase.GetString(reader, "BranchName"),
                GeoLocationId = HrmsDatabase.GetNullableInt(reader, "GeoLocationId"),
                GeoLocationName = HrmsDatabase.GetString(reader, "GeoLocationName") is { Length: > 0 } n ? n : null
            });
    }

    public static async Task<int> AssignAsync(ApplicationDbContext dbContext,
        IReadOnlyCollection<int> employeeIds, int geoLocationId)
    {
        await EnsureAsync(dbContext);
        if (employeeIds.Count == 0 || geoLocationId <= 0) return 0;

        foreach (var chunk in employeeIds.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                $"""
DELETE FROM EmployeeGeoLocations WHERE EmployeeId IN ({inList});
INSERT INTO EmployeeGeoLocations (EmployeeId, GeoLocationId)
SELECT Id, @Geo FROM Employees WHERE Id IN ({inList});
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Geo", geoLocationId);
                    for (var i = 0; i < chunk.Length; i++)
                        HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                });
        }
        return employeeIds.Count;
    }

    public static async Task UnassignAsync(ApplicationDbContext dbContext, IReadOnlyCollection<int> employeeIds)
    {
        await EnsureAsync(dbContext);
        if (employeeIds.Count == 0) return;
        foreach (var chunk in employeeIds.Chunk(500))
        {
            var inList = string.Join(",", chunk.Select((_, i) => $"@P{i}"));
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                $"DELETE FROM EmployeeGeoLocations WHERE EmployeeId IN ({inList});",
                command =>
                {
                    for (var i = 0; i < chunk.Length; i++)
                        HrmsDatabase.AddParameter(command, $"@P{i}", chunk[i]);
                });
        }
    }

    private static void AddLocParams(System.Data.Common.DbCommand command, GeoLocation loc)
    {
        HrmsDatabase.AddParameter(command, "@Name", loc.Name);
        HrmsDatabase.AddParameter(command, "@Lat", loc.Latitude);
        HrmsDatabase.AddParameter(command, "@Lng", loc.Longitude);
        HrmsDatabase.AddParameter(command, "@Rad", loc.RadiusMeters);
        HrmsDatabase.AddParameter(command, "@Active", loc.IsActive ? 1 : 0);
    }
}
