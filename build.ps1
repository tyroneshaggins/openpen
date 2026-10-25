param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    # A self-contained build is larger but runs on a machine with no .NET runtime.
    [switch]$SelfContained,

    [string]$Output = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root "dist\OpenPen"
}

$project = Join-Path $root "OpenPen.csproj"
$selfContainedValue = $SelfContained.IsPresent.ToString().ToLowerInvariant()

Write-Host "Publishing OpenPen ($Configuration, self-contained: $selfContainedValue)..." -ForegroundColor Cyan

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $Output

$exe = Join-Path $Output "OpenPen.exe"
if (-not (Test-Path $exe)) {
    throw "Publish completed but OpenPen.exe was not produced."
}

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $exe ($size MB)" -ForegroundColor Green
if (-not $SelfContained) {
    Write-Host "This build needs the .NET 8 Desktop Runtime (x64)." -ForegroundColor Yellow
    Write-Host "Use  .\build.ps1 -SelfContained  for a stand-alone build." -ForegroundColor Yellow
}
