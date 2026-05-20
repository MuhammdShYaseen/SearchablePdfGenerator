using Microsoft.Extensions.Logging;
using SkiaSharp;
using TesseractOCR;
using TesseractOCR.Enums;
using TesseractOCR.Pix;

namespace SearchablePdfGenerator.Infrastructure;

// ═══════════════════════════════════════════════════════════════════
//  PageOrientationDetector
//
//  النهج: Confidence-Based Voting
//
//  بدلاً من الاعتماد على metadata الـ PDF (غير موثوق)
//  أو OSD وحده (دقته 90% فقط)
//
//  نجرب الـ 4 اتجاهات الممكنة ونسأل Tesseract:
//  "أي اتجاه يجعلك أكثر ثقة بقراءة النص؟"
//
//  الاتجاه الذي يعطي أعلى MeanConfidence = الاتجاه الصحيح
// ═══════════════════════════════════════════════════════════════════

public sealed class PageOrientationDetector
{
    private readonly ILogger<PageOrientationDetector> _logger;
    private readonly string _tessDataPath;

    // الـ 4 اتجاهات الممكنة
    private static readonly int[] Rotations = [0, 90, 180, 270];

    // عتبة الفرق المقبول بين أفضل اتجاهين
    // إذا كان الفرق أقل من هذا → الصورة مستقيمة على الأرجح
    private const float MinConfidenceDelta = 0.05f;

    public PageOrientationDetector(
        ILogger<PageOrientationDetector> logger,
        string tessDataPath)
    {
        _logger = logger;
        _tessDataPath = tessDataPath;
    }

    /// <summary>
    /// يعيد زاوية التصحيح المطلوبة (0/90/180/270)
    /// 0 = الصورة مستقيمة — لا حاجة لتصحيح
    /// </summary>
    public int Detect(byte[] pngBytes)
    {
        // ── المرحلة 1: OSD السريع ──
        // نبدأ بـ OSD لأنه سريع — إذا confidence عالية نثق به
        var osdResult = TryOsdDetection(pngBytes);

        if (osdResult.HasValue && osdResult.Value.Confidence >= 3.0f)
        {
            _logger.LogDebug(
                "OSD result accepted: {Degrees}° | confidence: {Conf:F2}",
                osdResult.Value.Degrees, osdResult.Value.Confidence
            );
            return osdResult.Value.Degrees;
        }

        _logger.LogDebug(
            "OSD confidence too low ({Conf}), falling back to voting",
            osdResult?.Confidence.ToString("F2") ?? "N/A"
        );

        // ── المرحلة 2: Confidence Voting ──
        // نجرب الـ 4 اتجاهات ونختار الأفضل
        return VotingDetection(pngBytes);
    }

