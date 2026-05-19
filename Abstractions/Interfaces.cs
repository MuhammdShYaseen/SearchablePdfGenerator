using SearchablePdfGenerator.Domain;

namespace SearchablePdfGenerator.Abstractions;

// ═══════════════════════════════════════════════════════
//  Service Interfaces — Clean Architecture
// ═══════════════════════════════════════════════════════

/// <summary>
/// يحوّل صفحات PDF إلى صور جاهزة للـ OCR
/// </summary>
public interface IPdfRenderer : IDisposable
{
    /// <summary>عدد صفحات الملف</summary>
    int GetPageCount(string pdfPath);

    /// <summary>
    /// يُحوّل صفحة واحدة من PDF إلى byte[] (PNG/TIFF)
    /// </summary>
    Task<byte[]> RenderPageToImageAsync(
        string pdfPath,
        int pageIndex,
        int dpi,
        CancellationToken ct = default
    );
}

/// <summary>
/// يُشغّل OCR على صورة ويعيد الكلمات مع إحداثياتها
/// </summary>
public interface IOcrEngine : IDisposable
{
    /// <summary>
    /// يستقبل byte[] للصورة ويُعيد قائمة الكلمات المُتعرَّف عليها
    /// </summary>
    Task<IReadOnlyList<OcrWord>> RecognizeAsync(
        byte[] imageBytes,
        CancellationToken ct = default
    );
}

/// <summary>
/// يُنشئ PDF قابل للبحث من الصور ونتائج OCR
/// </summary>
public interface ISearchablePdfBuilder : IDisposable
{
    /// <summary>
    /// يبني PDF يحتوي:
    ///   - الصورة الأصلية كـ background layer
    ///   - طبقة نص مخفية (hidden text) للبحث والنسخ
    /// </summary>
    Task BuildAsync(
        string inputPdfPath,
        string outputPdfPath,
        IReadOnlyList<OcrPageResult> pages,
        int renderDpi,
        CancellationToken ct = default
    );
}

/// <summary>
/// الواجهة الرئيسية — تُنسّق العملية كاملاً
/// </summary>
public interface ISearchablePdfService
{
    Task<OcrDocumentResult> ProcessAsync(
        string inputPdfPath,
        string outputPdfPath,
        OcrOptions? options = null,
        IProgress<OcrProgress>? progress = null,
        CancellationToken ct = default
    );
}

/// <summary>تقدم العملية — للـ UI أو logging</summary>
public sealed record OcrProgress(
    int CurrentPage,
    int TotalPages,
    string Status
)
{
    public double Percentage => TotalPages == 0 ? 0 : (CurrentPage / (double)TotalPages) * 100;
}
