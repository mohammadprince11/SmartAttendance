using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// وثائق الشركة وفئاتها (نظير `/Employees/DocumentCenter` و`DocumentCategory` بكيان).
///
/// **الفرق عن <c>EmployeeDocuments</c>**: تلك وثائق موظفٍ بعينه (عقده · شهادته)،
/// وهذه وثائق **مشتركة** (لوائح · نماذج · تعاميم · أدلّة). خلط الاثنين كان يعني
/// إمّا نسخ اللائحة 1356 مرّة أو تركها خارج النظام بمجلّد شبكة.
///
/// والجمهور يُحدَّد بمحرّك الشروط العام: وثيقةٌ تخصّ فرعاً أو فئة لا تظهر لغيرهم.
/// </summary>
public static class CompanyDocumentStore
{
    public sealed record Category(int Id, string Name, string? Description, int SortOrder, bool IsActive, int DocumentCount);

    public sealed record Document(
        int Id,
        int? CategoryId,
        string CategoryName,
        string Title,
        string? Description,
        string? FileName,
        string? StorageKey,
        DateOnly? ExpiryDate,
        bool VisibleToEmployees,
        bool IsActive,
        string ConditionsJson)
    {
        public HrConditions.ConditionSet Conditions => HrConditions.Deserialize(ConditionsJson);
        public bool HasFile => !string.IsNullOrWhiteSpace(StorageKey);

        /// <summary>وثيقة منتهية الصلاحية تبقى بالسجلّ لكنها تُعلَّم — الحذف يمحو تاريخاً.</summary>
        public bool IsExpired(DateOnly asOf) => ExpiryDate is { } expiry && expiry < asOf;
    }

    // ── الفئات ─────────────────────────────────────────────────────────────────

    public static async Task<List<Category>> LoadCategoriesAsync(ApplicationDbContext db) =>
        await HrmsDatabase.QueryAsync(
            db,
            """
SELECT c.Id, c.Name, c.Description, c.SortOrder, c.IsActive,
       DocumentCount = (SELECT COUNT(*) FROM CompanyDocuments d
                        WHERE d.CategoryId = c.Id AND ISNULL(d.IsDeleted, 0) = 0)
FROM CompanyDocumentCategories c
WHERE ISNULL(c.IsDeleted, 0) = 0
ORDER BY c.SortOrder, c.Name;
""",
            command => { },
            reader => new Category(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Name"),
                HrmsDatabase.GetString(reader, "Description"),
                HrmsDatabase.GetInt(reader, "SortOrder"),
                HrmsDatabase.GetBool(reader, "IsActive"),
                HrmsDatabase.GetInt(reader, "DocumentCount")));

    public static async Task SaveCategoryAsync(
        ApplicationDbContext db, int id, string name, string? description, int sortOrder, bool isActive)
    {
        if (id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE CompanyDocumentCategories
SET Name = @Name, Description = @Description, SortOrder = @SortOrder, IsActive = @IsActive
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", id);
                    HrmsDatabase.AddParameter(command, "@Name", name);
                    HrmsDatabase.AddParameter(command, "@Description", description);
                    HrmsDatabase.AddParameter(command, "@SortOrder", sortOrder);
                    HrmsDatabase.AddParameter(command, "@IsActive", isActive);
                });
            return;
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO CompanyDocumentCategories (Name, Description, SortOrder, IsActive)
VALUES (@Name, @Description, @SortOrder, @IsActive);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Name", name);
                HrmsDatabase.AddParameter(command, "@Description", description);
                HrmsDatabase.AddParameter(command, "@SortOrder", sortOrder);
                HrmsDatabase.AddParameter(command, "@IsActive", isActive);
            });
    }

    /// <summary>
    /// حذف الفئة **لا يحذف وثائقها** — تصير بلا فئة وتبقى ظاهرة. حذفٌ متتالٍ هنا
    /// كان سيمحو لوائح الشركة كلّها بضغطة على فئة.
    /// </summary>
    public static async Task DeleteCategoryAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE CompanyDocuments SET CategoryId = NULL WHERE CategoryId = @Id;
