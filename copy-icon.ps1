# Copies design ICO into the project. Source folder name is built from Unicode code points (ASCII-only file).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$parent = Split-Path $root -Parent
$designFolder = -join @(
    [char]0x0434, [char]0x0438, [char]0x0437, [char]0x0430, [char]0x0439, [char]0x043D, [char]0x044B
)
$src = [System.IO.Path]::Combine($parent, $designFolder, 'ico-q.ico')
$dst = [System.IO.Path]::Combine($root, 'ReQuit', 'app.ico')
if (-not (Test-Path -LiteralPath $src)) {
    Write-Error "Not found: $src"
    exit 1
}
Copy-Item -LiteralPath $src -Destination $dst -Force
