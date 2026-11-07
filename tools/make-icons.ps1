# Generate KeepAwake tray icons (3 states: playing / normal / off) plus preview image.
# Simple monitor icon only - colour indicates state:
#   on      = red monitor   (audio/video playing, keeping awake)
#   monitor = green monitor (enabled, no playback / buffer)
#   off     = grey monitor  (disabled)
# Uses .NET Framework System.Drawing (built into Windows), no third-party dependencies.
[CmdletBinding()]
param(
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets' }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

function Add-RoundRect {
    param(
        [System.Drawing.Drawing2D.GraphicsPath]$Path,
        [single]$X, [single]$Y, [single]$W, [single]$H, [single]$R
    )
    if ($R -le 0) {
        $Path.AddRectangle([System.Drawing.RectangleF]::new($X, $Y, $W, $H))
        return
    }
    $d = $R * 2
    $Path.AddArc($X, $Y, $d, $d, 180, 90)
    $Path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $Path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $Path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $Path.CloseFigure()
}

function New-StateBitmap {
    param([int]$Size, [string]$State)

    $s = [single]$Size
    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $frame = [System.Drawing.Color]::FromArgb(255, 46, 64, 74)
    $bodyX = 0.06 * $s; $bodyY = 0.115 * $s
    $bodyW = 0.88 * $s; $bodyH = 0.60 * $s
    $bodyR = 0.10 * $s

    # monitor frame
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    Add-RoundRect -Path $p -X $bodyX -Y $bodyY -W $bodyW -H $bodyH -R $bodyR
    $br = [System.Drawing.SolidBrush]::new($frame)
    $g.FillPath($br, $p)
    $br.Dispose(); $p.Dispose()

    # screen (solid gradient - no symbols inside)
    $m = [single]([Math]::Max(0.06 * $s, 1.0))
    $sx = $bodyX + $m; $sy = $bodyY + $m
    $sw = $bodyW - 2 * $m; $sh = $bodyH - 2 * $m
    $sr = [single]([Math]::Max(0.055 * $s, 0.8))
    $sp = [System.Drawing.Drawing2D.GraphicsPath]::new()
    Add-RoundRect -Path $sp -X $sx -Y $sy -W $sw -H $sh -R $sr

    if ($State -eq 'on') {
        $c1 = [System.Drawing.Color]::FromArgb(255, 255, 128, 96)
        $c2 = [System.Drawing.Color]::FromArgb(255, 198, 38, 38)
    }
    elseif ($State -eq 'monitor') {
        $c1 = [System.Drawing.Color]::FromArgb(255, 126, 236, 178)
        $c2 = [System.Drawing.Color]::FromArgb(255, 12, 150, 92)
    }
    else {
        $c1 = [System.Drawing.Color]::FromArgb(255, 152, 162, 170)
        $c2 = [System.Drawing.Color]::FromArgb(255, 84, 94, 102)
    }
    $rect = [System.Drawing.RectangleF]::new($sx, $sy, $sw, $sh)
    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $rect, $c1, $c2, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($brush, $sp)
    $brush.Dispose()

    # stand
    $stand = [System.Drawing.SolidBrush]::new($frame)
    $neckW = 0.17 * $s; $neckH = 0.13 * $s
    $g.FillRectangle($stand, [single](($s - $neckW) / 2),
        [single]($bodyY + $bodyH - 0.01 * $s), [single]$neckW, [single]$neckH)
    $baseW = 0.46 * $s; $baseH = 0.085 * $s
    $baseY = $bodyY + $bodyH + $neckH - 0.02 * $s
    $bp = [System.Drawing.Drawing2D.GraphicsPath]::new()
    Add-RoundRect -Path $bp -X (($s - $baseW) / 2) -Y $baseY -W $baseW -H $baseH -R (0.045 * $s)
    $g.FillPath($stand, $bp)
    $bp.Dispose(); $stand.Dispose()

    $sp.Dispose()
    $g.Dispose()
    return $bmp
}

function Get-PngBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $ms = [System.IO.MemoryStream]::new()
    $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return , $bytes
}

function Write-Ico {
    param(
        [string]$Path,
        [object[]]$Entries
    )
    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$Entries.Count)

    $offset = 6 + 16 * $Entries.Count
    foreach ($e in $Entries) {
        $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
        $bw.Write([byte]$dim)
        $bw.Write([byte]$dim)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$e.Bytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $e.Bytes.Length
    }
    foreach ($e in $Entries) {
        $bw.Write($e.Bytes)
    }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($Path, $ms.ToArray())
    $bw.Dispose(); $ms.Dispose()
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$states = @(
    @{ Name = 'icon-on'; State = 'on' },
    @{ Name = 'icon-monitor'; State = 'monitor' },
    @{ Name = 'icon-off'; State = 'off' }
)
foreach ($spec in $states) {
    $entries = @()
    foreach ($sz in $sizes) {
        $bmp = New-StateBitmap -Size $sz -State $spec.State
        $entries += @{ Size = $sz; Bytes = (Get-PngBytes -Bitmap $bmp) }
        $bmp.Dispose()
    }
    $icoPath = Join-Path $OutDir ($spec.Name + '.ico')
    Write-Ico -Path $icoPath -Entries $entries
    Write-Host ("Generated {0} ({1:N1} KB)" -f $icoPath, ((Get-Item $icoPath).Length / 1KB))

    foreach ($sz in @(32, 256)) {
        $bmp = New-StateBitmap -Size $sz -State $spec.State
        $pngPath = Join-Path $OutDir ($spec.Name + '-' + $sz + '.png')
        $bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
}

# preview image: three states side by side
try {
    $preview = [System.Drawing.Bitmap]::new(420, 110, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pg = [System.Drawing.Graphics]::FromImage($preview)
    $pg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $pg.Clear([System.Drawing.Color]::FromArgb(255, 22, 26, 30))

    $onBmp = New-StateBitmap -Size 72 -State 'on'
    $monBmp = New-StateBitmap -Size 72 -State 'monitor'
    $offBmp = New-StateBitmap -Size 72 -State 'off'
    $pg.DrawImage($monBmp, 44, 19, 72, 72)
    $pg.DrawImage($onBmp, 174, 19, 72, 72)
    $pg.DrawImage($offBmp, 304, 19, 72, 72)
    $onBmp.Dispose(); $monBmp.Dispose(); $offBmp.Dispose()
    $pg.Dispose()

    $previewPath = Join-Path $OutDir 'preview.png'
    $preview.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $preview.Dispose()
    Write-Host "Generated $previewPath"
}
catch {
    Write-Warning "Preview generation failed (icons are fine): $_"
}

Write-Host 'Icon generation done.'
