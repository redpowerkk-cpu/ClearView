$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$output = Join-Path $PSScriptRoot 'dist/ClearView'
$setupSource = Join-Path $PSScriptRoot 'src/Setup.cs'
$assemblyInfo = Join-Path $PSScriptRoot 'src/AssemblyInfo.cs'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /define:UNINSTALL /win32manifest:src/app.manifest /win32icon:src/ClearView.ico /out:dist/ClearView/Uninstall.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $setupSource $assemblyInfo
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller build failed.' }
Copy-Item -LiteralPath 'README.md','PERFORMANCE.md','LICENSE' -Destination $output
$sourceFiles = @('src','build.ps1','package.ps1','README.md','LICENSE','tests/run.ps1','.gitignore','.gitattributes','PRODUCT.md','DESIGN.md','PERFORMANCE.md')
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$sourceStream = [IO.File]::Create((Join-Path $PSScriptRoot 'artifacts/ClearView-Source-0.1.0.zip'))
$sourceArchive = New-Object System.IO.Compression.ZipArchive($sourceStream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entryPath in $sourceFiles) {
        $entry = Get-Item -LiteralPath $entryPath
        $items = if ($entry.PSIsContainer) { Get-ChildItem -LiteralPath $entry.FullName -File -Recurse } else { @($entry) }
        foreach ($item in $items) {
            $relative = $item.FullName.Substring($PSScriptRoot.Length + 1).Replace('\','/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceArchive, $item.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
} finally { $sourceArchive.Dispose(); $sourceStream.Dispose() }
Copy-Item -LiteralPath 'artifacts/ClearView-Source-0.1.0.zip' -Destination $output
$manifest = Get-ChildItem -LiteralPath $output -File -Recurse | Where-Object Name -ne 'installed-files.txt' | ForEach-Object { $_.FullName.Substring($output.Length + 1) }
$manifest | Set-Content -LiteralPath (Join-Path $output 'installed-files.txt') -Encoding UTF8
Compress-Archive -Path 'dist/ClearView/*' -DestinationPath 'artifacts/payload.zip' -Force
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /win32manifest:src/app.manifest /win32icon:src/ClearView.ico /resource:artifacts/payload.zip,payload.zip /out:artifacts/ClearView-Setup-0.1.0-x64.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $setupSource $assemblyInfo
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
Copy-Item -LiteralPath 'artifacts/payload.zip' -Destination 'artifacts/ClearView-Portable-0.1.0-x64.zip'
Get-ChildItem -LiteralPath 'artifacts' -File -Filter 'ClearView-*' | Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLower() + '  ' + (Split-Path $_.Path -Leaf) } | Set-Content -LiteralPath 'artifacts/SHA256SUMS.txt' -Encoding ASCII
Get-ChildItem -LiteralPath 'artifacts' -File -Filter 'ClearView-*' | Select-Object Name,Length
