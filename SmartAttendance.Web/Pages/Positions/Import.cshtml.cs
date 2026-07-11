using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Pages.Positions;

public class ImportModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;

    private static readonly string[] TemplateColumns =
    {
        "PositionName",
        "CompanyCode",
        "Category",
        "Level",
        "Competencies",
        "EducationDegree",
        "EducationSpecialization",
        "Certifications",
        "JobPurpose",
        "KeyResponsibilities",
        "JobRequirements",
        "RequiredSkills",
        "JobKpis"
    };

    private static readonly HashSet<string> RequiredColumnKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "PositionName",
        "CompanyCode"
    };

    public ImportModel(ApplicationDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    [BindProperty]
    public string? PasteData { get; set; }

    [BindProperty]
    public string? PreviewToken { get; set; }

    public string[] Columns => TemplateColumns;

    public string[] RequiredColumns => TemplateColumns.Where(column => RequiredColumnKeys.Contains(column)).ToArray();

    public string[] OptionalColumns => TemplateColumns.Where(column => !RequiredColumnKeys.Contains(column)).ToArray();

    public List<PositionImportPreviewRow> PreviewRows { get; set; } = new();

    public int TotalRows { get; set; }

    public int ReadyRows { get; set; }

    public int ErrorRows { get; set; }

    public int CreateRows { get; set; }

    public int UpdateRows { get; set; }

    public string? SourceFileName { get; set; }

    public async Task OnGetAsync()
    {
        await EnsureReadyAsync();
    }

    public IActionResult OnGetTemplate()
    {
        var bytes = BuildTemplateWorkbook();
        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"NEXORA_Positions_Import_Template_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        await EnsureReadyAsync();

        var saved = await SaveSubmittedSourceAsync();
        if (saved == null)
        {
            TempData["ErrorMessage"] = "Please upload an Excel/CSV file or paste rows first.";
            return Page();
        }

        PreviewToken = saved.Value.Token;
        SourceFileName = saved.Value.OriginalName;

        var records = await ReadRecordsFromSavedSourceAsync(saved.Value.Token);
        PreviewRows = await BuildPreviewRowsAsync(records);

        SetSummary();
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        await EnsureReadyAsync();

        if (string.IsNullOrWhiteSpace(PreviewToken) || !Regex.IsMatch(PreviewToken, "^[a-fA-F0-9]{32}\\.(xlsx|csv|tsv|txt)$"))
        {
            TempData["ErrorMessage"] = "Preview token is missing. Please preview the file first.";
            return RedirectToPage();
        }

        var records = await ReadRecordsFromSavedSourceAsync(PreviewToken);
        var preview = await BuildPreviewRowsAsync(records);
        var validRows = preview.Where(row => row.Status == "Ready").ToList();

        var created = 0;
        var updated = 0;
        var skipped = preview.Count - validRows.Count;

        foreach (var row in validRows)
        {
            var result = await UpsertPositionAsync(row.Record);
            if (result == "Create")
            {
                created++;
            }
            else
            {
                updated++;
            }
        }

        await SyncEmployeePositionsAsync();

        TempData["SuccessMessage"] = $"Positions import completed. Created: {created}. Updated: {updated}. Skipped: {skipped}.";
        return RedirectToPage("./Index");
    }

    private Task EnsureReadyAsync()
    {
        return Task.CompletedTask;
    }

    private async Task<(string Token, string OriginalName)?> SaveSubmittedSourceAsync()
    {
        var dir = GetImportDirectory();
        Directory.CreateDirectory(dir);

        if (UploadFile != null && UploadFile.Length > 0)
        {
            var extension = Path.GetExtension(UploadFile.FileName).ToLowerInvariant();
            if (extension is not ".xlsx" and not ".csv" and not ".tsv" and not ".txt")
            {
                TempData["ErrorMessage"] = "Only .xlsx, .csv, .tsv or .txt files are supported.";
                return null;
            }

            var token = $"{Guid.NewGuid():N}{extension}";
            var path = Path.Combine(dir, token);

            await using var stream = System.IO.File.Create(path);
            await UploadFile.CopyToAsync(stream);

            return (token, UploadFile.FileName);
        }

        if (!string.IsNullOrWhiteSpace(PasteData))
        {
            var token = $"{Guid.NewGuid():N}.tsv";
            var path = Path.Combine(dir, token);
            await System.IO.File.WriteAllTextAsync(path, PasteData.Trim(), Encoding.UTF8);
            return (token, "Pasted rows");
        }

        return null;
    }

    private async Task<List<PositionImportRecord>> ReadRecordsFromSavedSourceAsync(string token)
    {
        var path = Path.Combine(GetImportDirectory(), token);
        if (!System.IO.File.Exists(path))
        {
            return new List<PositionImportRecord>();
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".xlsx")
        {
            return ReadXlsx(path);
        }

        var text = await System.IO.File.ReadAllTextAsync(path, Encoding.UTF8);
        return ReadDelimitedText(text);
    }

    private async Task<List<PositionImportPreviewRow>> BuildPreviewRowsAsync(
        List<PositionImportRecord> records)
    {
        var result = new List<PositionImportPreviewRow>();

        var companies = await _db.Companies
            .AsNoTracking()
            .Where(company => !company.IsDeleted)
            .Select(company => new
            {
                company.Id,
                company.Code,
                company.Name,
                company.IsActive
            })
            .ToListAsync();

        var companiesByCode = companies
            .Where(company => !string.IsNullOrWhiteSpace(company.Code))
            .GroupBy(
                company => NormalizeKey(company.Code),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        var existingPositions = await ReadExistingPositionKeysAsync();

        foreach (var record in records)
        {
            var messages = new List<string>();
            var positionName = NormalizeText(record.PositionName);
            var companyCode = NormalizeText(record.CompanyCode);
            int? companyId = null;
            string companyName = companyCode;

            if (string.IsNullOrWhiteSpace(positionName))
            {
                messages.Add("PositionName is required.");
            }

            if (string.IsNullOrWhiteSpace(companyCode))
            {
                messages.Add("CompanyCode is required.");
            }
            else if (!companiesByCode.TryGetValue(
                         NormalizeKey(companyCode),
                         out var company))
            {
                messages.Add("CompanyCode was not found.");
            }
            else
            {
                companyId = company.Id;
                companyName = company.Name;

                if (!company.IsActive)
                {
                    messages.Add("Company is inactive.");
                }
            }

            var key = companyId.HasValue
                ? BuildPositionKey(companyId.Value, positionName)
                : string.Empty;
            var exists = !string.IsNullOrWhiteSpace(key) &&
                         existingPositions.Contains(key);

            result.Add(new PositionImportPreviewRow
            {
                RowNumber = record.RowNumber,
                Key = positionName,
                Company = companyName,
                Action = exists ? "Update" : "Create",
                Status = messages.Count == 0 ? "Ready" : "Error",
                Message = messages.Count == 0
                    ? (exists
                        ? "Position will be updated."
                        : "Position will be created.")
                    : string.Join(" ", messages),
                Record = record
            });
        }

        return result;
    }

    private void SetSummary()
    {
        TotalRows = PreviewRows.Count;
        ReadyRows = PreviewRows.Count(row => row.Status == "Ready");
        ErrorRows = PreviewRows.Count(row => row.Status == "Error");
        CreateRows = PreviewRows.Count(row => row.Status == "Ready" && row.Action == "Create");
        UpdateRows = PreviewRows.Count(row => row.Status == "Ready" && row.Action == "Update");
    }

    private async Task<string> UpsertPositionAsync(
        PositionImportRecord record)
    {
        var positionName = NormalizeText(record.PositionName);
        var companyCode = NormalizeText(record.CompanyCode);
        var companyId = await FindCompanyIdByCodeAsync(companyCode);

        if (!companyId.HasValue)
        {
            return "Error";
        }

        var existingId = await FindPositionIdAsync(
            companyId.Value,
            positionName);

        await EnsureReferenceOptionAsync(
            "dbo.HrJobPositionCategories",
            record.Category);
        await EnsureReferenceOptionAsync(
            "dbo.HrJobPositionLevels",
            record.Level);
        await EnsureReferenceOptionAsync(
            "dbo.HrJobPositionCompetencyOptions",
            record.Competencies);
        await EnsureReferenceOptionAsync(
            "dbo.HrJobPositionEducationOptions",
            record.EducationDegree);
        await EnsureReferenceOptionAsync(
            "dbo.HrJobPositionEducationSpecializationOptions",
            record.EducationSpecialization);
        await EnsureReferenceOptionAsync(
            "dbo.HrJobPositionCertificationOptions",
            record.Certifications);

        if (existingId.HasValue)
        {
            await ExecuteNonQueryAsync(
                """
                UPDATE dbo.HrJobPositions
                SET DepartmentId = NULL,
                    Category = @Category,
                    Level = @Level,
                    JobPurpose = @JobPurpose,
                    KeyResponsibilities = @KeyResponsibilities,
                    JobRequirements = @JobRequirements,
                    RequiredSkills = @RequiredSkills,
                    JobKpis = @JobKpis,
                    Competencies = @Competencies,
                    Education = @Education,
                    EducationSpecialization = @EducationSpecialization,
                    Certifications = @Certifications,
                    IsActive = 1,
                    UpdatedAt = SYSDATETIME()
                WHERE Id = @Id
                  AND CompanyId = @CompanyId;
                """,
                command =>
                {
                    AddParameter(command, "@Id", existingId.Value);
                    AddParameter(
                        command,
                        "@CompanyId",
                        companyId.Value);
                    AddPositionParameters(command, record);
                });

            return "Update";
        }

        await ExecuteNonQueryAsync(
            """
            INSERT INTO dbo.HrJobPositions
            (
                CompanyId,
                ArabicName,
                DepartmentId,
                Category,
                Level,
                Description,
                JobPurpose,
                KeyResponsibilities,
                JobRequirements,
                RequiredSkills,
                JobKpis,
                Competencies,
                Education,
                EducationSpecialization,
                Certifications,
                IsActive,
                CreatedAt
            )
            VALUES
            (
                @CompanyId,
                @ArabicName,
                NULL,
                @Category,
                @Level,
                NULL,
                @JobPurpose,
                @KeyResponsibilities,
                @JobRequirements,
                @RequiredSkills,
                @JobKpis,
                @Competencies,
                @Education,
                @EducationSpecialization,
                @Certifications,
                1,
                SYSDATETIME()
            );
            """,
            command =>
            {
                AddParameter(command, "@CompanyId", companyId.Value);
                AddParameter(command, "@ArabicName", positionName);
                AddPositionParameters(command, record);
            });

        return "Create";
    }

    private static void AddPositionParameters(
        DbCommand command,
        PositionImportRecord record)
    {
        AddParameter(
            command,
            "@Category",
            EmptyToNull(record.Category));
        AddParameter(
            command,
            "@Level",
            EmptyToNull(record.Level));
        AddParameter(
            command,
            "@JobPurpose",
            EmptyToNull(record.JobPurpose));
        AddParameter(
            command,
            "@KeyResponsibilities",
            EmptyToNull(record.KeyResponsibilities));
        AddParameter(
            command,
            "@JobRequirements",
            EmptyToNull(record.JobRequirements));
        AddParameter(
            command,
            "@RequiredSkills",
            EmptyToNull(record.RequiredSkills));
        AddParameter(
            command,
            "@JobKpis",
            EmptyToNull(record.JobKpis));
        AddParameter(
            command,
            "@Competencies",
            EmptyToNull(record.Competencies));
        AddParameter(
            command,
            "@Education",
            EmptyToNull(record.EducationDegree));
        AddParameter(
            command,
            "@EducationSpecialization",
            EmptyToNull(record.EducationSpecialization));
        AddParameter(
            command,
            "@Certifications",
            EmptyToNull(record.Certifications));
    }

    private async Task<int?> FindCompanyIdByCodeAsync(string code)
    {
        var companies = await _db.Companies
            .AsNoTracking()
            .Where(company =>
                !company.IsDeleted &&
                company.IsActive)
            .Select(company => new
            {
                company.Id,
                company.Code
            })
            .ToListAsync();

        var normalizedCode = NormalizeKey(code);
        var company = companies.FirstOrDefault(item =>
            string.Equals(
                NormalizeKey(item.Code),
                normalizedCode,
                StringComparison.OrdinalIgnoreCase));

        return company?.Id;
    }

    private async Task<int?> FindPositionIdAsync(
        int companyId,
        string name)
    {
        var value = await ExecuteScalarAsync(
            """
            SELECT TOP 1 Id
            FROM dbo.HrJobPositions
            WHERE CompanyId = @CompanyId
              AND LTRIM(RTRIM(ArabicName)) = @ArabicName;
            """,
            command =>
            {
                AddParameter(command, "@CompanyId", companyId);
                AddParameter(
                    command,
                    "@ArabicName",
                    NormalizeText(name));
            });

        return value == null || value == DBNull.Value
            ? null
            : Convert.ToInt32(
                value,
                CultureInfo.InvariantCulture);
    }

    private async Task<HashSet<string>> ReadExistingPositionKeysAsync()
    {
        var keys = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT CompanyId, ArabicName
                FROM dbo.HrJobPositions;
                """;

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0) &&
                    !reader.IsDBNull(1))
                {
                    keys.Add(BuildPositionKey(
                        reader.GetInt32(0),
                        reader.GetString(1)));
                }
            }
        }
        finally
        {
            if (shouldClose &&
                connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        return keys;
    }

    private static string BuildPositionKey(
        int companyId,
        string? positionName)
    {
        return companyId.ToString(
                   CultureInfo.InvariantCulture) +
               "|" +
               NormalizeKey(positionName);
    }

    private async Task EnsureReferenceOptionAsync(string tableName, string? value)
    {
        var name = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await ExecuteNonQueryAsync($@"
IF EXISTS
(
    SELECT 1
    FROM {tableName}
    WHERE LTRIM(RTRIM(Name)) = @Name
)
BEGIN
    UPDATE {tableName}
    SET IsActive = 1,
        UpdatedAt = SYSDATETIME()
    WHERE LTRIM(RTRIM(Name)) = @Name;
END
ELSE
BEGIN
    INSERT INTO {tableName} (Name, IsActive, CreatedAt)
    VALUES (@Name, 1, SYSDATETIME());
END;
", command => AddParameter(command, "@Name", name));
    }

    private Task EnsurePositionProfileColumnsAsync()
    {
        return Task.CompletedTask;
    }

    private Task EnsurePositionReferenceTablesAsync()
    {
        return Task.CompletedTask;
    }

    private async Task SyncEmployeePositionsAsync()
    {
        await ExecuteNonQueryAsync(
            """
            UPDATE employee
            SET employee.PositionId = position.Id,
                employee.Position = position.ArabicName,
                employee.UpdatedAt = SYSDATETIME()
            FROM dbo.Employees employee
            INNER JOIN dbo.Branches branch
                ON branch.Id = employee.BranchId
            INNER JOIN dbo.HrJobPositions position
                ON position.CompanyId = branch.CompanyId
               AND LTRIM(RTRIM(
                       CONVERT(NVARCHAR(MAX), employee.Position))) =
                   LTRIM(RTRIM(position.ArabicName))
            WHERE employee.Position IS NOT NULL
              AND LTRIM(RTRIM(
                      CONVERT(NVARCHAR(MAX), employee.Position))) <> N''
              AND (
                  employee.PositionId IS NULL
                  OR employee.PositionId <> position.Id
              );
            """);
    }

    private byte[] BuildTemplateWorkbook()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            AddZipEntry(archive, "[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
</Types>");

            AddZipEntry(archive, "_rels/.rels", @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");

            AddZipEntry(archive, "xl/workbook.xml", @"<?xml version=""1.0"" encoding=""UTF-8""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets>
    <sheet name=""Positions"" sheetId=""1"" r:id=""rId1""/>
  </sheets>
</workbook>");

            AddZipEntry(archive, "xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
</Relationships>");

            AddZipEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml());
        }

        return memory.ToArray();
    }

    private string BuildWorksheetXml()
    {
        var sample = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PositionName"] = "HR Operations Supervisor",
            ["CompanyCode"] = "COMP-001",
            ["Category"] = "Senior Management",
            ["Level"] = "Director",
            ["Competencies"] = "Leadership",
            ["EducationDegree"] = "Bachelor",
            ["EducationSpecialization"] = "Business Administration",
            ["Certifications"] = "PMP",
            ["JobPurpose"] = "Lead HR operations.",
            ["KeyResponsibilities"] = "Manage daily HR operations.",
            ["JobRequirements"] = "Relevant HR experience.",
            ["RequiredSkills"] = "Communication, planning.",
            ["JobKpis"] = "Accuracy, response time."
        };

        var builder = new StringBuilder();
        builder.Append(@"<?xml version=""1.0"" encoding=""UTF-8""?><worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main""><sheetData>");
        builder.Append(BuildRowXml(1, TemplateColumns));
        builder.Append(BuildRowXml(2, TemplateColumns.Select(column => sample[column]).ToArray()));
        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static string BuildRowXml(int rowIndex, IReadOnlyList<string> values)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowIndex}\">");

        for (var i = 0; i < values.Count; i++)
        {
            var cellRef = $"{GetColumnName(i + 1)}{rowIndex}";
            builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{EscapeXml(values[i])}</t></is></c>");
        }

        builder.Append("</row>");
        return builder.ToString();
    }

    private static void AddZipEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static List<PositionImportRecord> ReadXlsx(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var sharedStrings = ReadSharedStrings(archive);

        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? archive.Entries.FirstOrDefault(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        if (sheetEntry == null)
        {
            return new List<PositionImportRecord>();
        }

        using var sheetStream = sheetEntry.Open();
        var document = XDocument.Load(sheetStream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;

        var rows = new List<Dictionary<int, string>>();
        foreach (var row in document.Descendants(ns + "row"))
        {
            var values = new Dictionary<int, string>();
            foreach (var cell in row.Elements(ns + "c"))
            {
                var reference = Convert.ToString(cell.Attribute("r")?.Value, CultureInfo.InvariantCulture);
                var columnIndex = GetColumnIndex(reference);
                if (columnIndex <= 0)
                {
                    continue;
                }

                values[columnIndex] = GetCellValue(cell, ns, sharedStrings);
            }

            rows.Add(values);
        }

        return RowsToRecords(rows);
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var result = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null)
        {
            return result;
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;

        foreach (var item in document.Descendants(ns + "si"))
        {
            var text = string.Concat(item.Descendants(ns + "t").Select(node => node.Value));
            result.Add(text);
        }

        return result;
    }

    private static string GetCellValue(XElement cell, XNamespace ns, List<string> sharedStrings)
    {
        var type = Convert.ToString(cell.Attribute("t")?.Value, CultureInfo.InvariantCulture);

        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(ns + "t").Select(node => node.Value));
        }

        var raw = cell.Element(ns + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex))
        {
            return sharedIndex >= 0 && sharedIndex < sharedStrings.Count ? sharedStrings[sharedIndex] : string.Empty;
        }

        return raw;
    }

    private static List<PositionImportRecord> ReadDelimitedText(string text)
    {
        var lines = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return new List<PositionImportRecord>();
        }

        var delimiter = lines[0].Contains('\t') ? '\t' : ',';
        var rows = lines.Select(line => SplitDelimitedLine(line, delimiter)).ToList();

        var indexedRows = rows
            .Select(row => row.Select((value, index) => new { index, value }).ToDictionary(item => item.index + 1, item => item.value))
            .ToList();

        return RowsToRecords(indexedRows);
    }

    private static List<PositionImportRecord> RowsToRecords(List<Dictionary<int, string>> rows)
    {
        if (rows.Count == 0)
        {
            return new List<PositionImportRecord>();
        }

        var headerRow = rows[0];
        var headerMap = headerRow.ToDictionary(
            item => item.Key,
            item => ResolveColumnKey(item.Value),
            EqualityComparer<int>.Default);

        var records = new List<PositionImportRecord>();

        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.Count == 0 || row.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var record = new PositionImportRecord
            {
                RowNumber = rowIndex + 1
            };

            foreach (var header in headerMap)
            {
                if (string.IsNullOrWhiteSpace(header.Value))
                {
                    continue;
                }

                row.TryGetValue(header.Key, out var value);
                record.Set(header.Value, NormalizeText(value));
            }

            records.Add(record);
        }

        return records;
    }

    private static string ResolveColumnKey(string? header)
    {
        var normalized = NormalizeHeader(header);
        return normalized switch
        {
            "positionname" or "position" or "arabicname" or "jobtitle" => "PositionName",
            "companycode" or "company" or "companyid" => "CompanyCode",
            "category" => "Category",
            "level" => "Level",
            "competencies" or "competency" => "Competencies",
            "educationdegree" or "education" or "degree" => "EducationDegree",
            "educationspecialization" or "specialization" or "major" => "EducationSpecialization",
            "certifications" or "certification" or "certificate" => "Certifications",
            "jobpurpose" or "purpose" => "JobPurpose",
            "keyresponsibilities" or "responsibilities" => "KeyResponsibilities",
            "jobrequirements" or "requirements" => "JobRequirements",
            "requiredskills" or "skills" => "RequiredSkills",
            "jobkpis" or "kpi" or "kpis" => "JobKpis",
            _ => string.Empty
        };
    }

    private static List<string> SplitDelimitedLine(string line, char delimiter)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == delimiter && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        values.Add(builder.ToString());
        return values;
    }

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return 0;
        }

        var letters = new string(cellReference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        var index = 0;
        foreach (var letter in letters)
        {
            index = (index * 26) + (letter - 'A' + 1);
        }

        return index;
    }

    private static string GetColumnName(int index)
    {
        var name = string.Empty;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }

        return name;
    }

    private static string NormalizeHeader(string? value)
    {
        return Regex.Replace(NormalizeText(value), @"[\s_\-:/\\]+", string.Empty).ToLowerInvariant();
    }

    private static string NormalizeKey(string? value)
    {
        return NormalizeText(value).ToLowerInvariant();
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    private static object? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string EscapeXml(string? value)
    {
        return System.Security.SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
    }

    private async Task<object?> ExecuteScalarAsync(string sql, Action<DbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task ExecuteNonQueryAsync(string sql, Action<DbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private string GetImportDirectory()
    {
        return Path.Combine(_environment.ContentRootPath, "App_Data", "PositionImports");
    }
}

public sealed class PositionImportRecord
{
    public int RowNumber { get; set; }

    public string? PositionName { get; set; }

    public string? CompanyCode { get; set; }

    public string? Category { get; set; }

    public string? Level { get; set; }

    public string? Competencies { get; set; }

    public string? EducationDegree { get; set; }

    public string? EducationSpecialization { get; set; }

    public string? Certifications { get; set; }

    public string? JobPurpose { get; set; }

    public string? KeyResponsibilities { get; set; }

    public string? JobRequirements { get; set; }

    public string? RequiredSkills { get; set; }

    public string? JobKpis { get; set; }

    public void Set(string key, string? value)
    {
        switch (key)
        {
            case "PositionName":
                PositionName = value;
                break;
            case "CompanyCode":
                CompanyCode = value;
                break;
            case "Category":
                Category = value;
                break;
            case "Level":
                Level = value;
                break;
            case "Competencies":
                Competencies = value;
                break;
            case "EducationDegree":
                EducationDegree = value;
                break;
            case "EducationSpecialization":
                EducationSpecialization = value;
                break;
            case "Certifications":
                Certifications = value;
                break;
            case "JobPurpose":
                JobPurpose = value;
                break;
            case "KeyResponsibilities":
                KeyResponsibilities = value;
                break;
            case "JobRequirements":
                JobRequirements = value;
                break;
            case "RequiredSkills":
                RequiredSkills = value;
                break;
            case "JobKpis":
                JobKpis = value;
                break;
        }
    }
}

public sealed class PositionImportPreviewRow
{
    public int RowNumber { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Company { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public PositionImportRecord Record { get; set; } = new();
}
