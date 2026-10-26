param([string]$Dotnet = 'dotnet', [switch]$WithSimulation, [switch]$SdkSmoke, [string]$SimulationUrl = 'http://127.0.0.1:4917')
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    & $Dotnet build CopilotUsage.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Compilation échouée.' }
    $testArgs = @()
    if ($WithSimulation) { $testArgs = @('--simulation', $SimulationUrl) }
    if ($SdkSmoke) { $testArgs += '--sdk-smoke' }
    & $Dotnet run --project tests/CopilotUsage.Tests -c Release --no-build -- @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Tests échoués.' }
}
finally { Pop-Location }
