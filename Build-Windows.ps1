# Double-click "Build-Windows.bat", or run this file in PowerShell on Windows.
param(
    [string]$OutputDirectory = 'release-fluid',
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$publishDirectory = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }

$localDotnet = Join-Path $PSScriptRoot '..\tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
if (-not (Test-Path -LiteralPath $localDotnet) -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host '未找到 .NET SDK。请先安装 .NET 10 SDK：https://dotnet.microsoft.com/download/dotnet/10.0' -ForegroundColor Yellow
    exit 1
}

$sdks = & $dotnetCommand --list-sdks
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    Write-Host '需要安装 .NET 10 SDK：https://dotnet.microsoft.com/download/dotnet/10.0' -ForegroundColor Yellow
    exit 1
}

Write-Host '正在构建 DailyPlan（Windows x64 低内存目录版本）...' -ForegroundColor Cyan
& $dotnetCommand publish '.\DailyPlan.csproj' `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    '-p:PublishSingleFile=false' `
    '-p:EnableCompressionInSingleFile=false' `
    '-p:DebugType=None' `
    '-p:DebugSymbols=false' `
    "-p:Version=$Version" `
    '--output' $publishDirectory

if ($LASTEXITCODE -ne 0) { throw '构建失败，请查看上方 dotnet 错误信息。' }

$exe = Join-Path $publishDirectory 'DailyPlan.exe'
if (-not (Test-Path $exe)) { throw '编译结束，但没有找到 DailyPlan.exe。' }
Write-Host "`n构建成功！" -ForegroundColor Green
Write-Host $exe -ForegroundColor Green
Write-Host "请保留 $publishDirectory 整个文件夹，双击其中 DailyPlan.exe 启动。"
