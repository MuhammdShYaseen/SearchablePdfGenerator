using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SearchablePdfGenerator.Abstractions;
using SearchablePdfGenerator.Infrastructure;

namespace SearchablePdfGenerator.Extensions;

// ═══════════════════════════════════════════════════════
//  Dependency Injection — تسجيل الخدمات
//  يمكن استخدامه في أي تطبيق ASP.NET Core / Worker / Console
// ═══════════════════════════════════════════════════════

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// تسجيل كل خدمات الـ SearchablePdfGenerator
    /// </summary>
    public static IServiceCollection AddSearchablePdfGenerator(
        this IServiceCollection services,
        Action<OcrServiceOptions>? configure = null)
    {
        var opts = new OcrServiceOptions();
        configure?.Invoke(opts);

        // تحديد مسار tessdata تلقائياً إذا لم يُحدَّد
        var tessDataPath = opts.TessDataPath
            ?? Path.Combine(AppContext.BaseDirectory, "tessdata");

        services.AddSingleton<IPdfRenderer, PdfPigRenderer>();

        services.AddSingleton<IOcrEngineFactory>(sp =>
            new TesseractOcrEngineFactory(
                sp.GetRequiredService<ILogger<TesseractOcrEngine>>(),
                tessDataPath
            )
        );

        services.AddSingleton<IText7PdfBuilder>();
        services.AddSingleton<ISearchablePdfService, SearchablePdfService>();
        services.AddSingleton<IImagePreprocessingService, OpenCvImagePreprocessingService>();
        services.AddSingleton<ImagePreprocessor>();
        return services;
    }
}

/// <summary>
/// خيارات تكوين الخدمة
/// </summary>
public sealed class OcrServiceOptions
{
    /// <summary>
    /// مسار مجلد tessdata
    /// القيمة الافتراضية: {AppBaseDir}/tessdata
    /// </summary>
    public string? TessDataPath { get; set; }
}
