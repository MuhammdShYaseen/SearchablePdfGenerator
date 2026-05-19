# SearchablePdfGenerator — Pure C# NuGet Solution

## لماذا هذا الحل أفضل من الحل المعتمد على OCRmyPDF؟

| المعيار | OCRmyPDF (CLI) | هذا الحل (Pure NuGet) |
|---|---|---|
| التبعيات | Python + Ghostscript + Tesseract مثبتة | **لا شيء** |
| Deployment | يجب إعداد PATH على كل جهاز | `dotnet publish` فقط |
| Docker | Dockerfile معقد | `FROM mcr.microsoft.com/dotnet/runtime` |
| Cross-platform | صعب | **Windows / Linux / macOS / ARM** |
| NuGet packaging | مستحيل | ✓ |
| Integration في ASP.NET | Process.Start هش | **DI مباشر** |
| Error Handling | Exit codes فقط | Exceptions طبيعية |
| Unit Testing | صعب | **قابل للـ mock** |

---

## البنية الداخلية للـ PDF الناتج

```
صفحة PDF واحدة
│
├── Layer 1: صورة الصفحة الأصلية (مرئية)
│     └── PNG مضغوط → JPEG داخل PDF
│
└── Layer 2: Hidden Text Layer (مخفية — للبحث فقط)
      ├── كل كلمة لها TextMatrix بإحداثيات دقيقة
      ├── TextRenderingMode = INVISIBLE
      └── تغطي نفس موضع الكلمة في الصورة تماماً
```

---

## Pipeline العملية

```
Input PDF
    │
    ▼
PDFiumSharp (PDF → PNG)
    │  300 DPI per page
    │  نفس محرك Chrome لعرض PDF
    ▼
Tesseract 5.x (OCR)
    │  يعيد: نص + إحداثيات كل كلمة
    │  يدعم: eng + ara + 100+ لغة أخرى
    ▼
iText7 (PDF Builder)
    │  يدمج الصورة + النص المخفي
    │  في صفحة PDF واحدة
    ▼
Output Searchable PDF
```

---

## NuGet Packages

```xml
<!-- OCR Engine — tesseract.dll مدمجة، لا تثبيت خارجي -->
<PackageReference Include="Tesseract" Version="5.2.0" />

<!-- PDF → Image — PDFium (محرك Chrome)، لا يحتاج Ghostscript -->
<PackageReference Include="PDFiumSharp" Version="0.3.3" />

<!-- PDF Creation + Hidden Text Layer -->
<PackageReference Include="itext7" Version="8.0.5" />
<PackageReference Include="itext7.bouncy-castle-adapter" Version="8.0.5" />

<!-- Image Processing -->
<PackageReference Include="SixLabors.ImageSharp" Version="3.1.4" />
```

---

## الإعداد الأولي (مرة واحدة فقط)

```powershell
# تحميل ملفات لغات Tesseract (eng + ara)
.\download-tessdata.ps1
```

---

## الاستخدام

### Console App

```bash
dotnet run -- "C:\Docs\invoice.pdf"
```

### ASP.NET Core / Worker Service

```csharp
// Program.cs
builder.Services.AddSearchablePdfGenerator(opts =>
{
    opts.TessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
});

// في أي Service أو Controller
public class DocumentService
{
    private readonly ISearchablePdfService _pdfService;

    public DocumentService(ISearchablePdfService pdfService)
        => _pdfService = pdfService;

    public async Task<string> MakeSearchableAsync(string inputPath)
    {
        var outputPath = inputPath.Replace(".pdf", ".searchable.pdf");

        var result = await _pdfService.ProcessAsync(
            inputPath,
            outputPath,
            new OcrOptions { Languages = "eng+ara", RenderDpi = 300 }
        );

        return result.FullText; // النص الكامل المستخرج — للتخزين في DB
    }
}
```

---

## هيكل المشروع

```
SearchablePdfGenerator/
├── Domain/
│   └── Models.cs                  # OcrPageResult, OcrWord, OcrOptions
├── Abstractions/
│   └── Interfaces.cs              # IPdfRenderer, IOcrEngine, ISearchablePdfBuilder
├── Infrastructure/
│   ├── PdfiumRenderer.cs          # PDF → PNG (بدون Ghostscript)
│   ├── TesseractOcrEngine.cs      # OCR (بدون CLI)
│   ├── IText7PdfBuilder.cs        # PDF مع Hidden Text Layer
│   └── SearchablePdfService.cs    # Orchestration
├── Extensions/
│   └── ServiceCollectionExtensions.cs  # DI registration
├── tessdata/
│   ├── eng.traineddata            # (يُحمَّل بـ download-tessdata.ps1)
│   └── ara.traineddata            # (يُحمَّل بـ download-tessdata.ps1)
├── download-tessdata.ps1          # سكريبت تحميل اللغات
└── Program.cs                     # Entry point
```

---

## ملاحظة: tessdata في الإنتاج

ملفات `.traineddata` هي البيانات التدريبية فقط (ليست برامج).  
في الإنتاج يمكن:
1. تضمينها كـ Embedded Resource في المشروع
2. أو تحميلها من blob storage عند أول تشغيل
3. أو تضمينها في Docker image

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app/publish .
COPY tessdata/ ./tessdata/
ENTRYPOINT ["dotnet", "SearchablePdfGenerator.dll"]
```
