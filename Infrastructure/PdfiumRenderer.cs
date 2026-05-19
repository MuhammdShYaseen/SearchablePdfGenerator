using Microsoft.Extensions.Logging;
using SearchablePdfGenerator.Abstractions;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;

namespace SearchablePdfGenerator.Infrastructure;

public sealed class PdfPigRenderer : IPdfRenderer
{
    private readonly ILogger<PdfPigRenderer> _logger;

    public PdfPigRenderer(ILogger<PdfPigRenderer> logger)
    {
        _logger = logger;
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

            _logger.LogDebug("Rendering page {Page} at {Dpi} DPI", pageNumber, dpi);

            using var doc = PdfDocument.Open(
                pdfPath,
                SkiaRenderingParsingOptions.Instance
            );

            doc.AddSkiaPageFactory();

            // ✅ clearColor هو SKColor? وليس RGBColor
            using var bitmap = doc.GetPageAsSKBitmap(
                pageNumber,
                scale,
                SKColors.White   // SKColor وليس RGBColor
            );

            _logger.LogDebug("Page {Page}: {W}×{H} px", pageNumber, bitmap.Width, bitmap.Height);

            return EncodeToPng(bitmap);

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