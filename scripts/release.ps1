<# Dispatch the gated GitHub Actions release workflow for the current main commit. #>
param([switch]$Draft)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
if (git -C $root status --porcelain) { throw 'Commit and push the reviewed changes first' }
$local = git -C $root rev-parse HEAD
$remote = ((git -C $root ls-remote origin refs/heads/main) -split '\s+')[0]
if ($LASTEXITCODE -or $local -ne $remote) { throw 'HEAD must match the published main branch' }
$origin = git -C $root remote get-url origin
if ($LASTEXITCODE) { throw 'Configure origin before releasing' }
gh workflow run release.yml --repo $origin --ref main -f ('draft=' + $Draft.IsPresent.ToString().ToLowerInvariant())
if ($LASTEXITCODE) { throw 'Release workflow dispatch failed' }
Write-Host 'Release dispatched. Review the build and the release assets on GitHub.'
