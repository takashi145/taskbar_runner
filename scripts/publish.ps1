param([switch]$FrameworkDependent, [string]$Version)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/TaskbarRunner/TaskbarRunner.csproj'
$projectVersion = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $projectVersion) { throw "Version not found in $project." }
if ($Version) {
    $requested = $Version.TrimStart('v')
    if ($requested -ne $projectVersion) { throw "Version mismatch: requested $requested, project has $projectVersion." }
}
$output = Join-Path $PSScriptRoot "../artifacts/TaskbarRunner-v$projectVersion-win-x64"
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
dotnet publish $project -c Release -r win-x64 --self-contained $selfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Write-Host "Ready: $output/TaskbarRunner.exe"
