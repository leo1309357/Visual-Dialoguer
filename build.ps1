<#
    build.ps1 — VisualDialoguer 构建脚本(增强版)

    职责:
      1. (可选)对齐版本号:更新 Directory.Build.props 的 Version /
         InformationalVersion(例如 -Version Dev-0.3.0)
      2. 重新生成 CDLG 帮助文档(保证编辑器嵌入资源是最新的)
      3. dotnet publish 单文件 portable exe
      4. 产物校验:exe 存在、体积合理、publish 目录无散放文件(警告)
      5. 启动冒烟测试:启动 6 秒未崩溃即通过
      6. 输出汇总:耗时 / 版本 / 大小 / 冒烟结果

    用法(在仓库根目录):
        .\build.ps1                          # Release + win-x64 + 默认全流程
        .\build.ps1 -Configuration Debug     # Debug 构建
        .\build.ps1 -Version Dev-0.3.0       # 对齐版本号后发布
        .\build.ps1 -SkipDocs                # 跳过文档生成
        .\build.ps1 -NoSmoke                 # 跳过启动冒烟测试
        .\build.ps1 -Clean                   # 先清理 bin/obj 再发布

    产出:
        dist\VisualDialoguer.exe —— 单个 portable 可执行文件(约 83 MB)

    要求:
        .NET SDK 8 或更高(不在 PATH 时自动在标准安装位置查找);
        Python(可选,用于重新生成帮助文档;缺失时跳过并警告)。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    # 版本号,格式 0.2.1 或 Dev-0.2.1。给定后同步更新 Directory.Build.props
    # 的 <Version>(纯数字)与 <InformationalVersion>(带前缀)。
    [string]$Version = '',

    # 跳过"重新生成帮助文档"步骤(一般不必跳过,生成很快)。
    [switch]$SkipDocs,

    # 跳过发布后的启动冒烟测试(会短暂弹出应用窗口)。
    [switch]$NoSmoke,

    # 发布前删除各项目的 bin/obj,强制全量构建。
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sw = [System.Diagnostics.Stopwatch]::StartNew()

$csproj      = Join-Path $root 'src\VisualDialoguer.App\VisualDialoguer.App.csproj'
$props       = Join-Path $root 'Directory.Build.props'
$docsGen     = Join-Path $root 'tools\build_docs.py'
$pubRoot     = Join-Path $root 'src\VisualDialoguer.App\bin'
$dist        = Join-Path $root 'dist'
$distExe     = Join-Path $dist 'VisualDialoguer.exe'

$step = 0
function Step([string]$title) {
    $script:step++
    Write-Host ""
    Write-Host "=== [$($script:step)/6] $title ===" -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------
# 工具定位
# ---------------------------------------------------------------------------

function Find-Tool {
    param([string]$Name, [string[]]$Candidates)
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($c in $Candidates) {
        if (Test-Path -LiteralPath $c) { return $c }
    }
    return $null
}

$dotnet = Find-Tool 'dotnet' @(
    'C:\Program Files\dotnet\dotnet.exe',
    'C:\Program Files (x86)\dotnet\dotnet.exe',
    (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
)
if (-not $dotnet) { throw '未找到 dotnet。请先安装 .NET SDK 8 或更高版本。' }

$python = Find-Tool 'python' @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python*\python.exe'),
    'C:\Python*\python.exe'
)

# ---------------------------------------------------------------------------
# 1. 版本号对齐(可选)
# ---------------------------------------------------------------------------

function Get-CurrentVersion {
    $content = [IO.File]::ReadAllText($props)
    $m = [regex]::Match($content, '<InformationalVersion>([^<]+)</InformationalVersion>')
    if ($m.Success) { return $m.Groups[1].Value }
    return '(未设置)'
}

function Update-VersionFile {
    param([string]$VersionArg)
    if (-not $VersionArg) { return }

    $m = [regex]::Match($VersionArg, '^(?:([A-Za-z][A-Za-z0-9]*)-)?(\d+\.\d+\.\d+)$')
    if (-not $m.Success) { throw "版本号格式应为 0.2.1 或 Dev-0.2.1,得到 '$VersionArg'" }

    $prefix        = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { 'Dev' }
    $versionBody   = $m.Groups[2].Value
    $informational = "$prefix-$versionBody"

    # 保持原文件编码:BOM 检测后原样写回。
    $bytes = [IO.File]::ReadAllBytes($props)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $content = [IO.File]::ReadAllText($props)
    $content = $content -replace '<Version>[^<]*</Version>', "<Version>$versionBody</Version>"
    $content = $content -replace '<InformationalVersion>[^<]*</InformationalVersion>', "<InformationalVersion>$informational</InformationalVersion>"
    $encoding = New-Object System.Text.UTF8Encoding($hasBom)
    [IO.File]::WriteAllText($props, $content, $encoding)

    Write-Host "版本已对齐: Version=$versionBody, InformationalVersion=$informational" -ForegroundColor Green
}

Step '版本信息'
Write-Host "当前版本: $(Get-CurrentVersion)  目标配置: $Configuration / $Runtime"
Update-VersionFile -VersionArg $Version

# ---------------------------------------------------------------------------
# 2. 清理(可选)
# ---------------------------------------------------------------------------

if ($Clean) {
    Step '清理 bin/obj'
    Get-ChildItem -LiteralPath $root -Directory -Recurse -Force |
        Where-Object { $_.Name -in @('bin', 'obj') } |
        ForEach-Object {
            Write-Host "  删除 $($_.FullName)"
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
        }
    if (Test-Path -LiteralPath $dist) {
        Write-Host "  清空 dist"
        Remove-Item -LiteralPath $dist -Recurse -Force
    }
}

# ---------------------------------------------------------------------------
# 3. 重新生成帮助文档(除非跳过)
# ---------------------------------------------------------------------------

if (-not $SkipDocs) {
    Step '生成帮助文档'
    if (-not $python) {
        Write-Warning '未找到 python,跳过文档生成(编辑器帮助将沿用仓库里的嵌入副本)。'
    }
    elseif (-not (Test-Path -LiteralPath $docsGen)) {
        Write-Warning "未找到生成器 $docsGen,跳过文档生成。"
    }
    else {
        & $python $docsGen
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "文档生成器退出码 $LASTEXITCODE,继续构建但帮助可能不是最新。"
        }
    }
}
else {
    Step '生成帮助文档'
    Write-Host '  -SkipDocs 已指定,跳过。'
}

