param(
    [switch]$Interactive,
    [switch]$NoRestore,
    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $buildArgs = @('build', 'TaskbarRunner.slnx', '-c', 'Release', '--nologo', '-m:1')
    $testArgs = @('test', '--solution', 'TaskbarRunner.slnx', '-c', 'Release', '--no-build',
        '--report-xunit-trx', '--results-directory', (Join-Path $repoRoot 'artifacts/test-results'))
    if ($NoRestore) { $buildArgs += '--no-restore' }
    if ($ArtifactsPath) {
        $buildArgs += @('--artifacts-path', $ArtifactsPath)
        $testArgs += @('--artifacts-path', $ArtifactsPath)
    }
    $desktopTests = if ($Interactive) { '1' } else { '0' }
    $testArgs += @('--environment', "TASKBARRUNNER_DESKTOP_TESTS=$desktopTests")

    dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
finally { Pop-Location }
