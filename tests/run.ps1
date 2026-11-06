param([switch]$Ui, [switch]$Generate)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$player = Join-Path $projectRoot 'dist/ClearView/ClearView.exe'
$media = Join-Path $PSScriptRoot 'media'
$output = Join-Path $PSScriptRoot 'output'
if (!(Test-Path -LiteralPath $player)) { throw 'Run build.ps1 first.' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
if ($Generate -or !(Test-Path -LiteralPath (Join-Path $media '08-large-96mp.bmp'))) {
    $process = Start-Process -FilePath $player -ArgumentList @('--make-fixtures', ('"' + $media + '"')) -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Fixture generation timed out.' }
    if ($process.ExitCode -ne 0) { throw 'Fixture generation failed. See tests/media/generation-error.txt.' }
}
$reportPath = Join-Path $output 'engine.json'
$process = Start-Process -FilePath $player -ArgumentList @('--test', ('"' + $media + '"'), ('"' + $reportPath + '"')) -WindowStyle Hidden -PassThru
if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Image checks timed out.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if ($process.ExitCode -ne 0 -or $report.failures -ne 0) { $report.tests | Where-Object { !$_.passed } | Format-List; throw 'Image checks failed.' }
Write-Output ('Image checks passed: ' + $report.tests.Count)
if ($Ui) {
    $uiOutput = Join-Path $output ('ui-' + [Guid]::NewGuid().ToString('N'))
    $process = Start-Process -FilePath $player -ArgumentList @('--ui-test', ('"' + $media + '"'), ('"' + $uiOutput + '"')) -PassThru
    if (!$process.WaitForExit(90000)) { $process.Kill(); throw 'UI checks timed out.' }
    $report = Get-Content -LiteralPath (Join-Path $uiOutput 'results.json') -Raw | ConvertFrom-Json
    if ($report.failures -ne 0) { $report.tests | Where-Object { !$_.passed } | Format-List; throw 'UI checks failed.' }
    Write-Output ('UI checks passed: ' + $report.tests.Count)
    Write-Output ('UI output: ' + $uiOutput)
}
