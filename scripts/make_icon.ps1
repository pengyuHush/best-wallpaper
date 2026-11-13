param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'assets')
)

# 生成一个 256x256 的应用图标：深蓝渐变 + 山脊 + 一轮月。
# 先画 PNG，再用 Python 包成单条目 ICO（256 尺寸用 PNG 压缩是合法的）。

Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::FromArgb(18, 26, 48))

$rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect,
    [System.Drawing.Color]::FromArgb(30, 58, 138),
    [System.Drawing.Color]::FromArgb(10, 14, 30),
    90.0)
$g.FillRectangle($grad, $rect)

# 月亮
$moonBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 238, 210))
$g.FillEllipse($moonBrush, 158, 42, 52, 52)

# 远山
$farBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(28, 44, 82))
$far = @(
    (New-Object System.Drawing.Point 0, 196),
    (New-Object System.Drawing.Point 62, 118),
    (New-Object System.Drawing.Point 126, 196)
) 
$g.FillPolygon($farBrush, $far)
$far2 = @(
    (New-Object System.Drawing.Point 96, 196),
    (New-Object System.Drawing.Point 168, 104),
    (New-Object System.Drawing.Point 256, 196)
)
$g.FillPolygon($farBrush, $far2)

# 近山
$nearBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(15, 23, 42))
$near = @(
    (New-Object System.Drawing.Point 0, 256),
    (New-Object System.Drawing.Point 0, 206),
    (New-Object System.Drawing.Point 74, 150),
    (New-Object System.Drawing.Point 152, 214),
    (New-Object System.Drawing.Point 206, 168),
    (New-Object System.Drawing.Point 256, 210),
    (New-Object System.Drawing.Point 256, 256)
)
$g.FillPolygon($nearBrush, $near)

# 水线高光
$linePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, 140, 190, 255)), 3
$g.DrawLine($linePen, 0, 232, 256, 232)

$g.Dispose()

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$png = Join-Path $OutDir 'app.png'
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"PNG written: $png ($((Get-Item $png).Length) bytes)"

$ico = Join-Path $OutDir 'app.ico'
$py = Join-Path $PSScriptRoot 'png_to_ico.py'
if (Test-Path $py) {
    python $py $png $ico
    "ICO written: $ico ($(if (Test-Path $ico) { (Get-Item $ico).Length } else { 0 }) bytes)"
} else {
    Write-Warning "png_to_ico.py not found at $py"
}
