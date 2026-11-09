$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "Paradox.csproj"
$outputPath = Join-Path $PSScriptRoot "dist\win-x64"
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $env:ProgramFiles "dotnet\dotnet.exe" }

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw "The .NET 10 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/10.0."
}

& $dotnetPath publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $outputPath `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
    throw "Paradox publish failed with exit code $LASTEXITCODE."
}

$appPath = Join-Path $outputPath "Paradox.exe"
if (-not (Test-Path -LiteralPath $appPath)) {
    throw "Publish completed but the expected executable was not created: $appPath"
}

foreach ($fileName in @("Paradox.pdb", "Paradox.xml", "Microsoft.Web.WebView2.Core.xml", "Microsoft.Web.WebView2.Wpf.xml", "Microsoft.Web.WebView2.WinForms.xml")) {
    $documentationPath = Join-Path $outputPath $fileName
    if (Test-Path -LiteralPath $documentationPath) {
        Remove-Item -LiteralPath $documentationPath
    }
}

Write-Host "Paradox is ready: $appPath"
