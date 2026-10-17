# 生成安装包用的品牌素材，几何与 src/WhichToPaste/UI/AppIcon.cs 保持一致：
#   - assets/WhichToPaste.ico  多尺寸 ICO（Setup.exe 图标、快捷方式、exe 资源）
#   - assets/wizard.bmp        安装向导左侧大图 164x314（Inno Setup modern 样式）
#   - assets/wizard-small.bmp  安装向导右上小图 55x55
# 只依赖 .NET 自带的 System.Drawing，不引第三方包；产物在 installer/assets/ 下，不入库。
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot 'assets\WhichToPaste.ico'),
    [string]$WizardImagePath = (Join-Path $PSScriptRoot 'assets\wizard.bmp'),
    [string]$WizardSmallImagePath = (Join-Path $PSScriptRoot 'assets\wizard-small.bmp'),
    [switch]$SkipWizard
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$primary = [System.Drawing.Color]::FromArgb(0x67, 0x50, 0xA4)
$deep = [System.Drawing.Color]::FromArgb(0x4A, 0x2A, 0x85)
$white = [System.Drawing.Color]::White
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$Radius)
    $d = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# 在 (Left, Top) 处画一个边长 Size 的剪贴板标记，内部线条用底色挖出来
function Draw-ClipboardMark {
    param($Graphics, [float]$Left, [float]$Top, [float]$Size,
          [System.Drawing.Color]$MarkColor, [System.Drawing.Color]$LineColor)
    $s = $Size / 64.0
    $markBrush = New-Object System.Drawing.SolidBrush($MarkColor)
    $clip = New-RoundedPath ($Left + 25 * $s) ($Top + 10 * $s) (14 * $s) (10 * $s) (4 * $s)
    $Graphics.FillPath($markBrush, $clip)
    $clip.Dispose()
    $body = New-RoundedPath ($Left + 15 * $s) ($Top + 15 * $s) (34 * $s) (38 * $s) (7 * $s)
    $Graphics.FillPath($markBrush, $body)
    $body.Dispose()
    $markBrush.Dispose()

    $lineBrush = New-Object System.Drawing.SolidBrush($LineColor)
    $Graphics.FillRectangle($lineBrush, $Left + 23 * $s, $Top + 28 * $s, 18 * $s, 3 * $s)
    $Graphics.FillRectangle($lineBrush, $Left + 23 * $s, $Top + 35 * $s, 18 * $s, 3 * $s)
    $Graphics.FillRectangle($lineBrush, $Left + 23 * $s, $Top + 42 * $s, 11 * $s, 3 * $s)
    $lineBrush.Dispose()
}

# ---------- 应用图标：1024 像素母版 + 多尺寸 ICO ----------
$masterSize = 1024
$master = New-Object System.Drawing.Bitmap($masterSize, $masterSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($master)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$scale = $masterSize / 64.0
$shell = New-RoundedPath (1 * $scale) (1 * $scale) (62 * $scale) (62 * $scale) (17 * $scale)
$brush = New-Object System.Drawing.SolidBrush($primary)
$g.FillPath($brush, $shell)
$brush.Dispose()
$shell.Dispose()

Draw-ClipboardMark -Graphics $g -Left 0 -Top 0 -Size $masterSize -MarkColor $white -LineColor $primary
$g.Dispose()

$frames = @()
foreach ($size in $sizes) {
    $frame = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $fg = [System.Drawing.Graphics]::FromImage($frame)
    $fg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $fg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $fg.Clear([System.Drawing.Color]::Transparent)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $fg.DrawImage($master, $rect)
    $fg.Dispose()

    $data = $frame.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $frame.UnlockBits($data)
    $frame.Dispose()

    $frames += , @($size, $stride, $pixels)
}

function New-FrameBlob {
    param([int]$Size, [int]$Stride, [byte[]]$Pixels)

    $maskRowBytes = [int]([math]::Floor(($Size + 31) / 32) * 4)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    # BITMAPINFOHEADER：高度写两倍（XOR 位图 + AND 掩码）
    $bw.Write([uint32]40)
    $bw.Write([int32]$Size)
    $bw.Write([int32]($Size * 2))
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]0)
    $bw.Write([uint32]($Size * $Size * 4))
    $bw.Write([int32]0)
    $bw.Write([int32]0)
    $bw.Write([uint32]0)
    $bw.Write([uint32]0)

    # 像素：BMP 自底向上，源数据自顶向下
    for ($y = $Size - 1; $y -ge 0; $y--) {
        $bw.Write($Pixels, $y * $Stride, $Size * 4)
    }

    # AND 掩码：1 表示透明
    for ($y = $Size - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskRowBytes
        for ($x = 0; $x -lt $Size; $x++) {
            if ($Pixels[$y * $Stride + $x * 4 + 3] -eq 0) {
                $row[[int][math]::Floor($x / 8)] = $row[[int][math]::Floor($x / 8)] -bor (1 -shl (7 - ($x % 8)))
            }
        }
        $bw.Write($row)
    }

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose()
    $ms.Dispose()
    # 前面的逗号防止 PowerShell 把 byte[] 展开成单个 Byte 对象
    return , $bytes
}

