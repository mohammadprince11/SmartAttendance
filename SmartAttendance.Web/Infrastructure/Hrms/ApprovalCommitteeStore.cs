using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>إدارة لجان الموافقة، بعزل شركة صريح ومن دون إنشاء مخطط وقت الطلب.</summary>
public static class ApprovalCommitteeStore
{
    public sealed class GroupRow
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public List<string> Members { get; set; } = new();
    }

    public sealed class ExternalRow
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ContactName { get; set; }
        public string? ContactEmail { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; }
    }

    public static async Task<List<GroupRow>> ListGroupsAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, bool activeOnly = false)
    {
        Demand(scope, companyId);
        var groups = await HrmsDatabase.QueryAsync(db, """
SELECT Id,CompanyId,Name,Description,IsActive
FROM ApprovalCommitteeGroups
WHERE CompanyId=@CompanyId AND (@ActiveOnly=0 OR IsActive=1)
ORDER BY IsActive DESC,Name;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@ActiveOnly", activeOnly ? 1 : 0);
        }, reader => new GroupRow
        {
            Id = HrmsDatabase.GetInt(reader, "Id"), CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
            Name = HrmsDatabase.GetString(reader, "Name"), Description = HrmsDatabase.GetString(reader, "Description"),
            IsActive = HrmsDatabase.GetBool(reader, "IsActive")
        });

        if (groups.Count == 0) return groups;
        var ids = groups.Select(group => group.Id).ToArray();
        var names = ids.Select((_, index) => $"@Id{index}").ToArray();
        var members = await HrmsDatabase.QueryAsync(db, $"""
SELECT GroupId,UserName FROM ApprovalCommitteeGroupMembers
WHERE GroupId IN ({string.Join(',', names)}) ORDER BY GroupId,SortOrder,Id;
""", command =>
        {
            for (var index = 0; index < ids.Length; index++) HrmsDatabase.AddParameter(command, names[index], ids[index]);
        }, reader => (GroupId: HrmsDatabase.GetInt(reader, "GroupId"), UserName: HrmsDatabase.GetString(reader, "UserName")));
        foreach (var group in groups)
            group.Members = members.Where(member => member.GroupId == group.Id).Select(member => member.UserName).ToList();
        return groups;
    }

    public static Task<List<ExternalRow>> ListExternalAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, bool activeOnly = false)
    {
        Demand(scope, companyId);
        return HrmsDatabase.QueryAsync(db, """
SELECT Id,CompanyId,Name,ContactName,ContactEmail,Notes,IsActive
FROM ApprovalExternalCommittees
WHERE CompanyId=@CompanyId AND (@ActiveOnly=0 OR IsActive=1)
ORDER BY IsActive DESC,Name;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@ActiveOnly", activeOnly ? 1 : 0);
        }, reader => new ExternalRow
        {
            Id = HrmsDatabase.GetInt(reader, "Id"), CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
            Name = HrmsDatabase.GetString(reader, "Name"), ContactName = HrmsDatabase.GetString(reader, "ContactName"),
            ContactEmail = HrmsDatabase.GetString(reader, "ContactEmail"), Notes = HrmsDatabase.GetString(reader, "Notes"),
            IsActive = HrmsDatabase.GetBool(reader, "IsActive")
        });
    }

    public static async Task<int> SaveGroupAsync(
        ApplicationDbContext db, CompanyScope scope, int companyId, int id,
        string name, string? description, IReadOnlyCollection<string> members, string actor)
    {
        Demand(scope, companyId);
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) throw new ArgumentException("اسم مجموعة اللجنة مطلوب.");
        var duplicate = await HrmsDatabase.ScalarAsync<int>(db, """
SELECT COUNT(1) FROM ApprovalCommitteeGroups WHERE CompanyId=@CompanyId AND Name=@Name AND Id<>@Id;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            HrmsDatabase.AddParameter(command, "@Name", name);
            HrmsDatabase.AddParameter(command, "@Id", id);
        });
        if (duplicate > 0) throw new ArgumentException("يوجد اسم مجموعة لجنة مطابق داخل الشركة.");
        var users = members.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (users.Length == 0) throw new ArgumentException("اختر عضواً واحداً على الأقل.");

        var validUsers = await db.SystemUsers.AsNoTracking()
            .Where(user => !user.IsDeleted && user.IsActive && user.Employee != null && user.Employee.CompanyId == companyId && users.Contains(user.UserName))
            .Select(user => user.UserName).Distinct().CountAsync();
        if (validUsers != users.Length) throw new ArgumentException("أحد أعضاء اللجنة غير نشط أو خارج الشركة.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        if (id > 0)
        {
            await HrmsDatabase.ExecuteAsync(db, """
UPDATE ApprovalCommitteeGroups SET Name=@Name,Description=@Description,IsActive=1,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id AND CompanyId=@CompanyId;
IF @@ROWCOUNT=0 THROW 50001,'Committee group outside company scope.',1;
DELETE FROM ApprovalCommitteeGroupMembers WHERE GroupId=@Id;
""", command => AddGroupParameters(command, companyId, id, name, description, actor));
        }
        else
        {
            id = await HrmsDatabase.ScalarAsync<int>(db, """
INSERT INTO ApprovalCommitteeGroups(CompanyId,Name,Description,CreatedBy)
VALUES(@CompanyId,@Name,@Description,@Actor); SELECT CAST(SCOPE_IDENTITY() AS int);
""", command => AddGroupParameters(command, companyId, 0, name, description, actor));
        }

        for (var index = 0; index < users.Length; index++)
        {
            await HrmsDatabase.ExecuteAsync(db, """
INSERT INTO ApprovalCommitteeGroupMembers(GroupId,UserName,SortOrder) VALUES(@GroupId,@UserName,@SortOrder);
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@GroupId", id);
                HrmsDatabase.AddParameter(command, "@UserName", users[index]);
                HrmsDatabase.AddParameter(command, "@SortOrder", index + 1);
            });
        }
        await transaction.CommitAsync();
        return id;
    }

    public static async Task<int> SaveExternalAsync(
        ApplicationDbContext db, CompanyScope scope, ExternalRow row, string actor)
    {
        Demand(scope, row.CompanyId);
        row.Name = (row.Name ?? string.Empty).Trim();
        if (row.Name.Length == 0) throw new ArgumentException("اسم اللجنة الخارجية مطلوب.");
        if (!string.IsNullOrWhiteSpace(row.ContactEmail) && !System.Net.Mail.MailAddress.TryCreate(row.ContactEmail.Trim(), out _))
            throw new ArgumentException("البريد الإلكتروني لجهة الاتصال غير صحيح.");
        var duplicate = await HrmsDatabase.ScalarAsync<int>(db, """
SELECT COUNT(1) FROM ApprovalExternalCommittees WHERE CompanyId=@CompanyId AND Name=@Name AND Id<>@Id;
""", command =>
        {
            HrmsDatabase.AddParameter(command, "@CompanyId", row.CompanyId);
            HrmsDatabase.AddParameter(command, "@Name", row.Name);
            HrmsDatabase.AddParameter(command, "@Id", row.Id);
        });
        if (duplicate > 0) throw new ArgumentException("يوجد اسم لجنة خارجية مطابق داخل الشركة.");
        if (row.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(db, """
UPDATE ApprovalExternalCommittees SET Name=@Name,ContactName=@ContactName,ContactEmail=@ContactEmail,Notes=@Notes,IsActive=1,UpdatedAt=SYSUTCDATETIME()
WHERE Id=@Id AND CompanyId=@CompanyId;
IF @@ROWCOUNT=0 THROW 50001,'External committee outside company scope.',1;
""", command => AddExternalParameters(command, row, actor));
            return row.Id;
        }
        return await HrmsDatabase.ScalarAsync<int>(db, """
INSERT INTO ApprovalExternalCommittees(CompanyId,Name,ContactName,ContactEmail,Notes,CreatedBy)
VALUES(@CompanyId,@Name,@ContactName,@ContactEmail,@Notes,@Actor); SELECT CAST(SCOPE_IDENTITY() AS int);
""", command => AddExternalParameters(command, row, actor));
    }

    public static Task DeactivateGroupAsync(ApplicationDbContext db, CompanyScope scope, int companyId, int id)
    {
        Demand(scope, companyId);
        return HrmsDatabase.ExecuteAsync(db,
            "UPDATE ApprovalCommitteeGroups SET IsActive=0,UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id AND CompanyId=@CompanyId;",
            command => { HrmsDatabase.AddParameter(command, "@Id", id); HrmsDatabase.AddParameter(command, "@CompanyId", companyId); });
    }

    public static Task DeactivateExternalAsync(ApplicationDbContext db, CompanyScope scope, int companyId, int id)
    {
        Demand(scope, companyId);
        return HrmsDatabase.ExecuteAsync(db,
            "UPDATE ApprovalExternalCommittees SET IsActive=0,UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id AND CompanyId=@CompanyId;",
            command => { HrmsDatabase.AddParameter(command, "@Id", id); HrmsDatabase.AddParameter(command, "@CompanyId", companyId); });
    }

    private static void Demand(CompanyScope scope, int companyId)
    {
        if (!scope.Allows(companyId)) throw new UnauthorizedAccessException();
    }

    private static void AddGroupParameters(System.Data.Common.DbCommand command, int companyId, int id, string name, string? description, string actor)
    {
        HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
        HrmsDatabase.AddParameter(command, "@Id", id);
        HrmsDatabase.AddParameter(command, "@Name", name);
        HrmsDatabase.AddParameter(command, "@Description", string.IsNullOrWhiteSpace(description) ? DBNull.Value : description.Trim());
        HrmsDatabase.AddParameter(command, "@Actor", actor);
    }

    private static void AddExternalParameters(System.Data.Common.DbCommand command, ExternalRow row, string actor)
    {
        HrmsDatabase.AddParameter(command, "@Id", row.Id);
        HrmsDatabase.AddParameter(command, "@CompanyId", row.CompanyId);
        HrmsDatabase.AddParameter(command, "@Name", row.Name);
        HrmsDatabase.AddParameter(command, "@ContactName", Value(row.ContactName));
        HrmsDatabase.AddParameter(command, "@ContactEmail", Value(row.ContactEmail));
        HrmsDatabase.AddParameter(command, "@Notes", Value(row.Notes));
        HrmsDatabase.AddParameter(command, "@Actor", actor);
    }

    private static object Value(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}
