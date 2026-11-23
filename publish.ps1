$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"
$appDir = Join-Path $dist "app"
$project = Join-Path $root "src\Keysist\Keysist.csproj"
$launcherProject = Join-Path $root "src\Keysist.Launcher\Keysist.Launcher.csproj"

if (Test-Path $dist) {
    Remove-Item -Recurse -Force $dist
}
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:EnableMsixTooling=true `
    -o $appDir

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $appDir "Keysist.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Expected $exe after publish."
    exit 1
}

# Ensure WinUI XAML resources are present next to the app exe.
$bin = Join-Path $root "src\Keysist\bin\Release\net8.0-windows10.0.26100.0\win-x64"
if (Test-Path $bin) {
    Get-ChildItem $bin -Filter *.pri | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $appDir $_.Name) -Force
    }
    Get-ChildItem $bin -Filter *.xbf | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $appDir $_.Name) -Force
    }
    $viewsBin = Join-Path $bin "Views"
    $viewsApp = Join-Path $appDir "Views"
    if (Test-Path $viewsBin) {
        New-Item -ItemType Directory -Force -Path $viewsApp | Out-Null
        Copy-Item (Join-Path $viewsBin "*.xbf") $viewsApp -Force
    }
}

$pri = Join-Path $appDir "Keysist.pri"
$mainXbf = Join-Path $appDir "MainWindow.xbf"
if (-not (Test-Path $pri) -or -not (Test-Path $mainXbf)) {
    Write-Error "Publish missing XAML resources (Keysist.pri / MainWindow.xbf). UI will fail to open."
    exit 1
}

# Root launcher stub: dist\Keysist.exe -> app\Keysist.exe
$launcherOut = Join-Path $dist "_launcher-build"
dotnet publish $launcherProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -o $launcherOut

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item (Join-Path $launcherOut "Keysist.exe") (Join-Path $dist "Keysist.exe") -Force
Remove-Item -Recurse -Force $launcherOut

# Desktop-style shortcut next to the launcher (relative target via WorkingDirectory)
$shortcutPath = Join-Path $dist "Keysist.lnk"
$wsh = New-Object -ComObject WScript.Shell
$sc = $wsh.CreateShortcut($shortcutPath)
$sc.TargetPath = Join-Path $dist "Keysist.exe"
$sc.WorkingDirectory = $dist
$sc.Description = "Keysist"
$sc.IconLocation = "$(Join-Path $dist 'Keysist.exe'),0"
$sc.Save()

$readme = Join-Path $dist "README.txt"
@(
    'Keysist'
    '======='
    ''
    'Start:  Keysist.exe   or   Keysist.lnk'
    'App:    app\Keysist.exe   (main app + dependencies)'
    ''
    'Tray: Open Keysist / New Mapping / Register Device / Exit'
    'Closing the window hides to tray; use tray Exit to quit.'
    ''
    'Settings (gear): Start with Windows, etc.'
    ''
    'Requires: .NET 8 runtime.'
    'Windows App SDK is bundled under app\.'
) | Set-Content -Path $readme -Encoding utf8

Write-Host "Published:"
Write-Host "  $dist\Keysist.exe"
Write-Host "  $appDir\Keysist.exe (+ dependencies)"
