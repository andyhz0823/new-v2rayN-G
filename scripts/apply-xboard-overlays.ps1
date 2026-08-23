$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$applyArgs = @('--ignore-space-change', '--ignore-whitespace', '--whitespace=nowarn')
& git -C (Join-Path $root 'v2rayN') apply @applyArgs (Join-Path $root 'patches/v2rayN-xboard.patch')
& git -C (Join-Path $root 'v2rayNG') apply @applyArgs (Join-Path $root 'patches/v2rayNG-xboard.patch')