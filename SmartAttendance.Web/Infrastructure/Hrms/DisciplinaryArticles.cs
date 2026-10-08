using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>User-written reference articles, isolated by tenant. No policy is seeded
/// and free text never changes the penalty engine or payroll calculations.</summary>
public static class DisciplinaryArticles
{
    public sealed record Article(string Number, string Title, string Text);
    public sealed record Document(List<Article> Articles, string Version);
    public const int MaxArticles = 100;

    public static string SettingKey(int tenantId) => tenantId > 0
        ? $"Tenant:{tenantId}:DisciplinaryArticles.v1"
        : throw new ArgumentOutOfRangeException(nameof(tenantId));

    public static string Version(string? json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json ?? string.Empty)));

    public static List<Article> Validate(string[]? numbers, string[]? titles, string[]? texts)
    {
        numbers ??= []; titles ??= []; texts ??= [];
        // One extra empty input row is rendered after the saved rows.
        if (numbers.Length != titles.Length || titles.Length != texts.Length || numbers.Length > MaxArticles + 1)
            throw new ArgumentException("عدد صفوف القواعد غير صحيح أو يتجاوز 100 قاعدة.");
        var rows = new List<Article>();
        var usedNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < numbers.Length; i++)
        {
            var row = new Article(numbers[i]?.Trim() ?? "", titles[i]?.Trim() ?? "", texts[i]?.Trim() ?? "");
            if (row.Number.Length + row.Title.Length + row.Text.Length == 0) continue;
            if (row.Number.Length == 0 || row.Title.Length == 0 || row.Text.Length == 0)
                throw new ArgumentException($"أكمل رقم وعنوان ونص القاعدة في الصف {i + 1}.");
            if (row.Number.Length > 60 || row.Title.Length > 180 || row.Text.Length > 4000)
                throw new ArgumentException($"الصف {i + 1} يتجاوز طول الحقول المسموح.");
            if (!usedNumbers.Add(row.Number)) throw new ArgumentException("رقم القاعدة مكرر.");
            rows.Add(row);
            if (rows.Count > MaxArticles) throw new ArgumentException("الحد الأقصى 100 قاعدة.");
        }
        return rows;
    }

    public static Document Parse(string? json)
    {
        var rows = string.IsNullOrWhiteSpace(json) ? []
            : JsonSerializer.Deserialize<List<Article>>(json) ?? throw new JsonException();
        if (rows.Any(x => x is null)) throw new JsonException();
        return new Document(Validate(rows.Select(x => x.Number).ToArray(),
            rows.Select(x => x.Title).ToArray(), rows.Select(x => x.Text).ToArray()), Version(json));
    }

    public static async Task<Document> LoadAsync(ApplicationDbContext db, int tenantId)
    {
        var values = await HrmsDatabase.QueryAsync(db,
            "SELECT [Value] FROM DisciplinarySettings WHERE [Key] = @Key;",
            command => HrmsDatabase.AddParameter(command, "@Key", SettingKey(tenantId)),
            reader => HrmsDatabase.GetString(reader, "Value"));
        return Parse(values.SingleOrDefault());
    }

    // Serializable lock + content version avoid two editors silently overwriting
    // one another, including the first insert. Identical retries are harmless.
    public static async Task<bool> SaveAsync(ApplicationDbContext db, int tenantId,
        List<Article> articles, string expectedVersion)
    {
        var key = SettingKey(tenantId);
        var normalized = Validate(articles.Select(x => x.Number).ToArray(),
            articles.Select(x => x.Title).ToArray(), articles.Select(x => x.Text).ToArray());
        var json = JsonSerializer.Serialize(normalized);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var values = await HrmsDatabase.QueryAsync(db,
            "SELECT [Value] FROM DisciplinarySettings WITH (UPDLOCK, HOLDLOCK) WHERE [Key] = @Key;",
            command => HrmsDatabase.AddParameter(command, "@Key", key),
            reader => HrmsDatabase.GetString(reader, "Value"));
        var current = values.SingleOrDefault();
        if (!string.Equals(Version(current), expectedVersion, StringComparison.Ordinal))
        {
            // A retried successful request must not write again or mask a conflict.
            return string.Equals(current, json, StringComparison.Ordinal);
        }
        await HrmsDatabase.ExecuteAsync(db,
            """
IF EXISTS (SELECT 1 FROM DisciplinarySettings WHERE [Key] = @Key)
    UPDATE DisciplinarySettings SET [Value] = @Value, UpdatedAt = SYSUTCDATETIME() WHERE [Key] = @Key;
ELSE
    INSERT INTO DisciplinarySettings([Key], [Value], UpdatedAt) VALUES(@Key, @Value, SYSUTCDATETIME());
""", command =>
            {
                HrmsDatabase.AddParameter(command, "@Key", key);
                HrmsDatabase.AddParameter(command, "@Value", json);
            });
        await transaction.CommitAsync();
        return true;
    }
}