# ---------------------------------------------------------------------------
# 4. 发布单文件 portable exe
# ---------------------------------------------------------------------------

Step 'dotnet publish'
Write-Host "  dotnet publish -c $Configuration -r $Runtime --self-contained true"
& $dotnet publish $csproj -c $Configuration -r $Runtime --self-contained true --nologo
if ($LASTEXITCODE -ne 0) { throw "publish 失败,退出码 $LASTEXITCODE" }

$pub = Join-Path $pubRoot "$Configuration\net8.0-windows10.0.19041.0\$Runtime\publish"
if (-not (Test-Path -LiteralPath $pub)) { throw "未找到发布目录: $pub" }

# 单文件性检查:除 exe 外若有散放文件,警告(便于发现配置回退)。
$stray = Get-ChildItem -LiteralPath $pub -File | Where-Object { $_.Extension -ne '.exe' }
if ($stray) {
    Write-Warning ("publish 目录存在非 exe 文件(共 {0} 个):{1}" -f $stray.Count,
        (($stray | Select-Object -First 5 | ForEach-Object Name) -join ', '))
}

# ---------------------------------------------------------------------------
# 5. 拷贝到 dist 并校验
# ---------------------------------------------------------------------------

Step '产出与校验'
if (-not (Test-Path -LiteralPath $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

Get-ChildItem -LiteralPath $pub -File -Filter '*.exe' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $dist $_.Name) -Force
}
if (-not (Test-Path -LiteralPath $distExe)) { throw 'dist 目录中没有找到 VisualDialoguer.exe,构建可能未按预期产出。' }

$sizeMb = [math]::Round((Get-Item -LiteralPath $distExe).Length / 1MB, 1)
if ($sizeMb -lt 50) { Write-Warning "产物偏小($sizeMb MB),请确认自包含单文件配置没有回退。" }
Write-Host "  产物: $distExe ($sizeMb MB)"

# ---------------------------------------------------------------------------
# 6. 冒烟测试(除非跳过)
# ---------------------------------------------------------------------------

$smoke = '跳过(-NoSmoke)'
if (-not $NoSmoke) {
    Write-Host ''
    Write-Host "=== [6/6] 冒烟测试(启动 6 秒) ===" -ForegroundColor Cyan
    $p = Start-Process -FilePath $distExe -PassThru
    Start-Sleep -Seconds 6
    if ($p.HasExited) {
        $smoke = "失败(启动后退出,代码 $($p.ExitCode))"
        Write-Error $smoke
    }
    else {
        $smoke = '通过(启动后保持运行)'
        Stop-Process -Id $p.Id -Force
        Write-Host "  $smoke" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# 汇总
# ---------------------------------------------------------------------------

$sw.Stop()
Write-Host ''
Write-Host '========== 构建汇总 ==========' -ForegroundColor Cyan
Write-Host "  版本      : $(Get-CurrentVersion)"
Write-Host "  配置      : $Configuration / $Runtime"
Write-Host "  产物      : $distExe ($sizeMb MB)"
Write-Host "  冒烟测试  : $smoke"
Write-Host ("  总耗时    : {0:hh\:mm\:ss}" -f $sw.Elapsed)
Write-Host '==============================' -ForegroundColor Cyan
