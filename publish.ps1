$ErrorActionPreference = "Stop"

$outDir = Join-Path $PSScriptRoot "dist\portable"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

dotnet publish "$PSScriptRoot\WirelessBatteryMonitor.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -o $outDir

Write-Host "Taşınabilir tek dosya hazır: $outDir\WirelessBatteryMonitor.exe"
