$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
New-Item -ItemType Directory -Force -Path 'dist/ClearView','artifacts' | Out-Null
Add-Type -AssemblyName System.Drawing
if (!(Test-Path -LiteralPath 'src/ClearView.ico')) {
    $bitmap = New-Object System.Drawing.Bitmap 64,64
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.Clear([System.Drawing.Color]::FromArgb(25,29,32))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(139,226,194)),4
    $graphics.DrawRectangle($pen,12,16,40,32)
    $graphics.DrawLines($pen, [System.Drawing.Point[]]@((New-Object System.Drawing.Point 15,43),(New-Object System.Drawing.Point 27,29),(New-Object System.Drawing.Point 38,41),(New-Object System.Drawing.Point 44,35),(New-Object System.Drawing.Point 50,43)))
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(139,226,194))
    $graphics.FillEllipse($brush,40,23,6,6)
    $icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
    $stream = [IO.File]::Create((Join-Path $PSScriptRoot 'src/ClearView.ico'))
    $icon.Save($stream); $stream.Dispose(); $icon.Dispose(); $brush.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$sources = Get-ChildItem -LiteralPath 'src' -Filter '*.cs' | Where-Object Name -ne 'Setup.cs' | ForEach-Object FullName
$references = @('/r:System.Xaml.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll')
foreach ($assembly in @('WindowsBase','PresentationCore','PresentationFramework')) { $references += '/r:' + (Join-Path $framework ('WPF/' + $assembly + '.dll')) }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /win32manifest:src/app.manifest /win32icon:src/ClearView.ico /out:dist/ClearView/ClearView.exe @references @sources
if ($LASTEXITCODE -ne 0) { throw 'ClearView build failed.' }
Write-Output 'Built ClearView.exe'
