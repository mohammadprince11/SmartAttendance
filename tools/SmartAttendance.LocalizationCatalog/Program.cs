using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SmartAttendance.Web.Infrastructure.Localization;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: SmartAttendance.LocalizationCatalog <web-content-root> <output-json>");
    return 2;
}

var contentRoot = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);

if (!Directory.Exists(contentRoot))
{
    Console.Error.WriteLine($"Content root does not exist: {contentRoot}");
    return 3;
}

var keys = LocalizationSourceTextScanner.Scan(contentRoot)
    .OrderBy(key => key, StringComparer.Ordinal)
    .ToArray();

if (keys.Length == 0)
{
    Console.Error.WriteLine("Localization source scanner returned zero keys.");
    return 4;
}

var outputDirectory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrWhiteSpace(outputDirectory))
    Directory.CreateDirectory(outputDirectory);

var json = JsonSerializer.Serialize(keys, new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
});

File.WriteAllText(outputPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine($"Generated localization source catalog: {keys.Length} scanned keys -> {outputPath}");
return 0;