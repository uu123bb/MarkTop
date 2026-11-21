# Publishes MarkTop as a single self-contained Windows executable.
#   powershell -ExecutionPolicy Bypass -File build\publish.ps1
# Output: artifacts\publish\MarkTop.exe
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$output = Join-Path $root "artifacts\publish"

dotnet publish (Join-Path $root "src\MarkTop.App\MarkTop.App.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败（退出码 $LASTEXITCODE）"
}

$exe = Join-Path $output "MarkTop.exe"
if (-not (Test-Path $exe)) {
    throw "未找到发布产物：$exe"
}

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "发布完成：$exe（$size MB）" -ForegroundColor Green
Write-Host "目录内容：" -ForegroundColor DarkGray
Get-ChildItem $output | Select-Object Name, @{Name = "MB"; Expression = { [math]::Round($_.Length / 1MB, 2) } } | Format-Table
