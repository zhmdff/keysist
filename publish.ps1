$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"

if (Test-Path $dist) {
    Remove-Item -Recurse -Force $dist
}

dotnet publish "$root\src\Keysist\Keysist.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:WindowsAppSDKSelfContained=false `
    -o $dist

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $dist "Keysist.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Expected $exe after publish."
    exit 1
}

Write-Host "Published to $dist"
