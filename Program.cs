using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SearchablePdfGenerator.Abstractions;
using SearchablePdfGenerator.Domain;
using SearchablePdfGenerator.Extensions;

// ── DI Setup ──
var services = new ServiceCollection()
    .AddLogging(logging =>
    {
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .AddSearchablePdfGenerator(opts =>
    {
        // opts.TessDataPath = @"C:\MyApp\tessdata";
    });

using var provider = services.BuildServiceProvider();
var pdfService = provider.GetRequiredService<ISearchablePdfService>();

// ── واجهة Console ──
Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║   Searchable PDF Generator — Pure C#     ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.WriteLine();

// ── قراءة المسار من المستخدم ──
string inputPath;

// إذا مرّر المستخدم المسار كـ argument استخدمه مباشرة
// وإلا اطلب منه الإدخال
if (args.Length > 0)
{
    inputPath = args[0];
    Console.WriteLine($"Input (from args): {inputPath}");
}
else
{
    Console.Write("Enter PDF path: ");
    inputPath = Console.ReadLine()?.Trim().Trim('"') ?? string.Empty;
}

if (string.IsNullOrWhiteSpace(inputPath))
{
    Console.WriteLine("✗ No path provided.");
    Environment.Exit(1);
}

if (!File.Exists(inputPath))
{
    Console.WriteLine($"✗ File not found: {inputPath}");
    Environment.Exit(1);
}

var outputPath = Path.Combine(
    Path.GetDirectoryName(inputPath)!,
    Path.GetFileNameWithoutExtension(inputPath) + ".searchable.pdf"
);

Console.WriteLine($"Input:  {inputPath}");
Console.WriteLine($"Output: {outputPath}");
Console.WriteLine();

// ── خيارات OCR ──
var options = new OcrOptions
{
    Languages = "eng+ara",
    RenderDpi = 300,
    MinConfidence = 35f,
    Deskew = true,
    KeepOriginalBackup = true,
};

// ── Progress Reporting ──
var progress = new Progress<OcrProgress>(p =>
{
    var bar = new string('█', (int)(p.Percentage / 5));
    var empty = new string('░', 20 - bar.Length);
    Console.Write($"\r[{bar}{empty}] {p.Percentage:F0}% — {p.Status}   ");
});

try
{
    var result = await pdfService.ProcessAsync(
        inputPath,
        outputPath,
        options,
        progress,
        CancellationToken.None
    );

    Console.WriteLine();
    Console.WriteLine();

    if (result.IsSuccess)
    {
        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine("║              ✓ SUCCESS                    ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.WriteLine($"  Pages:     {result.TotalPages}");
        Console.WriteLine($"  Words:     {result.Pages.Sum(p => p.Words.Count):N0}");
        Console.WriteLine($"  Duration:  {result.Duration.TotalSeconds:F2}s");
        Console.WriteLine($"  Output:    {result.OutputPath}");
        Console.WriteLine();
        Console.WriteLine("  The PDF now supports:");
        Console.WriteLine("    ✓ CTRL+F search");
        Console.WriteLine("    ✓ Text selection & copy");
        Console.WriteLine("    ✓ Screen readers");
        Console.WriteLine("    ✓ Full-text indexing");
    }
    else
    {
        Console.WriteLine("⚠ Partial success: some pages may have failed.");
    }
}
catch (FileNotFoundException ex)
{
    Console.WriteLine($"\n✗ File not found: {ex.FileName}");
    Environment.Exit(1);
}
catch (Exception ex)
{
    Console.WriteLine($"\n✗ Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    Environment.Exit(2);
}