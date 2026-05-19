using Microsoft.Extensions.Logging;
using SearchablePdfGenerator.Abstractions;
using SearchablePdfGenerator.Domain;
using SkiaSharp;
using TesseractOCR;
using TesseractOCR.Enums;

namespace SearchablePdfGenerator.Infrastructure;

public sealed class SearchablePdfService : ISearchablePdfService
{
    private readonly ILogger<SearchablePdfService> _logger;
    private readonly IPdfRenderer _renderer;
    private readonly IOcrEngineFactory _ocrFactory;
    private readonly IText7PdfBuilder _builder;  // ✅ اسم جديد

    public SearchablePdfService(
        ILogger<SearchablePdfService> logger,
        IPdfRenderer renderer,
        IOcrEngineFactory ocrFactory,
        IText7PdfBuilder builder)
    {
        _logger = logger;
        _renderer = renderer;
        _ocrFactory = ocrFactory;
        _builder = builder;
    }

    public async Task<OcrDocumentResult> ProcessAsync(
        string inputPdfPath,
        string outputPdfPath,
        OcrOptions? options = null,
        IProgress<OcrProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new OcrOptions();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        if (!File.Exists(inputPdfPath))
            throw new FileNotFoundException("Input PDF not found", inputPdfPath);

        if (options.KeepOriginalBackup)
        {
            var backupPath = inputPdfPath + ".original.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(inputPdfPath, backupPath);
                _logger.LogInformation("Backup created: {Backup}", backupPath);
            }
        }

        int totalPages = _renderer.GetPageCount(inputPdfPath);

        _logger.LogInformation(
            "Processing: {File} ({Pages} pages) | Lang: {Lang} | DPI: {Dpi}",
            Path.GetFileName(inputPdfPath),
            totalPages,
            options.Languages,
            options.RenderDpi
        );

        using var ocrEngine = _ocrFactory.Create(options);

        var ocrPages = new List<OcrPageResult>(totalPages);
        var pageData = new List<(OcrPageResult, byte[])>(totalPages);
        int processed = 0;

        for (int i = 0; i < totalPages; i++)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new OcrProgress(i, totalPages, $"Processing page {i + 1}/{totalPages}"));

            // ── خطوة 1: PDF → PNG bytes ──
            var imageBytes = await _renderer.RenderPageToImageAsync(
                inputPdfPath, i, options.RenderDpi, ct
            );

            // ── خطوة 2: أبعاد الصورة عبر SkiaSharp ──
            var (widthPx, heightPx) = GetImageDimensions(imageBytes);

            // ── خطوة 3: OCR ──
            var words = await ocrEngine.RecognizeAsync(imageBytes, ct);

            var ocrPage = new OcrPageResult(
                PageNumber: i + 1,
                Words: words,
                PageWidthPx: widthPx,
                PageHeightPx: heightPx
            );

            ocrPages.Add(ocrPage);
            pageData.Add((ocrPage, imageBytes));
            processed++;

            _logger.LogDebug(
                "Page {Page}/{Total}: {Words} words recognized",
                i + 1, totalPages, words.Count
            );
        }

        progress?.Report(new OcrProgress(totalPages, totalPages, "Building searchable PDF..."));

        await _builder.BuildWithImagesAsync(
            outputPdfPath,
            pageData,
            options.RenderDpi,
            ct
        );

        stopwatch.Stop();

        var result = new OcrDocumentResult(
            InputPath: inputPdfPath,
            OutputPath: outputPdfPath,
            TotalPages: totalPages,
            ProcessedPages: processed,
            Pages: ocrPages,
            Duration: stopwatch.Elapsed
        );

        _logger.LogInformation(
            "✓ Done in {Duration:F1}s | {Pages} pages | {Words} total words | Output: {Output}",
            stopwatch.Elapsed.TotalSeconds,
            processed,
            ocrPages.Sum(p => p.Words.Count),
            Path.GetFileName(outputPdfPath)
        );

        progress?.Report(new OcrProgress(totalPages, totalPages, "Complete"));

        return result;
    }

    // ✅ SkiaSharp بدلاً من SixLabors.ImageSharp
    private static (int Width, int Height) GetImageDimensions(byte[] imageBytes)
    {
        using var bitmap = SKBitmap.Decode(imageBytes);
        return (bitmap.Width, bitmap.Height);
    }
}

// ═══════════════════════════════════════════════════════
//  IOcrEngineFactory + TesseractOcrEngineFactory
// ═══════════════════════════════════════════════════════

public interface IOcrEngineFactory
{
    IOcrEngine Create(OcrOptions options);
}

public sealed class TesseractOcrEngineFactory : IOcrEngineFactory
{
    private readonly ILogger<TesseractOcrEngine> _logger;
    private readonly string _defaultTessDataPath;

    public TesseractOcrEngineFactory(
        ILogger<TesseractOcrEngine> logger,
        string defaultTessDataPath)
    {
        _logger = logger;
        _defaultTessDataPath = defaultTessDataPath;
    }

    public IOcrEngine Create(OcrOptions options)
    {
        var tessDataPath = options.TessDataPath ?? _defaultTessDataPath;

        return new TesseractOcrEngine(
            _logger,
            tessDataPath,
            options.Languages,
            options.MinConfidence
        );
    }
}