$blobs = @()
foreach ($f in $frames) {
    $blobs += , (New-FrameBlob -Size $f[0] -Stride $f[1] -Pixels $f[2])
}

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($out)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)

$offset = 6 + 16 * $frames.Count
for ($i = 0; $i -lt $frames.Count; $i++) {
    $size = $frames[$i][0]
    $blob = $blobs[$i]
    $dimension = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$blob.Length)
    $writer.Write([uint32]$offset)
    $offset += $blob.Length
}
foreach ($blob in $blobs) {
    $writer.Write($blob)
}
$writer.Flush()

$directory = Split-Path -Parent $OutputPath
if ($directory -and -not (Test-Path $directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
[System.IO.File]::WriteAllBytes($OutputPath, $out.ToArray())
$writer.Dispose()
$out.Dispose()
$master.Dispose()

Write-Host "图标已生成: $OutputPath ($([math]::Round((Get-Item $OutputPath).Length / 1KB, 1)) KB, $($sizes.Count) 个尺寸)"

if ($SkipWizard) { return }

# ---------- 安装向导左侧大图（Inno Setup modern 样式，164x314） ----------
$wizardWidth = 164
$wizardHeight = 314
$wizard = New-Object System.Drawing.Bitmap($wizardWidth, $wizardHeight, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$wg = [System.Drawing.Graphics]::FromImage($wizard)
$wg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$wg.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

$wizardRect = New-Object System.Drawing.Rectangle(0, 0, $wizardWidth, $wizardHeight)
$gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush($wizardRect, $deep, $primary, 90.0)
$wg.FillRectangle($gradient, $wizardRect)
$gradient.Dispose()

# 品牌标记
Draw-ClipboardMark -Graphics $wg -Left 44 -Top 50 -Size 76 -MarkColor $white -LineColor $primary

$lightBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0xCC, 0xC6, 0xE4))
$wg.FillRectangle($lightBrush, 72, 176, 20, 2)
$lightBrush.Dispose()

$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center

$titleFont = New-Object System.Drawing.Font('Segoe UI', 18, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$titleBrush = New-Object System.Drawing.SolidBrush($white)
$wg.DrawString('WhichToPaste', $titleFont, $titleBrush, (New-Object System.Drawing.RectangleF(4, 190, 156, 30)), $format)
$titleFont.Dispose()

$subFont = New-Object System.Drawing.Font('Microsoft YaHei', 12, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$subBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0xE8, 0xDE, 0xF8))
$wg.DrawString('剪贴板历史选择器', $subFont, $subBrush, (New-Object System.Drawing.RectangleF(4, 228, 156, 22)), $format)
$subFont.Dispose()

$tipFont = New-Object System.Drawing.Font('Microsoft YaHei', 11, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$tipBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0xC9, 0xBC, 0xE6))
$wg.DrawString('复制即记录 · 粘贴时挑一条', $tipFont, $tipBrush, (New-Object System.Drawing.RectangleF(4, 258, 156, 20)), $format)
$tipFont.Dispose()
$titleBrush.Dispose()
$subBrush.Dispose()
$tipBrush.Dispose()
$format.Dispose()
$wg.Dispose()
$wizard.Save($WizardImagePath, [System.Drawing.Imaging.ImageFormat]::Bmp)
$wizard.Dispose()
Write-Host "向导大图已生成: $WizardImagePath"

# ---------- 安装向导右上小图（55x55）：白底 + 应用图标方块 ----------
$small = New-Object System.Drawing.Bitmap(55, 55, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$sg = [System.Drawing.Graphics]::FromImage($small)
$sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$sg.Clear($white)
$smallShell = New-RoundedPath 1 1 53 53 14
$smallBrush = New-Object System.Drawing.SolidBrush($primary)
$sg.FillPath($smallBrush, $smallShell)
$smallBrush.Dispose()
$smallShell.Dispose()
Draw-ClipboardMark -Graphics $sg -Left 1 -Top 1 -Size 53 -MarkColor $white -LineColor $primary
$sg.Dispose()
$small.Save($WizardSmallImagePath, [System.Drawing.Imaging.ImageFormat]::Bmp)
$small.Dispose()
Write-Host "向导小图已生成: $WizardSmallImagePath"
