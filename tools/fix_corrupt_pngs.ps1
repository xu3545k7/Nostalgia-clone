# Fix corrupt PNGs by replacing with a 1x1 transparent PNG placeholder and backing up originals
$repoRoot = 'D:\Nostalgia\Nostalgia-clone'
$report = Join-Path $repoRoot 'tools\png_header_scan.txt'
$backupRoot = Join-Path $repoRoot 'tools\corrupt_image_backups'
$placeholderB64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII='

if (-not (Test-Path $report)) { Write-Output "Report not found: $report"; exit 1 }

$lines = Get-Content -Path $report -Encoding utf8
foreach ($line in $lines) {
    if ($line -and $line.StartsWith('BAD_SIG')) {
        $parts = $line -split "`t"
        $target = $parts[1]
        Write-Output "Processing: $target"
        $rel = $target.Substring( ($repoRoot + '\\').Length )
        $backupPath = Join-Path $backupRoot $rel
        $backupDir = Split-Path $backupPath -Parent
        New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
        if (Test-Path $target) {
            try { Copy-Item -Path $target -Destination $backupPath -Force } catch { Write-Output "Backup failed: $_" }
        }
        # write placeholder PNG
        $bytes = [System.Convert]::FromBase64String($placeholderB64)
        try { [System.IO.File]::WriteAllBytes($target, $bytes) } catch { Write-Output "Write failed: $_" }

        # update or create .meta to sprite
        $meta = $target + '.meta'
        if (Test-Path $meta) {
            try {
                $txt = Get-Content $meta -Raw -ErrorAction SilentlyContinue
                $txt = $txt -replace 'textureType: \d+', 'textureType: 8'
                $txt = $txt -replace 'spriteMode: \d+', 'spriteMode: 1'
                $txt = $txt -replace 'alphaIsTransparency: \d+', 'alphaIsTransparency: 1'
                $txt = $txt -replace 'spritePixelsToUnits: \d+', 'spritePixelsToUnits: 100'
                Set-Content -Path $meta -Value $txt -Encoding utf8
            } catch { Write-Output "Meta update failed: $_" }
        } else {
            $guid = [guid]::NewGuid().ToString('N')
            $metaContent = @"
fileFormatVersion: 2
guid: $guid
TextureImporter:
  serializedVersion: 13
  spriteMode: 1
  spritePixelsToUnits: 100
  alphaIsTransparency: 1
  textureType: 8
"@
            $metaContent | Out-File -FilePath $meta -Encoding utf8
        }
        Write-Output "REPLACED: $target"
    }
}

Write-Output "Done. Backups are under: $backupRoot"
