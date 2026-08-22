param(
  [switch]$NoFetch
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$repos = @(
  @{ Name = 'v2rayN'; Path = Join-Path $root 'v2rayN'; Remote = 'https://github.com/2dust/v2rayN.git' },
  @{ Name = 'v2rayNG'; Path = Join-Path $root 'v2rayNG'; Remote = 'https://github.com/2dust/v2rayNG.git' }
)
foreach ($repo in $repos) {
  if (-not (Test-Path (Join-Path $repo.Path '.git'))) {
    git clone --depth 1 $repo.Remote $repo.Path
  } elseif (-not $NoFetch) {
    git -C $repo.Path fetch --depth 1 origin HEAD
    git -C $repo.Path reset --hard FETCH_HEAD
  }
}
$record = foreach ($repo in $repos) {
  [pscustomobject]@{
    repository = $repo.Name
    remote = $repo.Remote
    commit = (git -C $repo.Path rev-parse HEAD).Trim()
    date = (git -C $repo.Path show -s --format=%cI HEAD).Trim()
  }
}
$record | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $root 'UPSTREAM.json')
$record | Format-Table -AutoSize
