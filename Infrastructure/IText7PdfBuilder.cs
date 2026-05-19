using iText.IO.Image;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using Microsoft.Extensions.Logging;
using SearchablePdfGenerator.Abstractions;
using SearchablePdfGenerator.Domain;

namespace SearchablePdfGenerator.Infrastructure;

public sealed class IText7PdfBuilder : ISearchablePdfBuilder
{
    private readonly ILogger<IText7PdfBuilder> _logger;
    private bool _disposed;

    private const float PdfDpi = 72f;

    public IText7PdfBuilder(ILogger<IText7PdfBuilder> logger)
    {
        _logger = logger;
    }

    public async Task BuildAsync(
        string inputPdfPath,
        string outputPdfPath,
        IReadOnlyList<OcrPageResult> pages,
        int renderDpi,
        CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            _logger.LogInformation(
                "Building searchable PDF: {Output} ({Pages} pages)",
                System.IO.Path.GetFileName(outputPdfPath), pages.Count
            );

            float scale = PdfDpi / renderDpi;

            using var writer = new PdfWriter(outputPdfPath);
            using var pdfDoc = new PdfDocument(writer);

            pdfDoc.GetDocumentInfo().SetCreator("SearchablePdfGenerator");
            pdfDoc.GetDocumentInfo().SetProducer("iText + TesseractOCR + PdfPig");

            // ✅ font يُنشأ مرة واحدة خارج الـ loop
            var font = PdfFontFactory.CreateFont(
                iText.IO.Font.Constants.StandardFonts.HELVETICA
            );

            foreach (var ocrPage in pages)
            {
                ct.ThrowIfCancellationRequested();

                _logger.LogDebug(
                    "Writing page {Page}/{Total} with {Words} words",
                    ocrPage.PageNumber, pages.Count, ocrPage.Words.Count
                );

                float pageW = ocrPage.PageWidthPx * scale;
                float pageH = ocrPage.PageHeightPx * scale;

                var pdfPage = pdfDoc.AddNewPage(new PageSize(pageW, pageH));
                var canvas = new PdfCanvas(pdfPage);

                // ✅ تمرير font
                WriteHiddenTextLayer(canvas, ocrPage, pageH, scale, font);

                canvas.Release();
            }

            _logger.LogInformation("Searchable PDF written successfully");

        }, ct);
    }

    public async Task BuildWithImagesAsync(
        string outputPdfPath,
        IReadOnlyList<(OcrPageResult OcrPage, byte[] ImageBytes)> pages,
        int renderDpi,
        CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            _logger.LogInformation(
                "Building searchable PDF with images: {Output}",
                System.IO.Path.GetFileName(outputPdfPath)
            );

            float scale = PdfDpi / renderDpi;

            using var writer = new PdfWriter(outputPdfPath);
            using var pdfDoc = new PdfDocument(writer);

            writer.SetCompressionLevel(CompressionConstants.BEST_COMPRESSION);
            pdfDoc.GetDocumentInfo().SetCreator("SearchablePdfGenerator");
            pdfDoc.GetDocumentInfo().SetProducer("iText + TesseractOCR + PdfPig");

            // ✅ font يُنشأ مرة واحدة خارج الـ loop
            var font = PdfFontFactory.CreateFont(
                iText.IO.Font.Constants.StandardFonts.HELVETICA
            );

            foreach (var (ocrPage, imageBytes) in pages)
            {
                ct.ThrowIfCancellationRequested();

                float pageW = ocrPage.PageWidthPx * scale;
                float pageH = ocrPage.PageHeightPx * scale;

                var pdfPage = pdfDoc.AddNewPage(new PageSize(pageW, pageH));
                var canvas = new PdfCanvas(pdfPage);

                WriteImageLayer(canvas, imageBytes, pageW, pageH);

                // ✅ تمرير font
                WriteHiddenTextLayer(canvas, ocrPage, pageH, scale, font);

                canvas.Release();

                _logger.LogDebug(
                    "Page {Page}/{Total}: {Words} words embedded",
                    ocrPage.PageNumber, pages.Count, ocrPage.Words.Count
                );
            }

            _logger.LogInformation("Done.");

        }, ct);
    }

    private static void WriteImageLayer(
        PdfCanvas canvas,
        byte[] imageBytes,
        float pageW,
        float pageH)
    {
        var imageData = ImageDataFactory.CreatePng(imageBytes);

        canvas.SaveState();
        canvas.AddImageFittedIntoRectangle(
            imageData,
            new Rectangle(0, 0, pageW, pageH),
            false
        );
        canvas.RestoreState();
    }

    private static void WriteHiddenTextLayer(
        PdfCanvas canvas,
        OcrPageResult ocrPage,
        float pageH,
        float scale,
        PdfFont font)
    {
        if (ocrPage.Words.Count == 0) return;

        canvas.SaveState();
        canvas.SetTextRenderingMode(PdfCanvasConstants.TextRenderingMode.INVISIBLE);
        canvas.BeginText();

        foreach (var word in ocrPage.Words)
        {
            var bb = word.BoundingBox;

            float x = bb.X1 * scale;
            float y = pageH - (bb.Y2 * scale);
            float ww = (bb.X2 - bb.X1) * scale;
            float wh = (bb.Y2 - bb.Y1) * scale;

            if (ww <= 0 || wh <= 0 || string.IsNullOrWhiteSpace(word.Text))
                continue;

            float fontSize = wh;
            float naturalWidth = font.GetWidth(word.Text, fontSize);

            float hScale = naturalWidth > 0
                ? (ww / naturalWidth) * 100f
                : 100f;

            hScale = Math.Clamp(hScale, 10f, 500f);

            canvas.SetFontAndSize(font, fontSize);
            canvas.SetHorizontalScaling(hScale);
            canvas.SetTextMatrix(1, 0, 0, 1, x, y);
            canvas.ShowText(word.Text);
        }

        canvas.EndText();
        canvas.SetHorizontalScaling(100f);
        canvas.RestoreState();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}