using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SearchablePdfGenerator.Abstractions;
using Math = System.Math;

namespace SearchablePdfGenerator.Infrastructure
{
    public sealed class OpenCvImagePreprocessingService : IImagePreprocessingService
    {
        private readonly ILogger<OpenCvImagePreprocessingService> _logger;

        public OpenCvImagePreprocessingService(
            ILogger<OpenCvImagePreprocessingService> logger)
        {
            _logger = logger;
        }

        public async Task<byte[]> ProcessForOcr(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return imageBytes ?? Array.Empty<byte>();

            try
            {
                using var src = Cv2.ImDecode(imageBytes, ImreadModes.Color);
                if (src.Empty())
                    return imageBytes;

                // 1. تحويل إلى Grayscale
                using var gray = new Mat();
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

                // 2. إزالة الظلال
                using var shadowRemoved = RemoveShadows(gray);

                // 3. تصحيح الميلان إذا لزم الأمر
                using var deskewed = CorrectSkewIfNeeded(shadowRemoved);

                // 4. تحسين التباين بـ CLAHE
                using var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8));
                using var enhanced = new Mat();
                clahe.Apply(deskewed, enhanced);

                // 5. تنعيم مع الحفاظ على الحواف
                using var denoised = new Mat();
                Cv2.BilateralFilter(enhanced, denoised, 9, 75, 75);

                return await Task.Run(() => denoised.ToBytes(".png"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preprocessing image");
                return imageBytes;
            }
        }

        private static Mat RemoveShadows(Mat gray)
        {
            // تمويه قوي لاستخراج الخلفية
            using var background = new Mat();
            Cv2.GaussianBlur(gray, background, new Size(51, 51), 0);

            // قسمة الصورة على الخلفية
            using var corrected = new Mat();
            Cv2.Divide(gray, background, corrected, 255.0);

            // معادلة التباين المحسنة
            using var clahe = Cv2.CreateCLAHE(2.5, new Size(8, 8));
            using var result = new Mat();
            clahe.Apply(corrected, result);

            return result.Clone();
        }

        private static Mat CorrectSkewIfNeeded(Mat gray)
        {
            double angle = DetectSkewAngle(gray);

            // فقط إذا كان الميلان واضحًا (> 1.5 درجة)
            if (Math.Abs(angle) <= 1.5)
                return gray.Clone();

            var center = new Point2f(gray.Width / 2f, gray.Height / 2f);
            var rotMatrix = Cv2.GetRotationMatrix2D(center, angle, 1.0);

            var rotated = new Mat();
            Cv2.WarpAffine(gray, rotated, rotMatrix, gray.Size(),
                InterpolationFlags.Cubic, BorderTypes.Replicate);

            return rotated;
        }

        private static double DetectSkewAngle(Mat gray)
        {
            // تحضير الصورة للكشف
            using var small = new Mat();
            using var edges = new Mat();
            using var dilated = new Mat();

            // تصغير الصورة للسرعة
            int maxDimension = 1000;
            if (gray.Width > maxDimension)
            {
                double scale = (double)maxDimension / gray.Width;
                int newHeight = (int)(gray.Height * scale);
                Cv2.Resize(gray, small, new Size(maxDimension, newHeight));
            }
            else
            {
                gray.CopyTo(small);
            }

            // كشف الحواف
            Cv2.Canny(small, edges, 50, 150, 3);

            // توسيع الحواف لربط الخطوط المتقطعة
            var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.Dilate(edges, dilated, kernel);

            // كشف الخطوط
            var lines = Cv2.HoughLinesP(
                dilated,
                rho: 1,
                theta: Math.PI / 180,
                threshold: 80,
                minLineLength: small.Width / 3,
                maxLineGap: 20);

            if (lines == null || lines.Length < 3)
                return 0;

            double sumAngle = 0;
            int count = 0;

            foreach (var line in lines)
            {
                double dx = line.P2.X - line.P1.X;
                double dy = line.P2.Y - line.P1.Y;

                if (Math.Abs(dx) < 1) continue;

                double angle = Math.Atan2(dy, dx) * 180 / Math.PI;

                // فقط الزوايا المعقولة (بين -10 و 10 درجات)
                if (Math.Abs(angle) < 10 && Math.Abs(angle) > 0.5)
                {
                    sumAngle += angle;
                    count++;
                }
            }

            return count > 0 ? sumAngle / count : 0;
        }
    }
}