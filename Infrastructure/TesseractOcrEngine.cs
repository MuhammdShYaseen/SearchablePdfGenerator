using Microsoft.Extensions.Logging;
using SearchablePdfGenerator.Abstractions;
using SearchablePdfGenerator.Domain;
using TesseractOCR;
using TesseractOCR.Enums;
using TesseractOCR.Pix;

namespace SearchablePdfGenerator.Infrastructure;

public sealed class TesseractOcrEngine : IOcrEngine
{
    private readonly ILogger<TesseractOcrEngine> _logger;
    private readonly float _minConfidence;
    private readonly Engine _engine;
    private bool _disposed;

    public TesseractOcrEngine(
        ILogger<TesseractOcrEngine> logger,
        string tessDataPath,
        string languages = "eng+ara",
        float minConfidence = 30f)
    {
        _logger = logger;
        _minConfidence = minConfidence;

        _logger.LogInformation(
            "Initializing TesseractOCR | tessdata: {Path} | languages: {Lang}",
            tessDataPath, languages
        );

        _engine = new Engine(tessDataPath, languages, EngineMode.Default);

        // ── إعدادات لتحسين الدقة إلى أقصى حد ──

        // الحفاظ على المسافات بين الكلمات
        _engine.SetVariable("preserve_interword_spaces", "1");

        // لا تعكس الصورة تلقائياً
        _engine.SetVariable("tessedit_do_invert", "0");

        // LSTM فقط — أدق من الـ legacy engine
        _engine.SetVariable("tessedit_ocr_engine_mode", "1");

        // تحسين دقة الأرقام والرموز الخاصة
        _engine.SetVariable("classify_bln_numeric_mode", "0");

        // تقليل الأخطاء في الكلمات القصيرة
        _engine.SetVariable("tessedit_char_blacklist", "");

        // اعتبار كل الكلمات حتى الأقل ثقة
        _engine.SetVariable("tessedit_enable_dict_correction", "1");
    }

    public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(
        byte[] imageBytes,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var words = new List<OcrWord>();

            using var image = Image.LoadFromMemory(imageBytes);

            // ✅ تحديد DPI صراحةً — يمنع warning "Invalid resolution 0 dpi"
            image.XRes = 300;
            image.YRes = 300;

            // ✅ PageSegMode.Auto — يحلل layout الصفحة تلقائياً
            // أفضل من SingleBlock للمستندات المختلطة (نصوص + جداول + أرقام)
            using var page = _engine.Process(image, PageSegMode.Auto);

            _logger.LogDebug(
                "Mean confidence: {Confidence:F1}%",
                page.MeanConfidence * 100
            );

            // ✅ التنقل عبر Layout hierarchy كاملاً
            // Block → Paragraph → TextLine → Word
            // أدق من iterator مباشر
            foreach (var block in page.Layout)
            {
                if (block is null) continue;

                foreach (var paragraph in block.Paragraphs)
                {
                    if (paragraph is null) continue;

                    foreach (var line in paragraph.TextLines)
                    {
                        if (line is null) continue;

                        foreach (var word in line.Words)
                        {
                            ct.ThrowIfCancellationRequested();

                            if (word is null) continue;

                            var text = word.Text?.Trim();

                            if (string.IsNullOrWhiteSpace(text)) continue;
                            if (word.Confidence < _minConfidence) continue;

                            var bb = word.BoundingBox;
                            if (bb is null) continue;

                            words.Add(new OcrWord(
                                Text: text,
                                Confidence: word.Confidence,
                                BoundingBox: new OcrBoundingBox(
                                    X1: bb.Value.X1,
                                    Y1: bb.Value.Y1,
                                    X2: bb.Value.X2,
                                    Y2: bb.Value.Y2
                                )
                            ));
                        }
                    }
                }
            }

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