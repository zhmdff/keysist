$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"
$project = Join-Path $root "src\Keysist\Keysist.csproj"

if (Test-Path $dist) {
    Remove-Item -Recurse -Force $dist
}

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:EnableMsixTooling=true `
    -o $dist

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $dist "Keysist.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Expected $exe after publish."
    exit 1
}

# Belt-and-suspenders: ensure WinUI XAML resources landed in dist (LoadComponent needs them).
$bin = Join-Path $root "src\Keysist\bin\Release\net10.0-windows10.0.26100.0\win-x64"
if (Test-Path $bin) {
    Get-ChildItem $bin -Filter *.pri | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $dist $_.Name) -Force
    }
    Get-ChildItem $bin -Filter *.xbf | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $dist $_.Name) -Force
    }
    $viewsBin = Join-Path $bin "Views"
    $viewsDist = Join-Path $dist "Views"
    if (Test-Path $viewsBin) {
        New-Item -ItemType Directory -Force -Path $viewsDist | Out-Null
        Copy-Item (Join-Path $viewsBin "*.xbf") $viewsDist -Force
    }
}

$pri = Join-Path $dist "Keysist.pri"
$mainXbf = Join-Path $dist "MainWindow.xbf"
if (-not (Test-Path $pri) -or -not (Test-Path $mainXbf)) {
    Write-Error "Publish missing XAML resources (Keysist.pri / MainWindow.xbf). UI will fail to open."
    exit 1
}

Write-Host "Published to $dist (PRI + XBF present)"
