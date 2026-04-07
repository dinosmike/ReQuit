# Optional design ICO sync.
# If design icon is not present outside the repo, do nothing (no warnings) because app.ico is already part of the project.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$parent = Split-Path $root -Parent
$designFolder = -join @(
    [char]0x0434, [char]0x0438, [char]0x0437, [char]0x0430, [char]0x0439, [char]0x043D, [char]0x044B
)
$src = [System.IO.Path]::Combine($parent, $designFolder, 'ico-q.ico')
$dst = [System.IO.Path]::Combine($root, 'ReQuit', 'app.ico')
if (-not (Test-Path -LiteralPath $src)) {
    exit 0
}
Copy-Item -LiteralPath $src -Destination $dst -Force