UPDATE CompanyDocumentCategories SET IsDeleted = 1 WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

    // ── الوثائق ────────────────────────────────────────────────────────────────

    public static async Task<List<Document>> LoadDocumentsAsync(
        ApplicationDbContext db, int? categoryId = null, bool visibleOnly = false)
    {
        var filters = new List<string>();
        if (categoryId is not null) filters.Add("d.CategoryId = @CategoryId");
        if (visibleOnly) filters.Add("d.VisibleToEmployees = 1 AND d.IsActive = 1");

        var where = filters.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", filters);

        return await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT d.Id, d.CategoryId, ISNULL(c.Name, N'بلا فئة') AS CategoryName, d.Title, d.Description,
       d.FileName, d.StorageKey, d.ExpiryDate, d.VisibleToEmployees, d.IsActive,
       ISNULL(d.ConditionsJson, N'') AS ConditionsJson
FROM CompanyDocuments d
LEFT JOIN CompanyDocumentCategories c ON c.Id = d.CategoryId
WHERE ISNULL(d.IsDeleted, 0) = 0{where}
ORDER BY ISNULL(c.SortOrder, 9999), c.Name, d.Title;
""",
            command =>
            {
                if (categoryId is not null) HrmsDatabase.AddParameter(command, "@CategoryId", categoryId);
            },
            reader => new Document(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetNullableInt(reader, "CategoryId"),
                HrmsDatabase.GetString(reader, "CategoryName"),
                HrmsDatabase.GetString(reader, "Title"),
                HrmsDatabase.GetString(reader, "Description"),
                HrmsDatabase.GetString(reader, "FileName"),
                HrmsDatabase.GetString(reader, "StorageKey"),
                HrmsDatabase.GetDateOnly(reader, "ExpiryDate"),
                HrmsDatabase.GetBool(reader, "VisibleToEmployees"),
                HrmsDatabase.GetBool(reader, "IsActive"),
                HrmsDatabase.GetString(reader, "ConditionsJson")));
    }

    public static async Task<Document?> FindDocumentAsync(ApplicationDbContext db, int id) =>
        (await LoadDocumentsAsync(db)).FirstOrDefault(row => row.Id == id);

    public static async Task<int> SaveDocumentAsync(
        ApplicationDbContext db,
        int id,
        int? categoryId,
        string title,
        string? description,
        DateOnly? expiryDate,
        bool visibleToEmployees,
        bool isActive,
        HrConditions.ConditionSet conditions,
        string? fileName,
        string? storageKey,
        string? user)
    {
        if (id > 0)
        {
            // الملف يُحدَّث **فقط إن رُفع جديد**: حفظ التعديلات بلا اختيار ملف كان
            // سيمسح المرفق القائم ويترك وثيقةً بلا وثيقة.
            await HrmsDatabase.ExecuteAsync(
                db,
                """
UPDATE CompanyDocuments
SET CategoryId = @CategoryId, Title = @Title, Description = @Description,
    ExpiryDate = @ExpiryDate, VisibleToEmployees = @Visible, IsActive = @IsActive,
    ConditionsJson = @Conditions,
    FileName = ISNULL(@FileName, FileName), StorageKey = ISNULL(@StorageKey, StorageKey),
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;
""",
                command => Bind(command, id, categoryId, title, description, expiryDate,
                    visibleToEmployees, isActive, conditions, fileName, storageKey, user));

            return id;
        }

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO CompanyDocuments
    (CategoryId, Title, Description, ExpiryDate, VisibleToEmployees, IsActive,
     ConditionsJson, FileName, StorageKey, CreatedBy)
OUTPUT INSERTED.Id
VALUES (@CategoryId, @Title, @Description, @ExpiryDate, @Visible, @IsActive,
        @Conditions, @FileName, @StorageKey, @CreatedBy);
""",
            command => Bind(command, id, categoryId, title, description, expiryDate,
                visibleToEmployees, isActive, conditions, fileName, storageKey, user));
    }

    private static void Bind(
        System.Data.Common.DbCommand command,
        int id, int? categoryId, string title, string? description, DateOnly? expiryDate,
        bool visible, bool isActive, HrConditions.ConditionSet conditions,
        string? fileName, string? storageKey, string? user)
    {
        if (id > 0) HrmsDatabase.AddParameter(command, "@Id", id);
        HrmsDatabase.AddParameter(command, "@CategoryId", categoryId);
        HrmsDatabase.AddParameter(command, "@Title", title);
        HrmsDatabase.AddParameter(command, "@Description", description);
        HrmsDatabase.AddParameter(command, "@ExpiryDate", expiryDate?.ToDateTime(TimeOnly.MinValue));
        HrmsDatabase.AddParameter(command, "@Visible", visible);
        HrmsDatabase.AddParameter(command, "@IsActive", isActive);
        HrmsDatabase.AddParameter(command, "@Conditions", HrConditions.Serialize(conditions));
        HrmsDatabase.AddParameter(command, "@FileName", fileName);
        HrmsDatabase.AddParameter(command, "@StorageKey", storageKey);
        if (id <= 0) HrmsDatabase.AddParameter(command, "@CreatedBy", user);
    }

    public static async Task DeleteDocumentAsync(ApplicationDbContext db, int id) =>
        await HrmsDatabase.ExecuteAsync(
            db,
            "UPDATE CompanyDocuments SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

    /// <summary>
    /// الوثائق التي يراها موظف بعينه: الظاهرة للموظفين + من تنطبق عليه شروطها.
    /// **بلا شروط ⟶ للجميع** (نمط أهلية لا نسخة سياسة).
    /// </summary>
    public static async Task<List<Document>> LoadForEmployeeAsync(
        ApplicationDbContext db, int employeeId, DateOnly asOf)
    {
        var documents = await LoadDocumentsAsync(db, visibleOnly: true);
        if (documents.Count == 0)
        {
            return documents;
        }

        var rows = await HrConditionFacts.LoadAsync(db, employeeId);
        if (rows.Count == 0)
        {
            return new List<Document>();
        }

        var facts = HrConditionFacts.Build(rows[0], asOf);

        return documents
            .Where(document => HrConditions.Matches(document.Conditions, facts, matchWhenEmpty: true))
            .ToList();
    }
}
