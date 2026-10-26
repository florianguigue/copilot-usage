param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    & $Dotnet publish src/CopilotUsage.Probe/CopilotUsage.Probe.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o artifacts/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publication échouée.' }
    & ./artifacts/win-x64/CopilotUsage.Probe.exe --help
    if ($LASTEXITCODE -ne 0) { throw 'Vérification exécutable échouée.' }
    Copy-Item -LiteralPath README.md -Destination artifacts/win-x64/README.md -Force
    Copy-Item -LiteralPath docs/SOURCES.md -Destination artifacts/win-x64/SOURCES.md -Force
    Copy-Item -LiteralPath docs/VALIDATION.md -Destination artifacts/win-x64/VALIDATION.md -Force
    $runtimeLicense = 'src/CopilotUsage.Core/obj/Release/net10.0/copilot-cli/1.0.103/win32-x64/LICENSE.md'
    if (-not (Test-Path -LiteralPath $runtimeLicense)) { throw 'Licence du runtime Copilot manquante.' }
    Copy-Item -LiteralPath $runtimeLicense -Destination artifacts/win-x64/COPILOT-RUNTIME-LICENSE.md -Force
    Copy-Item -LiteralPath docs/SDK-LICENSE.txt -Destination artifacts/win-x64/SDK-LICENSE.txt -Force
    Get-FileHash -Algorithm SHA256 artifacts/win-x64/CopilotUsage.Probe.exe | Format-List
}
finally { Pop-Location }
