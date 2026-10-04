# SCREAMER - Fix My Assets
# Mirrors Assets/ScreamerCompat/Editor/ScreamerCompatGuard.cs for the case
# where the editor cannot load any new script because the project does not
# compile. Run it with Unity open or closed; nothing is deleted, only parked:
#   - folders are renamed with a trailing ~ (Unity ignores them)
#   - scripts are renamed .cs -> .cs~
#   - packages that do not build are removed from Packages/manifest.json

$ErrorActionPreference = "Continue"
$project = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $project
Write-Host "Project: $project"

$protectedRoots = @("Assets/Scripts", "Assets/Editor", "Assets/Screamer", "Assets/ScreamerCompat")
$protectedPackages = @("com.unity.netcode.gameobjects", "com.unity.transport", "com.unity.ugui",
                       "com.unity.collections", "com.unity.burst", "com.unity.mathematics", "com.unity.modules.")
$manifest = Join-Path $project "Packages/manifest.json"
$changed = $false

function Remove-Package([string] $name) {
    if (-not (Test-Path $manifest)) { return $false }
    $json = Get-Content $manifest -Raw
    $pattern = '\s*"' + [regex]::Escape($name) + '"\s*:\s*"[^"]*"\s*,?'
    if (-not [regex]::IsMatch($json, $pattern)) { return $false }
    $json = [regex]::Replace($json, $pattern, "")
    $json = [regex]::Replace($json, ',(\s*})', '$1')
    Set-Content -Path $manifest -Value $json -NoNewline
    return $true
}

function Park-Folder([string] $from, [string] $to) {
    if (Test-Path $to) {
        Get-ChildItem $from -Force | ForEach-Object {
            $target = Join-Path $to $_.Name
            if (Test-Path $target) { Remove-Item $target -Recurse -Force }
            Move-Item $_.FullName $target
        }
        Remove-Item $from -Recurse -Force
    } else {
        Move-Item $from $to
    }
    if (Test-Path "$from.meta") { Remove-Item "$from.meta" -Force }
}

# 1. Editor version rules: the UGS building blocks need Unity 6.
$versionLine = (Get-Content "ProjectSettings/ProjectVersion.txt" | Select-String "m_EditorVersion:").Line
$major = 0
if ($versionLine -match "m_EditorVersion:\s*(\d+)") { $major = [int]$Matches[1] }
Write-Host "Editor major version: $major"

if ($major -gt 0 -and $major -lt 6000) {
    if (Test-Path "Assets/Blocks") {
        Park-Folder "Assets/Blocks" "Assets/Blocks~"
        Write-Host "PARKED Assets/Blocks -> Assets/Blocks~ (UGS building blocks need Unity 6; they return automatically there)."
        $changed = $true
    }
    if (Remove-Package "com.unity.services.multiplayer") {
        Write-Host "REMOVED package com.unity.services.multiplayer (used only by the Unity 6 building blocks)."
        $changed = $true
    }
} elseif ($major -ge 6000) {
    if ((Test-Path "Assets/Blocks~") -and -not (Test-Path "Assets/Blocks")) {
        Move-Item "Assets/Blocks~" "Assets/Blocks"
        Write-Host "RESTORED Assets/Blocks (Unity 6)."
        $changed = $true
    }
}

# 2. Whatever else failed in the last compile, according to Editor.log.
$log = Join-Path $env:LOCALAPPDATA "Unity/Editor/Editor.log"
if (Test-Path $log) {
    $lines = Get-Content $log -Tail 2500 -ErrorAction SilentlyContinue
    $files = New-Object System.Collections.Generic.HashSet[string]
    $packages = New-Object System.Collections.Generic.HashSet[string]
    foreach ($line in $lines) {
        $m = [regex]::Match($line.Trim(), '^(.+?\.cs)\(\d+,\d+\): error CS', 'IgnoreCase')
        if (-not $m.Success) { continue }
        $file = $m.Groups[1].Value -replace '\\', '/'
        $cache = $file.IndexOf("PackageCache/", [System.StringComparison]::OrdinalIgnoreCase)
        if ($cache -ge 0) {
            $rest = $file.Substring($cache + 13)
            $at = $rest.IndexOf('@')
            if ($at -gt 0) { [void]$packages.Add($rest.Substring(0, $at)) }
            continue
        }
        $assets = $file.IndexOf("Assets/", [System.StringComparison]::OrdinalIgnoreCase)
        if ($assets -lt 0) { continue }
        [void]$files.Add($file.Substring($assets))
    }

    foreach ($file in $files) {
        $isProtected = $false
        foreach ($root in $protectedRoots) { if ($file.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) { $isProtected = $true } }
        if ($isProtected) { continue }
        if (-not (Test-Path $file)) { continue }
        Move-Item $file "$file~" -Force
        if (Test-Path "$file.meta") { Remove-Item "$file.meta" -Force }
        Write-Host "PARKED $file -> $file~ (failed to compile; rename back to undo)."
        $changed = $true
    }

    foreach ($package in $packages) {
        $isProtected = $false
        foreach ($p in $protectedPackages) { if ($package.StartsWith($p, [System.StringComparison]::OrdinalIgnoreCase)) { $isProtected = $true } }
        if ($isProtected) { continue }
        if (Remove-Package $package) {
            Write-Host "REMOVED package $package (does not build on this editor)."
            $changed = $true
        }
    }
} else {
    Write-Host "Editor.log not found at $log - version rules only."
}

if (-not $changed) { Write-Host "Nothing to fix." }
