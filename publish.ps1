$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet publish "$root\src\Keysist\Keysist.csproj" -c Release -r win-x64 --self-contained false -o "$root\dist"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published to $root\dist"
