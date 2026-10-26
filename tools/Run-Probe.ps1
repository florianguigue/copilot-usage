param(
    [string]$Output = (Join-Path $PSScriptRoot '../diagnostic.json'),
    [Nullable[decimal]]$GitHubCycleCredits,
    [string]$GitHubCycleMonth
)
$ErrorActionPreference = 'Stop'
$probePath = Join-Path $PSScriptRoot '../artifacts/win-x64/CopilotUsage.Probe.exe'
if (-not (Test-Path -LiteralPath $probePath)) { throw 'Compiler avec tools/Publish.ps1 avant de lancer la sonde.' }
if ($null -ne $GitHubCycleCredits -and -not $GitHubCycleMonth) { throw 'Indiquer aussi -GitHubCycleMonth AAAA-MM.' }
$previousToken = $env:COPILOT_USAGE_TOKEN
try {
    if (-not $env:COPILOT_USAGE_TOKEN) {
        $secureToken = Read-Host 'Token GitHub.com compatible (saisie masquée, mémoire du processus uniquement)' -AsSecureString
        $env:COPILOT_USAGE_TOKEN = [System.Net.NetworkCredential]::new('', $secureToken).Password
    }
    $probeArgs = @('--output', $Output)
    if ($null -ne $GitHubCycleCredits) {
        $probeArgs += @('--github-cycle-credits', $GitHubCycleCredits.ToString([Globalization.CultureInfo]::InvariantCulture), '--github-cycle-month', $GitHubCycleMonth)
    }
    & $probePath @probeArgs
    $probeExit = $LASTEXITCODE
}
finally {
    if ($secureToken) { $secureToken.Dispose() }
    if ($null -eq $previousToken) { Remove-Item Env:COPILOT_USAGE_TOKEN -ErrorAction SilentlyContinue }
    else { $env:COPILOT_USAGE_TOKEN = $previousToken }
}
exit $probeExit
