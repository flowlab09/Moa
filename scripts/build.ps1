# Builds dist\Moa.exe and dist\Moa-<version>-windows-x86_64-setup.exe.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1
# Needs Windows with .NET Framework 4.8 (csc.exe) and internet access on the first run.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$src = "$root\src"; $assets = "$root\assets"; $dist = "$root\dist"; $build = "$root\build"; $sdk = "$build\webview2-sdk"
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sdkVersion = '1.0.4314.35'
New-Item -ItemType Directory -Force $dist, $build, $sdk | Out-Null

# WebView2 SDK (not committed; restored from NuGet at a pinned version)
if (-not (Test-Path "$sdk\Microsoft.Web.WebView2.Core.dll")) {
    $ProgressPreference = 'SilentlyContinue'
    [Net.ServicePointManager]::SecurityProtocol = 'Tls12'
    $zip = "$build\webview2.zip"
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$sdkVersion/microsoft.web.webview2.$sdkVersion.nupkg" -OutFile $zip -UseBasicParsing
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '^(lib/net4[^/]*/Microsoft\.Web\.WebView2\.(Core|WinForms)\.dll|runtimes/win-x64/native/WebView2Loader\.dll)$') {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $sdk $entry.Name), $true)
        }
    }
    $archive.Dispose()
    Remove-Item $zip
    foreach ($dll in Get-ChildItem $sdk -Filter *.dll) {
        if ((Get-AuthenticodeSignature $dll.FullName).Status -ne 'Valid') { throw "Invalid signature: $($dll.Name)" }
    }
}

# Icon, generated from the logo
$ico = "$build\moa.ico"
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    $source = [Drawing.Image]::FromFile("$assets\moa-logo.png")
    $sizes = 16, 24, 32, 48, 64, 128, 256
    $images = foreach ($s in $sizes) {
        $bmp = New-Object Drawing.Bitmap $s, $s
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'
        $g.DrawImage($source, 0, 0, $s, $s); $g.Dispose()
        $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        , $ms.ToArray()
    }
    $source.Dispose()
    $o = New-Object IO.MemoryStream; $b = New-Object IO.BinaryWriter $o
    $b.Write([uint16]0); $b.Write([uint16]1); $b.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $n = $images[$i].Length; $v = if ($s -ge 256) { 0 } else { $s }
        $b.Write([byte]$v); $b.Write([byte]$v); $b.Write([byte]0); $b.Write([byte]0); $b.Write([uint16]1); $b.Write([uint16]32); $b.Write([uint32]$n); $b.Write([uint32]$offset); $offset += $n
    }
    foreach ($img in $images) { $b.Write([byte[]]$img) }
    $b.Flush(); [IO.File]::WriteAllBytes($ico, $o.ToArray())
}

$common = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', "/win32icon:$ico", "/win32manifest:$src\moa-app.manifest", '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Core.dll')

# The version lives in MoaSetup.cs; the app gets it from a generated file
$version = [regex]::Match((Get-Content "$src\MoaSetup.cs" -Raw -Encoding UTF8), 'const string Version = "([\d.]+)"').Groups[1].Value
if (-not $version) { throw 'Version not found in MoaSetup.cs' }
$versionFile = "$build\MoaVersion.cs"
Set-Content $versionFile "internal static class MoaVersion { internal const string Value = `"$version`"; }" -Encoding UTF8

# App
$app = "$dist\Moa.exe"
& $csc @common "/out:$app" "/resource:$root\app\moa-board.html,moa.html" "/resource:$sdk\WebView2Loader.dll,WebView2Loader.dll" "/resource:$sdk\Microsoft.Web.WebView2.Core.dll,Microsoft.Web.WebView2.Core.dll" "/resource:$sdk\Microsoft.Web.WebView2.WinForms.dll,Microsoft.Web.WebView2.WinForms.dll" "/r:$sdk\Microsoft.Web.WebView2.Core.dll" "/r:$sdk\Microsoft.Web.WebView2.WinForms.dll" '/r:System.Web.Extensions.dll' "$src\MoaApp.cs" $versionFile
if ($LASTEXITCODE -ne 0) { throw "App build failed: $LASTEXITCODE" }

# Uninstaller, then the installer that carries both
$un = "$build\uninstall.exe"
& $csc @common '/define:UNINSTALLER' "/out:$un" '/r:Microsoft.CSharp.dll' "$src\MoaSetup.cs"
if ($LASTEXITCODE -ne 0) { throw "Uninstaller build failed: $LASTEXITCODE" }
$setup = "$dist\Moa-$version-windows-x86_64-setup.exe"
& $csc @common "/out:$setup" "/resource:$app,Moa.exe" "/resource:$un,uninstall.exe" "/resource:$assets\moa-logo.png,moa-logo.png" '/r:Microsoft.CSharp.dll' "$src\MoaSetup.cs"
if ($LASTEXITCODE -ne 0) { throw "Setup build failed: $LASTEXITCODE" }
# The screen file is uploaded with each release; installed copies pick it up on their own
Copy-Item "$root\app\moa-board.html" "$dist\moa-$version.html"
Write-Output $setup