    // ────────────────────────────────────────────────────────────────
    //  المرحلة 1: OSD Detection
    //  سريع لكن يحتاج osd.traineddata
    // ────────────────────────────────────────────────────────────────
    private (int Degrees, float Confidence)? TryOsdDetection(byte[] pngBytes)
    {
        try
        {
            using var osdEngine = new Engine(
                _tessDataPath,
                "osd",
                EngineMode.Default
            );

            using var image = Image.LoadFromMemory(pngBytes);
            image.XRes = 300;
            image.YRes = 300;

            using var page = osdEngine.Process(image, PageSegMode.OsdOnly);

            page.DetectOrientationAndScript(
                out int degrees,
                out float confidence,
                out ScriptName scriptName,
                out float scriptConfidence
            );

            _logger.LogDebug(
                "OSD → {Degrees}° | conf: {Conf:F2} | script: {Script} ({SC:F2})",
                degrees, confidence, scriptName.ToString(), scriptConfidence
            );

            return (degrees, confidence);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("OSD failed: {Msg}", ex.Message);
            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  المرحلة 2: Confidence Voting
    //  نجرب كل اتجاه ونقيس MeanConfidence من Tesseract
    //  الأعلى confidence = الاتجاه الصحيح
    // ────────────────────────────────────────────────────────────────
    private int VotingDetection(byte[] pngBytes)
    {
        var results = new List<(int Rotation, float Confidence, int WordCount)>();

        // engine مؤقت للتصويت — لغة إنجليزية فقط (أسرع)
        // لا نحتاج دقة OCR كاملة هنا — فقط confidence score
        using var votingEngine = new Engine(
            _tessDataPath,
            "eng",
            EngineMode.LstmOnly  // LSTM أسرع وأكفأ للتصويت
        );

        foreach (int rotation in Rotations)
        {
            // دوّر الصورة
            var rotatedBytes = rotation == 0
                ? pngBytes
                : RotateImage(pngBytes, rotation);

            using var image = Image.LoadFromMemory(rotatedBytes);
            image.XRes = 300;
            image.YRes = 300;

            // نستخدم SparseText — أسرع من Auto للتصويت
            using var page = votingEngine.Process(image, PageSegMode.SparseText);

            float confidence = page.MeanConfidence;
            int wordCount = CountWords(page);

            results.Add((rotation, confidence, wordCount));

            _logger.LogDebug(
                "Rotation {R}°: confidence={C:F4} | words={W}",
                rotation, confidence, wordCount
            );
        }

        // ── اختيار الفائز ──
        // نرتب بـ composite score:
        // confidence (الأهم) + bonus إذا عدد الكلمات أعلى
        var winner = results
            .OrderByDescending(r => ComputeScore(r.Confidence, r.WordCount))
            .First();

        // إذا كان الفرق بين الأول والثاني صغير جداً → لا تصحيح
        var runner = results
            .OrderByDescending(r => ComputeScore(r.Confidence, r.WordCount))
            .Skip(1)
            .First();

        float delta = ComputeScore(winner.Confidence, winner.WordCount)
                    - ComputeScore(runner.Confidence, runner.WordCount);

        if (delta < MinConfidenceDelta && winner.Rotation != 0)
        {
            _logger.LogDebug(
                "Delta too small ({D:F4}), keeping original orientation",
                delta
            );
            return 0;
        }

        _logger.LogInformation(
            "Orientation detected: {R}° (score={S:F4}, delta={D:F4})",
            winner.Rotation,
            ComputeScore(winner.Confidence, winner.WordCount),
            delta
        );

        return winner.Rotation;
    }

    // ── Composite Score ──
    // confidence هو المعيار الأساسي
    // wordCount يكسر التعادل عندما تكون الـ confidence متقاربة
    private static float ComputeScore(float confidence, int wordCount)
    {
        // وزن confidence = 90% | وزن wordCount = 10%
        float normalizedWords = Math.Min(wordCount / 100f, 1f);
        return (confidence * 0.9f) + (normalizedWords * 0.1f);
    }

    private static int CountWords(TesseractOCR.Page page)
    {
        int count = 0;
        foreach (var block in page.Layout)
        {
            if (block is null) continue;
            foreach (var para in block.Paragraphs)
            {
                if (para is null) continue;
                foreach (var line in para.TextLines)
                {
                    if (line is null) continue;
                    foreach (var word in line.Words)
                    {
                        if (word?.Text is not null)
                            count++;
                    }
                }
            }
        }
        return count;
    }

    // ────────────────────────────────────────────────────────────────
    //  تدوير الصورة بـ SkiaSharp
    // ────────────────────────────────────────────────────────────────
    public static byte[] RotateImage(byte[] pngBytes, int degrees)
    {
        if (degrees == 0) return pngBytes;

        using var src = SKBitmap.Decode(pngBytes);

        bool swap = degrees is 90 or 270;
        int newWidth = swap ? src.Height : src.Width;
        int newHeight = swap ? src.Width : src.Height;

        var rotated = new SKBitmap(newWidth, newHeight);

        using var canvas = new SKCanvas(rotated);

        canvas.Clear(SKColors.White);
        canvas.Translate(newWidth / 2f, newHeight / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-src.Width / 2f, -src.Height / 2f);
        canvas.DrawBitmap(src, 0, 0);

        using var image = SKImage.FromBitmap(rotated);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}