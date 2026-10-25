# Build a clean, complete Windows x64 portable archive. Also used by GitHub Actions.
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.0',
    [string]$OutputDirectory = 'dist'
)

$ErrorActionPreference = 'Stop'
$destination = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
$destination = [IO.Path]::GetFullPath($destination)
[IO.Directory]::CreateDirectory($destination) | Out-Null
$archiveName = "DailyPlan-Windows-x64-v$Version.zip"
$archivePath = Join-Path $destination $archiveName
if (Test-Path -LiteralPath $archivePath) { throw "文件已存在，请指定新的版本号或输出目录：$archivePath" }

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stage = Join-Path $temporaryRoot ('DailyPlan-package-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'DailyPlan'
$requiredFiles = @(
    'DailyPlan.exe', 'DailyPlan.dll', 'DailyPlan.deps.json', 'DailyPlan.runtimeconfig.json',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'PresentationFramework.dll',
    'Assets/Fonts/PlanRounded.ttf', 'Assets/Fonts/OFL-License.txt', '使用说明.txt'
)

try {
    & (Join-Path $PSScriptRoot 'Build-Windows.ps1') -OutputDirectory $payload -Version $Version
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PORTABLE-README.txt') -Destination (Join-Path $payload '使用说明.txt')
    foreach ($relative in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $relative) -PathType Leaf)) { throw "运行包缺少：$relative" }
    }
    $files = @(Get-ChildItem -LiteralPath $payload -Recurse -File)
    foreach ($file in $files) {
        if ($file.Name -match '^(tasks\.json.*|settings\.json|\.env(?:\..*)?)$') { throw "运行包不能包含用户数据或环境文件：$($file.Name)" }
    }

    # Explicit relative entries use '/', making the ZIP portable across extraction tools.
    Add-Type -AssemblyName System.IO.Compression
    $archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $entry = [IO.Path]::GetRelativePath($stage, $file.FullName).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry,
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $archive.Dispose() }

    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $names = @($archive.Entries.FullName)
        foreach ($relative in $requiredFiles) {
            if ($names -notcontains "DailyPlan/$relative") { throw "ZIP缺少：$relative" }
        }
        if ($names.Count -ne $files.Count -or @($names | Where-Object { $_.Contains('\') -or $_.StartsWith('/') -or $_.Contains('../') }).Count) {
            throw 'ZIP文件数或相对路径校验失败。'
        }
    }
    finally { $archive.Dispose() }

    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$archivePath.sha256", "$hash  $archiveName`n", [Text.Encoding]::ASCII)
    Write-Host "完整运行包：$archivePath" -ForegroundColor Green
    Write-Host "文件数：$($files.Count)；SHA-256：$hash"
}
finally {
    # Only remove this invocation's GUID-named folder, after verifying its absolute location.
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    if ($resolvedStage.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedStage) -match '^DailyPlan-package-[0-9a-f]{32}$' -and
        (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
