namespace SearchablePdfGenerator.Domain;

// ═══════════════════════════════════════════════════════
//  Domain Models
// ═══════════════════════════════════════════════════════

/// <summary>
/// نتيجة OCR لصفحة واحدة
/// </summary>
public sealed record OcrPageResult(
    int PageNumber,
    IReadOnlyList<OcrWord> Words,
    int PageWidthPx,
    int PageHeightPx
);

/// <summary>
/// كلمة واحدة مع إحداثياتها الدقيقة على الصفحة
/// </summary>
public sealed record OcrWord(
    string Text,
    float Confidence,
    OcrBoundingBox BoundingBox
);

/// <summary>
/// الإطار المحيط بالكلمة (بالبكسل — يتم تحويله لاحقاً لإحداثيات PDF)
/// </summary>
public sealed record OcrBoundingBox(
    int X1, int Y1,
    int X2, int Y2
)
{
    public int Width  => X2 - X1;
    public int Height => Y2 - Y1;
}

/// <summary>
/// خيارات عملية OCR الكاملة
/// </summary>
public sealed class OcrOptions
{
    /// <summary>لغات OCR — مثال: "eng+ara" أو "ara"</summary>
    public string Languages { get; init; } = "eng+ara";

    /// <summary>مسار tessdata (يتم تحديده تلقائياً إذا ترك فارغاً)</summary>
    public string? TessDataPath { get; init; }

    /// <summary>DPI للتحويل من PDF إلى صورة (كلما ارتفع، دقة OCR أعلى)</summary>
    public int RenderDpi { get; init; } = 300;

    /// <summary>حد أدنى لثقة الكلمة (0–100). الكلمات دون الحد لا تُضاف</summary>
    public float MinConfidence { get; init; } = 30f;

    /// <summary>تصحيح ميلان الصفحة قبل OCR</summary>
    public bool Deskew { get; init; } = true;

    /// <summary>حفظ نسخة احتياطية من الملف الأصلي</summary>
    public bool KeepOriginalBackup { get; init; } = true;
}

/// <summary>
/// نتيجة عملية OCR الكاملة للمستند
/// </summary>
public sealed record OcrDocumentResult(
    string InputPath,
    string OutputPath,
    int TotalPages,
    int ProcessedPages,
    IReadOnlyList<OcrPageResult> Pages,
    TimeSpan Duration
)
{
    public bool IsSuccess => ProcessedPages == TotalPages;

    /// <summary>كامل النص المستخرج من المستند</summary>
    public string FullText => string.Join(
        Environment.NewLine,
        Pages.Select(p => string.Join(" ", p.Words.Select(w => w.Text)))
    );
}
