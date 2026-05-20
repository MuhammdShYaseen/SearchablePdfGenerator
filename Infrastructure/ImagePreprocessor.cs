using OpenCvSharp;
using Microsoft.Extensions.Logging;

namespace SearchablePdfGenerator.Infrastructure;

public sealed class ImagePreprocessor
{
    private readonly ILogger<ImagePreprocessor> _logger;

    public ImagePreprocessor(ILogger<ImagePreprocessor> logger)
    {
        _logger = logger;
    }

    public byte[] Process(byte[] pngBytes)
    {
        // PNG bytes → OpenCV Mat
        using var src = Mat.FromImageData(pngBytes, ImreadModes.Color);

        // ── Step 1: Grayscale ──
        using var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

        // ── Step 2: Deskew ──
        using var deskewed = Deskew(gray);

        // ── Step 3: Denoise ──
        using var denoised = new Mat();
        Cv2.FastNlMeansDenoising(deskewed, denoised, h: 10);

        // ── Step 4: Adaptive Threshold ──
        // أفضل بكثير من Otsu للصور ذات الإضاءة غير المنتظمة
        using var binary = new Mat();
        Cv2.AdaptiveThreshold(
            denoised,
            binary,
            maxValue: 255,
            adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
            thresholdType: ThresholdTypes.Binary,
            blockSize: 15,   // حجم المنطقة المحلية
            c: 10    // ثابت الطرح
        );

        // ── Step 5: Morphological Closing ──
        // يصلح الأحرف المكسورة أو المتقطعة
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new Size(1, 1)
        );
        using var morphed = new Mat();
        Cv2.MorphologyEx(binary, morphed, MorphTypes.Close, kernel);

        _logger.LogDebug(
            "Preprocessing complete: {W}×{H}",
            morphed.Width, morphed.Height
        );

        // OpenCV Mat → PNG bytes
        return morphed.ToBytes(".png");
    }

    private static Mat Deskew(Mat gray)
    {
        // ── حساب زاوية الميلان ──
        using var edges = new Mat();
        Cv2.Canny(gray, edges, 50, 150, apertureSize: 3);

        // HoughLines لاكتشاف الخطوط الأفقية
        var lines = Cv2.HoughLinesP(
            edges,
            rho: 1,
            theta: Math.PI / 180,
            threshold: 100,
            minLineLength: gray.Width / 4.0,
            maxLineGap: 20
        );

        if (lines.Length == 0)
            return gray.Clone();

        // حساب متوسط زاوية الخطوط
        double angleSum = 0;
        int validLines = 0;

        foreach (var line in lines)
        {
            double angle = Math.Atan2(
                line.P2.Y - line.P1.Y,
                line.P2.X - line.P1.X
            ) * 180.0 / Math.PI;

            // نأخذ فقط الخطوط شبه الأفقية (ميلان أقل من 15°)
            if (Math.Abs(angle) < 15)
            {
                angleSum += angle;
                validLines++;
            }
        }

        if (validLines == 0)
            return gray.Clone();

        double skewAngle = angleSum / validLines;

        // إذا الميلان أقل من 0.5° لا داعي للتصحيح
        if (Math.Abs(skewAngle) < 0.5)
            return gray.Clone();

        // تطبيق التصحيح
        var center = new Point2f(gray.Width / 2f, gray.Height / 2f);
        using var rotMatrix = Cv2.GetRotationMatrix2D(center, skewAngle, scale: 1.0);

        var deskewed = new Mat();
        Cv2.WarpAffine(
            gray,
            deskewed,
            rotMatrix,
            new Size(gray.Width, gray.Height),
            flags: InterpolationFlags.Cubic,
            borderMode: BorderTypes.Replicate
        );

        return deskewed;
    }
}