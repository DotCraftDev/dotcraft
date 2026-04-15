[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Section {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [ConsoleColor]$Color = [ConsoleColor]::Cyan
    )

    Write-Host ""
    Write-Host "================================" -ForegroundColor $Color
    Write-Host $Text -ForegroundColor $Color
    Write-Host "================================" -ForegroundColor $Color
}

function Resolve-LinkTarget {
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force
    if (-not $item.LinkType) {
        return $null
    }

    $target = $item.Target
    if ($target -is [System.Array]) {
        $target = $target[0]
    }

    if ([string]::IsNullOrWhiteSpace($target)) {
        return $null
    }

    try {
        return [System.IO.Path]::GetFullPath($target)
    } catch {
        return $target
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$samplesSkillsPath = Join-Path $repoRoot "samples\skills"
$cursorDirPath = Join-Path $repoRoot ".cursor"
$cursorSkillsPath = Join-Path $cursorDirPath "skills"

Write-Section -Text "DotCraft Cursor Skills Linker"
Write-Host "Repository root: $repoRoot" -ForegroundColor Gray
Write-Host "Source skills:    $samplesSkillsPath" -ForegroundColor Gray
Write-Host "Cursor skills:    $cursorSkillsPath" -ForegroundColor Gray

if (-not (Test-Path -LiteralPath $samplesSkillsPath)) {
    throw "Source skills directory not found: $samplesSkillsPath"
}

if (-not (Test-Path -LiteralPath $cursorDirPath)) {
    New-Item -ItemType Directory -Path $cursorDirPath | Out-Null
    Write-Host "Created .cursor directory." -ForegroundColor Green
}

if (Test-Path -LiteralPath $cursorSkillsPath) {
    $existingItem = Get-Item -LiteralPath $cursorSkillsPath -Force
    $existingTarget = Resolve-LinkTarget -Path $cursorSkillsPath
    $expectedTarget = [System.IO.Path]::GetFullPath($samplesSkillsPath)

    if ($existingItem.LinkType -and $existingTarget -eq $expectedTarget) {
        Write-Host ""
        Write-Host ".cursor\\skills is already linked to samples\\skills." -ForegroundColor Green
        Write-Host "Nothing to change." -ForegroundColor Green
        exit 0
    }

    if ($existingItem.LinkType) {
        Write-Host ""
        Write-Host "Replacing existing $($existingItem.LinkType) at .cursor\\skills" -ForegroundColor Yellow
        if ($existingTarget) {
            Write-Host "Current target: $existingTarget" -ForegroundColor Yellow
        }
        Remove-Item -LiteralPath $cursorSkillsPath -Force
    } else {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $backupPath = Join-Path $cursorDirPath ("skills.backup-" + $timestamp)

        Write-Host ""
        Write-Host "Existing .cursor\\skills is a normal directory. Moving it to backup:" -ForegroundColor Yellow
        Write-Host $backupPath -ForegroundColor Yellow
        Move-Item -LiteralPath $cursorSkillsPath -Destination $backupPath
    }
}

New-Item -ItemType Junction -Path $cursorSkillsPath -Target $samplesSkillsPath | Out-Null

$createdItem = Get-Item -LiteralPath $cursorSkillsPath -Force
$createdTarget = Resolve-LinkTarget -Path $cursorSkillsPath

Write-Host ""
Write-Host "Created $($createdItem.LinkType) successfully." -ForegroundColor Green
if ($createdTarget) {
    Write-Host "Target: $createdTarget" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done. Cursor will now read skills directly from samples\\skills." -ForegroundColor Green
