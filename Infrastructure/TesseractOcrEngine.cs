using Microsoft.Extensions.Logging;
using SearchablePdfGenerator.Abstractions;
using SearchablePdfGenerator.Domain;
using Tesseract;

namespace SearchablePdfGenerator.Infrastructure;

// ═══════════════════════════════════════════════════════
//  Tesseract OCR Engine
//  المكتبة Tesseract NuGet تتضمن:
//    - tesseract.dll (x64/x86/arm64)
//    - leptonica.dll
//  وتحتاج فقط ملفات traineddata للغات
// ═══════════════════════════════════════════════════════

public sealed class TesseractOcrEngine : IOcrEngine
{
    private readonly ILogger<TesseractOcrEngine>  _logger;
    private readonly string                        _tessDataPath;
    private readonly string                        _languages;
    private readonly float                         _minConfidence;
    private readonly TesseractEngine               _engine;
    private bool                                   _disposed;

    public TesseractOcrEngine(
        ILogger<TesseractOcrEngine> logger,
        string tessDataPath,
        string languages    = "eng+ara",
        float  minConfidence = 30f)
    {
        _logger        = logger;
        _tessDataPath  = tessDataPath;
        _languages     = languages;
        _minConfidence = minConfidence;

        _logger.LogInformation(
            "Initializing Tesseract | tessdata: {Path} | languages: {Lang}",
            tessDataPath, languages
        );

        _engine = new TesseractEngine(tessDataPath, languages, EngineMode.Default);

        // إعدادات للحصول على أفضل دقة
        _engine.SetVariable("preserve_interword_spaces", "1");
        _engine.SetVariable("tessedit_do_invert",        "0");
    }

    public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(
        byte[] imageBytes,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var words = new List<OcrWord>();

            // تحويل byte[] إلى Pix (صيغة Tesseract الداخلية)
            using var pix  = Pix.LoadFromMemory(imageBytes);
            using var page = _engine.Process(pix);

            using var iterator = page.GetIterator();
            iterator.Begin();

            do
            {
                ct.ThrowIfCancellationRequested();

                if (!iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var bbox))
                    continue;

                var text       = iterator.GetText(PageIteratorLevel.Word)?.Trim();
                var confidence = iterator.GetConfidence(PageIteratorLevel.Word);

                // تجاهل كلمات فارغة أو منخفضة الثقة
                if (string.IsNullOrWhiteSpace(text) || confidence < _minConfidence)
                    continue;

                words.Add(new OcrWord(
                    Text:        text,
                    Confidence:  confidence,
                    BoundingBox: new OcrBoundingBox(
                        X1: bbox.X1, Y1: bbox.Y1,
                        X2: bbox.X2, Y2: bbox.Y2
                    )
                ));

            } while (iterator.Next(PageIteratorLevel.Word));

            _logger.LogDebug("Recognized {Count} words", words.Count);

            return (IReadOnlyList<OcrWord>)words;

        }, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Dispose();
    }
}
