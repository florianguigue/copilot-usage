param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    & $Dotnet publish src/CopilotUsage.Tray/CopilotUsage.Tray.csproj -c Release -r win-x64 --self-contained true -p:CopilotSkipCliDownload=true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o artifacts/tray-win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publication de l’application Windows échouée.' }
    Copy-Item -LiteralPath docs/TRAY.md -Destination artifacts/tray-win-x64/README.md -Force
    Copy-Item -LiteralPath docs/SDK-LICENSE.txt -Destination artifacts/tray-win-x64/SDK-LICENSE.txt -Force
    Get-FileHash -Algorithm SHA256 artifacts/tray-win-x64/CopilotUsage.Tray.exe | Format-List
}
finally { Pop-Location }
