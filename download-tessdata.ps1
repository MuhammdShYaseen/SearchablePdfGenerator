#!/usr/bin/env pwsh
# ════════════════════════════════════════════════════════════════
#  تحميل ملفات tessdata للغات المطلوبة
#  شغّل هذا السكريبت مرة واحدة بعد إنشاء المشروع
# ════════════════════════════════════════════════════════════════

$tessDataDir = Join-Path $PSScriptRoot "tessdata"

if (-not (Test-Path $tessDataDir)) {
    New-Item -ItemType Directory -Path $tessDataDir | Out-Null
}

Write-Host "Downloading Tesseract traineddata files..." -ForegroundColor Cyan

$baseUrl = "https://github.com/tesseract-ocr/tessdata_best/raw/main"

$languages = @(
    @{ Code = "eng"; Name = "English" },
    @{ Code = "ara"; Name = "Arabic"  }
)

foreach ($lang in $languages) {
    $destFile = Join-Path $tessDataDir "$($lang.Code).traineddata"

    if (Test-Path $destFile) {
        Write-Host "  ✓ $($lang.Name) already exists, skipping." -ForegroundColor Green
        continue
    }

    $url = "$baseUrl/$($lang.Code).traineddata"
    Write-Host "  ↓ Downloading $($lang.Name) ($($lang.Code).traineddata)..." -ForegroundColor Yellow

    try {
        Invoke-WebRequest -Uri $url -OutFile $destFile -UseBasicParsing
        Write-Host "  ✓ $($lang.Name) downloaded." -ForegroundColor Green
    }
    catch {
        Write-Host "  ✗ Failed: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "tessdata directory: $tessDataDir" -ForegroundColor Cyan
Write-Host "Done. You can now run the application." -ForegroundColor Green
