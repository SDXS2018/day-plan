# Create the installer, portable app, update feed and full/delta packages together.
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.1.2',
    [string]$OutputDirectory = 'dist',
    [string]$PreviousReleaseDirectory = '',
    [switch]$FetchPrevious
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$destination = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }))
[IO.Directory]::CreateDirectory($destination) | Out-Null
$dotnet = if (Test-Path -LiteralPath '..\tools\dotnet\dotnet.exe') { [IO.Path]::GetFullPath('..\tools\dotnet\dotnet.exe') } else { 'dotnet' }
& $dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw '无法恢复固定版本的 vpk 工具。' }
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stage = Join-Path $temporaryRoot ('DailyPlan-package-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'publish'
$releases = Join-Path $stage 'releases'
[IO.Directory]::CreateDirectory($releases) | Out-Null

try {
    & (Join-Path $PSScriptRoot 'Build-Windows.ps1') -OutputDirectory $payload -Version $Version
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PORTABLE-README.txt') -Destination (Join-Path $payload '使用说明.txt')
    foreach ($relative in @('DailyPlan.exe', 'Velopack.dll', 'coreclr.dll', 'Assets/Fonts/PlanRounded.ttf', 'Assets/Fonts/OFL-License.txt', 'Assets/Licenses/Velopack-MIT.txt', '使用说明.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payload $relative))) { throw "缺少运行文件：$relative" }
    }
    if (@(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object { $_.Name -match '^(tasks\.json.*|settings\.json|update-check\.txt|\.env(?:\..*)?)$' }).Count) { throw '运行包中不能包含用户数据。' }

    if ($PreviousReleaseDirectory) {
        Get-ChildItem -LiteralPath $PreviousReleaseDirectory -File | Where-Object { $_.Name -match 'full\.nupkg$|^releases\.win\.json$|^RELEASES$' } | Copy-Item -Destination $releases
    }
    elseif ($FetchPrevious) {
        $latest = Invoke-RestMethod 'https://api.github.com/repos/SDXS2018/day-plan/releases/latest'
        if (@($latest.assets | Where-Object { $_.name -eq 'releases.win.json' }).Count) {
            $downloadArgs = @('tool','run','vpk','--','download','github','--repoUrl','https://github.com/SDXS2018/day-plan','--outputDir',$releases,'--channel','win','--timeout','5')
            if ($env:GITHUB_TOKEN) { $downloadArgs += @('--token',$env:GITHUB_TOKEN) }
            & $dotnet @downloadArgs
            if ($LASTEXITCODE -ne 0) { throw '无法取得上个更新包，请重试后再发布，以免丢失增量更新。' }
        }
        else { Write-Host '首次发布更新器版本：没有可用于增量生成的旧更新包。' }
    }

    # vpk generates an execution-stub filename from this title. Keep it ASCII because
    # its Windows zstd tool cannot delta-patch filenames containing Chinese characters.
    & $dotnet tool run vpk -- pack --packId 'Shang.DailyPlan' --packVersion $Version --packDir $payload `
        --mainExe 'DailyPlan.exe' --packTitle 'DailyPlan' --packAuthors 'Shang' `
        --channel 'win' --runtime 'win-x64' --icon (Join-Path $PSScriptRoot 'Assets\DailyPlan.ico') `
        --releaseNotes (Join-Path $PSScriptRoot 'RELEASE-NOTES.md') --shortcuts 'Desktop,StartMenuRoot' --outputDir $releases
    if ($LASTEXITCODE -ne 0) { throw 'Velopack 打包失败。' }

    $setup = Get-ChildItem -LiteralPath $releases -Filter '*Setup.exe' | Select-Object -First 1
    $portable = Get-ChildItem -LiteralPath $releases -Filter '*Portable.zip' | Select-Object -First 1
    $full = Get-ChildItem -LiteralPath $releases -Filter "*-$Version-full.nupkg" | Select-Object -First 1
    if (-not $setup -or -not $portable -or -not $full -or -not (Test-Path -LiteralPath (Join-Path $releases 'releases.win.json'))) { throw '缺少安装器、便携版或更新元数据。' }
    # Keep updater package names/feed unchanged; add friendlier versioned names to user downloads.
    $outputs = @(
        @{ Source=$setup.FullName; Name="DailyPlan-Windows-x64-v$Version-Setup.exe" },
        @{ Source=$portable.FullName; Name="DailyPlan-Windows-x64-v$Version-Portable.zip" },
        @{ Source=$full.FullName; Name=$full.Name },
        @{ Source=(Join-Path $releases 'releases.win.json'); Name='releases.win.json' }
    )
    foreach ($extra in @(Get-ChildItem -LiteralPath $releases -Filter "*-$Version-delta.nupkg")) { $outputs += @{ Source=$extra.FullName; Name=$extra.Name } }
    foreach ($extra in @('RELEASES','assets.win.json')) {
        if (Test-Path -LiteralPath (Join-Path $releases $extra)) { $outputs += @{ Source=(Join-Path $releases $extra); Name=$extra } }
    }

    Add-Type -AssemblyName System.IO.Compression
    $zip = [IO.Compression.ZipFile]::OpenRead($portable.FullName)
    try {
        $names = @($zip.Entries.FullName)
        $fontEntry = $names | Where-Object { $_.EndsWith('Assets/Fonts/PlanRounded.ttf') } | Select-Object -First 1
        if (-not $fontEntry) { throw '便携ZIP缺少字体。' }
        $appPrefix = $fontEntry.Substring(0, $fontEntry.Length - 'Assets/Fonts/PlanRounded.ttf'.Length)
        foreach ($required in @('DailyPlan.exe','Velopack.dll','Assets/Fonts/PlanRounded.ttf','Assets/Fonts/OFL-License.txt','使用说明.txt')) {
            if ($names -notcontains "$appPrefix$required") { throw "便携ZIP缺少：$appPrefix$required" }
        }
        if (@($names | Where-Object { $_.Contains('\') -or $_.StartsWith('/') -or $_.Contains('../') }).Count) { throw 'ZIP中存在不兼容路径。' }
    }
    finally { $zip.Dispose() }

    $checksums = @()
    foreach ($output in $outputs) {
        $target = Join-Path $destination $output.Name
        if (Test-Path -LiteralPath $target) { throw "输出已存在，请使用新目录：$target" }
        Copy-Item -LiteralPath $output.Source -Destination $target
        $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        $checksums += "$hash  $($output.Name)"
    }
    [IO.File]::WriteAllLines((Join-Path $destination 'SHA256SUMS.txt'), $checksums, [Text.Encoding]::ASCII)
    Write-Host "安装器、便携版和更新包已生成：$destination" -ForegroundColor Green
}
finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    if ($resolvedStage.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedStage) -match '^DailyPlan-package-[0-9a-f]{32}$' -and (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
