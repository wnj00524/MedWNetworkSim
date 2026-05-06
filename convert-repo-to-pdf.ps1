[CmdletBinding()]
param(
    [string]$RepoRoot = $PSScriptRoot,
    [string]$ArtifactsRoot = (Join-Path $PSScriptRoot "artifacts"),
    [string]$LibreOfficePath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-LibreOfficePath {
    param(
        [string]$RequestedPath
    )

    if ($RequestedPath) {
        if (-not (Test-Path -LiteralPath $RequestedPath)) {
            throw "LibreOffice executable not found at '$RequestedPath'."
        }

        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }

    $fromPath = Get-Command soffice.exe -ErrorAction SilentlyContinue
    if ($fromPath) {
        return $fromPath.Source
    }

    $candidates = @(
        "C:\Program Files\LibreOffice\program\soffice.exe",
        "C:\Program Files (x86)\LibreOffice\program\soffice.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Unable to find soffice.exe. Install LibreOffice or pass -LibreOfficePath."
}

function Test-IsConvertibleNatively {
    param(
        [string]$Extension
    )

    $nativeExtensions = @(
        ".csv", ".doc", ".docm", ".docx", ".dot", ".dotx",
        ".htm", ".html",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".svg", ".tif", ".tiff", ".webp",
        ".odp", ".ods", ".odt",
        ".ppt", ".pptm", ".pptx",
        ".rtf",
        ".tsv", ".txt",
        ".xls", ".xlsm", ".xlsx",
        ".xml"
    )

    return $nativeExtensions -contains $Extension.ToLowerInvariant()
}

function Test-IsTextFile {
    param(
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $bufferSize = [Math]::Min([int]$stream.Length, 4096)
        if ($bufferSize -eq 0) {
            return $true
        }

        $buffer = New-Object byte[] $bufferSize
        [void]$stream.Read($buffer, 0, $buffer.Length)

        foreach ($byte in $buffer) {
            if ($byte -eq 0) {
                return $false
            }
        }

        return $true
    }
    finally {
        $stream.Dispose()
    }
}

function Invoke-LibreOfficeConversion {
    param(
        [string]$SofficePath,
        [string]$InputPath,
        [string]$OutputDirectory
    )

    $arguments = @(
        "--headless",
        "--convert-to", "pdf",
        "--outdir", $OutputDirectory,
        $InputPath
    )

    & $SofficePath @arguments | Out-Null

    $expectedPdf = Join-Path $OutputDirectory ([System.IO.Path]::GetFileNameWithoutExtension($InputPath) + ".pdf")
    if (-not (Test-Path -LiteralPath $expectedPdf)) {
        throw "LibreOffice did not produce '$expectedPdf' for '$InputPath'."
    }

    return $expectedPdf
}

function New-StagedTextCopy {
    param(
        [string]$SourcePath,
        [string]$RelativePath,
        [string]$StagingRoot
    )

    $safeRelativeDirectory = [System.IO.Path]::GetDirectoryName($RelativePath)
    $targetDirectory = if ([string]::IsNullOrWhiteSpace($safeRelativeDirectory)) {
        $StagingRoot
    }
    else {
        Join-Path $StagingRoot $safeRelativeDirectory
    }

    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null

    $leafName = [System.IO.Path]::GetFileName($RelativePath) + ".txt"
    $targetPath = Join-Path $targetDirectory $leafName

    [System.IO.File]::Copy($SourcePath, $targetPath, $true)
    return $targetPath
}

function Get-RelativePathCompat {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    $baseFullPath = [System.IO.Path]::GetFullPath($BasePath)
    if (-not $baseFullPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $baseFullPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $baseUri = [System.Uri]::new($baseFullPath)
    $targetUri = [System.Uri]::new([System.IO.Path]::GetFullPath($TargetPath))
    $relativeUri = $baseUri.MakeRelativeUri($targetUri)
    $relativePath = [System.Uri]::UnescapeDataString($relativeUri.ToString())

    return $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

$repoRootResolved = (Resolve-Path -LiteralPath $RepoRoot).Path
$artifactsRootResolved = $ArtifactsRoot
if (-not [System.IO.Path]::IsPathRooted($artifactsRootResolved)) {
    $artifactsRootResolved = Join-Path $repoRootResolved $artifactsRootResolved
}
$artifactsRootResolved = [System.IO.Path]::GetFullPath($artifactsRootResolved)

$sofficePath = Resolve-LibreOfficePath -RequestedPath $LibreOfficePath

New-Item -ItemType Directory -Path $artifactsRootResolved -Force | Out-Null

$pdfOutputRoot = Join-Path $artifactsRootResolved "pdf"
New-Item -ItemType Directory -Path $pdfOutputRoot -Force | Out-Null

$excludedDirectoryNames = @(".git", ".vs", "artifacts", "bin", "obj")
$files = Get-ChildItem -LiteralPath $repoRootResolved -Recurse -File | Where-Object {
    $segments = $_.FullName.Substring($repoRootResolved.Length).TrimStart('\', '/').Split([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    foreach ($segment in $segments) {
        if ($excludedDirectoryNames -contains $segment) {
            return $false
        }
    }

    return $true
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("libreoffice-pdf-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

$convertedCount = 0
$skippedCount = 0
$failedCount = 0
$skippedFiles = [System.Collections.Generic.List[string]]::new()
$failedFiles = [System.Collections.Generic.List[string]]::new()

try {
    foreach ($file in $files) {
        $relativePath = Get-RelativePathCompat -BasePath $repoRootResolved -TargetPath $file.FullName
        $relativeDirectory = [System.IO.Path]::GetDirectoryName($relativePath)
        $destinationDirectory = if ([string]::IsNullOrWhiteSpace($relativeDirectory)) {
            $pdfOutputRoot
        }
        else {
            Join-Path $pdfOutputRoot $relativeDirectory
        }

        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

        $destinationPdf = Join-Path $destinationDirectory ($file.Name + ".pdf")

        try {
            $inputForConversion = $file.FullName
            if (-not (Test-IsConvertibleNatively -Extension $file.Extension)) {
                if (-not (Test-IsTextFile -Path $file.FullName)) {
                    $skippedCount++
                    $skippedFiles.Add($relativePath)
                    Write-Warning "Skipping unsupported binary file '$relativePath'."
                    continue
                }

                $inputForConversion = New-StagedTextCopy -SourcePath $file.FullName -RelativePath $relativePath -StagingRoot $tempRoot
            }

            $convertedPdf = Invoke-LibreOfficeConversion -SofficePath $sofficePath -InputPath $inputForConversion -OutputDirectory $destinationDirectory
            Move-Item -LiteralPath $convertedPdf -Destination $destinationPdf -Force
            $convertedCount++
            Write-Host "Converted '$relativePath' -> '$destinationPdf'"
        }
        catch {
            $failedCount++
            $failedFiles.Add("$relativePath :: $($_.Exception.Message)")
            Write-Warning "Failed to convert '$relativePath': $($_.Exception.Message)"
        }
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

Write-Host ""
Write-Host "Conversion complete."
Write-Host "Converted: $convertedCount"
Write-Host "Skipped:   $skippedCount"
Write-Host "Failed:    $failedCount"

if ($skippedFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "Skipped files:"
    $skippedFiles | ForEach-Object { Write-Host "  $_" }
}

if ($failedFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "Failed files:"
    $failedFiles | ForEach-Object { Write-Host "  $_" }
    exit 1
}
