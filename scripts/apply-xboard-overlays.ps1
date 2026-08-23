$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
& git -C (Join-Path $root 'v2rayN') apply --whitespace=nowarn (Join-Path $root 'patches/v2rayN-xboard.patch')
& git -C (Join-Path $root 'v2rayNG') apply --whitespace=nowarn (Join-Path $root 'patches/v2rayNG-xboard.patch')
