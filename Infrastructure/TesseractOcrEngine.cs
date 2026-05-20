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

    // الـ modes مرتبة من الأدق للأسرع
    // نجرب كل واحد إذا كانت النتيجة ضعيفة
    private static readonly PageSegMode[] FallbackModes =
    [
        PageSegMode.Auto,           // الافتراضي — layout analysis كامل
        PageSegMode.SparseText,     // جيد للفواتير والجداول المبعثرة
        PageSegMode.SingleBlock,    // block نصي واحد — جيد للـ FDA والعقود
        PageSegMode.SparseTextOsd,  // sparse مع orientation detection
    ];

    // عتبة الثقة المقبولة — إذا كانت أقل نجرب mode آخر
    private const float AcceptableConfidence = 60f;

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

        _engine.SetVariable("preserve_interword_spaces", "1");
        _engine.SetVariable("tessedit_do_invert", "0");
        _engine.SetVariable("tessedit_ocr_engine_mode", "1");  // LSTM only
        _engine.SetVariable("classify_bln_numeric_mode", "0");
        _engine.SetVariable("tessedit_enable_dict_correction", "1");

        // ── إعدادات خاصة بالمستندات البحرية والمالية ──

        // تحسين دقة الأرقام — مهم جداً للـ invoices والـ FDA
        _engine.SetVariable("tessedit_classify_debug_level", "0");

        // الحفاظ على التنسيق الأصلي للأرقام (لا يحذف الأصفار)
        _engine.SetVariable("numeric_punctuation", ".,");

        // تحسين قراءة الجداول
        _engine.SetVariable("textord_tabfind_find_tables", "1");
        _engine.SetVariable("textord_tablefind_recognize_tables", "1");
    }

    public async Task<IReadOnlyList<OcrWord>> RecognizeAsync(
        byte[] imageBytes,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var image = Image.LoadFromMemory(imageBytes);
            image.XRes = 300;
            image.YRes = 300;

            // ── نجرب الـ modes بالترتيب ونختار الأفضل ──
            List<OcrWord>? bestResult = null;
            float bestConfidence = 0f;
            PageSegMode bestMode = FallbackModes[0];

            foreach (var mode in FallbackModes)
            {
                ct.ThrowIfCancellationRequested();

                var (words, meanConfidence) = ProcessWithMode(image, mode, ct);

                _logger.LogDebug(
                    "Mode {Mode}: {Count} words | mean confidence: {Conf:F1}%",
                    mode, words.Count, meanConfidence
                );

                // نحتفظ بالأفضل دائماً
                if (meanConfidence > bestConfidence)
                {
                    bestConfidence = meanConfidence;
                    bestResult = words;
                    bestMode = mode;
                }

                // إذا وصلنا لعتبة مقبولة نتوقف — لا داعي لتجربة باقي الـ modes
                if (meanConfidence >= AcceptableConfidence)
                    break;
            }

            _logger.LogDebug(
                "Best mode: {Mode} | confidence: {Conf:F1}% | words: {Count}",
                bestMode, bestConfidence, bestResult?.Count ?? 0
            );

            return (IReadOnlyList<OcrWord>)(bestResult ?? []);

        }, ct);
    }

    // ────────────────────────────────────────────────────
    //  معالجة الصورة بـ mode محدد
    // ────────────────────────────────────────────────────
    private (List<OcrWord> Words, float MeanConfidence) ProcessWithMode(
        Image image,
        PageSegMode mode,
        CancellationToken ct)
    {
        var words = new List<OcrWord>();

        using var page = _engine.Process(image, mode);

        float meanConfidence = page.MeanConfidence * 100f;

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

        return (words, meanConfidence);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.Dispose();
    }
}