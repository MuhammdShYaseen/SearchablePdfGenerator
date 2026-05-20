using Microsoft.Extensions.Logging;
using SearchablePdfGenerator.Abstractions;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace SearchablePdfGenerator.Infrastructure;

public sealed class PdfPigRenderer : IPdfRenderer
{
    private readonly ILogger<PdfPigRenderer> _logger;
    private readonly PageOrientationDetector _orientationDetector;

    public PdfPigRenderer(
        ILogger<PdfPigRenderer> logger,
        PageOrientationDetector orientationDetector)
    {
        _logger = logger;
        _orientationDetector = orientationDetector;
    }

    public int GetPageCount(string pdfPath)
    {
        using var doc = PdfDocument.Open(pdfPath);
        return doc.NumberOfPages;
    }

    public async Task<byte[]> RenderPageToImageAsync(
    string pdfPath,
    int pageIndex,
    int dpi,
    CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            int pageNumber = pageIndex + 1;
            float scale = dpi / 72f;

            _logger.LogDebug(
                "Rendering page {Page} at {Dpi} DPI",
                pageNumber,
                dpi
            );

            using var doc = PdfDocument.Open(
                pdfPath,
                SkiaRenderingParsingOptions.Instance
            );

            doc.AddSkiaPageFactory();

            using var bitmap = doc.GetPageAsSKBitmap(
                pageNumber,
                scale,
                SKColors.White
            );

            _logger.LogDebug(
                "Page {Page} rendered: {W}×{H} px",
                pageNumber,
                bitmap.Width,
                bitmap.Height
            );

            // ── الصورة الأصلية ──
            byte[] current = EncodeToPng(bitmap);

            // حد أقصى لمنع loop لا نهائي
            const int maxAttempts = 4;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                int rotation = _orientationDetector.Detect(current);

                _logger.LogDebug(
                    "Page {Page}: attempt {Attempt} -> detected rotation {Rotation}°",
                    pageNumber,
                    attempt,
                    rotation
                );

                // مستقيمة
                if (rotation == 0)
                {
                    _logger.LogInformation(
                        "Page {Page}: orientation normalized",
                        pageNumber
                    );

                    return current;
                }

                // صحح الدوران
                _logger.LogInformation(
                    "Page {Page}: rotating {Rotation}°",
                    pageNumber,
                    rotation
                );

                current = PageOrientationDetector.RotateImage(
                    current,
                    rotation
                );
            }

            _logger.LogWarning(
                "Page {Page}: max rotation attempts reached",
                pageNumber
            );

            return current;

        }, ct);
    }

    private static byte[] EncodeToPng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose() { }
}