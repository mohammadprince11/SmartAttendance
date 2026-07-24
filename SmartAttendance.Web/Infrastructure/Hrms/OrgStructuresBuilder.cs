using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// الهياكل الثلاث المتوازية (نمط كيان — قسم 17.ي بدراسة الأشخاص): ثلاثة أبعاد
/// تنظيمية مستقلة على <b>نفس الموظفين</b>، كلٌّ شجرة تجميع مختلفة:
/// (1) وحدات الأعمال — الفرع ← القسم، (2) الهيكل الهرمي — المدير المباشر (رفع التقارير)،
/// (3) الهيكل الوظيفي — المنصب ← القسم. تُبنى من البيانات القائمة بلا سكيمة جديدة،
/// وتخدم تقارير وصلاحيات ومسارات موافقات مختلفة على نفس القوى العاملة.
/// </summary>
public static class OrgStructuresBuilder
{
    /// <summary>عقدة شجرة عامة لأي بُعد تنظيمي.</summary>
    public sealed class Node
    {
        public string Name { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public int Count { get; set; }                 // عدد الموظفين تحت العقدة (شاملاً)
        public List<Node> Children { get; set; } = new();
    }

    /// <summary>البُعد الأول: وحدات الأعمال — الفرع ثم القسم بعدّاد موظفين.</summary>
    public static async Task<Node> BusinessUnitsAsync(ApplicationDbContext db, int companyId, string companyName)
    {
        var rows = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive && !e.IsDeleted && e.Branch.CompanyId == companyId)
            .Select(e => new { Branch = e.Branch.Name, Department = e.Department.Name })
            .ToListAsync();

        var root = new Node { Name = companyName, Subtitle = "وحدات الأعمال", Count = rows.Count };
        foreach (var branchGroup in rows.GroupBy(r => string.IsNullOrWhiteSpace(r.Branch) ? "— بلا فرع —" : r.Branch)
                     .OrderByDescending(g => g.Count()))
        {
            var branchNode = new Node { Name = branchGroup.Key, Subtitle = "فرع", Count = branchGroup.Count() };
            foreach (var deptGroup in branchGroup.GroupBy(r => string.IsNullOrWhiteSpace(r.Department) ? "— بلا قسم —" : r.Department)
                         .OrderByDescending(g => g.Count()))
            {
                branchNode.Children.Add(new Node { Name = deptGroup.Key, Subtitle = "قسم", Count = deptGroup.Count() });
            }
            root.Children.Add(branchNode);
        }
        return root;
    }

    /// <summary>البُعد الثالث: الهيكل الوظيفي — المنصب ثم القسم بعدّاد موظفين.</summary>
    public static async Task<Node> FunctionalAsync(ApplicationDbContext db, int companyId, string companyName)
    {
        var rows = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive && !e.IsDeleted && e.Branch.CompanyId == companyId)
            .Select(e => new
            {
                Position = e.PositionId != null
                    ? db.HrJobPositions.Where(p => p.Id == e.PositionId).Select(p => p.ArabicName).FirstOrDefault()
                    : e.Position,
                Department = e.Department.Name
            })
            .ToListAsync();

        var root = new Node { Name = companyName, Subtitle = "الهيكل الوظيفي", Count = rows.Count };
        foreach (var posGroup in rows.GroupBy(r => string.IsNullOrWhiteSpace(r.Position) ? "— بلا مسمى —" : r.Position!)
                     .OrderByDescending(g => g.Count()))
        {
            var posNode = new Node { Name = posGroup.Key, Subtitle = "مسمى وظيفي", Count = posGroup.Count() };
            foreach (var deptGroup in posGroup.GroupBy(r => string.IsNullOrWhiteSpace(r.Department) ? "— بلا قسم —" : r.Department)
                         .OrderByDescending(g => g.Count()))
            {
                posNode.Children.Add(new Node { Name = deptGroup.Key, Subtitle = "قسم", Count = deptGroup.Count() });
            }
            root.Children.Add(posNode);
        }
        return root;
    }

    /// <summary>البُعد الثاني: الهيكل الهرمي — المدير المباشر (رفع التقارير). يعيد استخدام OrgChartBuilder.</summary>
    public static async Task<(List<Node> Roots, int WithManager, int WithoutManager, int Managers)>
        HierarchyAsync(ApplicationDbContext db, int companyId)
    {
        var data = await Pages.Organization.OrgChartBuilder.BuildAsync(db, companyId);
        var roots = data.ManagerRoots.Select(ToNode).ToList();
        return (roots, data.WithManagerCount, data.WithoutManagerCount, data.ManagerCount);
    }

    private static Node ToNode(Pages.Organization.OrgNode n)
    {
        var node = new Node
        {
            Name = n.FullName,
            Subtitle = string.IsNullOrWhiteSpace(n.Position) ? n.DepartmentName : n.Position,
            Count = n.TotalReports
        };
        foreach (var c in n.Children)
            node.Children.Add(ToNode(c));
        return node;
    }
}
