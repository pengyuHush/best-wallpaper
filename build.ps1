param(
    [switch]$SkipTests,
    [switch]$SkipPublish,
    [switch]$SelfContained,
    [switch]$RegenIcon
)

# 构建「每日壁纸」：编译 →（可选）跑自测 → 发布单文件 exe 到 dist\
# 需要 .NET 10 SDK；默认从 %LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe 取，也可用 -dotnet 指定。

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $dotnet = $onPath.Source }
    else { throw "找不到 .NET SDK。请把 SDK 装到 $env:LOCALAPPDATA\Microsoft\dotnet，或把 dotnet 放进 PATH。" }
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

Write-Host "使用 SDK: $dotnet" -ForegroundColor Cyan
& $dotnet --version

if ($RegenIcon) {
    Write-Host "重新生成图标…" -ForegroundColor Cyan
    & (Join-Path $root 'scripts\make_icon.ps1')
    Copy-Item (Join-Path $root 'scripts\assets\app.ico') (Join-Path $root 'BestWallpaper\assets\app.ico') -Force
}

Write-Host "`n编译…" -ForegroundColor Cyan
& $dotnet build (Join-Path $root 'BestWallpaper\BestWallpaper.csproj') -c Release -v minimal -p:NoWarn=WFO0003
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

if (-not $SkipTests) {
    Write-Host "`n自测…" -ForegroundColor Cyan
    & $dotnet run --project (Join-Path $root 'tests\BestWallpaper.Tests\BestWallpaper.Tests.csproj') -c Release -p:NoWarn=WFO0003
    if ($LASTEXITCODE -ne 0) { throw "自测未通过（见上面 FAIL 行）" }
}

if (-not $SkipPublish) {
    $out = Join-Path $root 'dist'
    Write-Host "`n发布到 $out …" -ForegroundColor Cyan
    $pubArgs = @(
        'publish', (Join-Path $root 'BestWallpaper\BestWallpaper.csproj'),
        '-c', 'Release', '-r', 'win-x64',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:NoWarn=WFO0003',
        '-p:DebugType=none',
        '-o', $out,
        '-v', 'quiet'
    )
    if ($SelfContained) { $pubArgs += '--self-contained'; $pubArgs += 'true' } else { $pubArgs += '--self-contained'; $pubArgs += 'false' }
    & $dotnet @pubArgs
    if ($LASTEXITCODE -ne 0) { throw "发布失败" }

    $exe = Join-Path $out 'BestWallpaper.exe'

    # publish 出来的文件会继承目录的完整性标签；若目录是 Low，exe 就会以 Low 进程运行，
    # 那样它写不了 HKCU、改不了壁纸。这里统一重置为 Medium，并断言必须成功。
    $fixer = Join-Path $root 'scripts\set-integrity.ps1'
    if (Test-Path $fixer) {
        Write-Host "修正发布目录的完整性标签…" -ForegroundColor Cyan
        & $fixer -Path $out -Recurse | Select-Object -First 1

        $labelOut = & $fixer -Path $exe 2>&1 | Out-String
        if ($labelOut -notmatch 'S-1-16-8192') {
            throw "交付中止：exe 的完整性标签不是 Medium(8192)。实际输出：`n$labelOut"
        }
        Write-Host "标签断言通过：exe 是 Medium。" -ForegroundColor Green
    }

    $size = [math]::Round((Get-Item $exe).Length / 1MB, 2)
    Write-Host "`n完成: $exe ($size MB)" -ForegroundColor Green
}
