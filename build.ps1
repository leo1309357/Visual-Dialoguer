<#
    build.ps1 — VisualDialoguer 单文件 portable exe 构建脚本

    用法:
        powershell -ExecutionPolicy Bypass -File .\build.ps1
        # 或直接在本仓库根目录:
        .\build.ps1

    产出:
        dist\VisualDialoguer.exe   —— 单个 portable 可执行文件(约 83 MB)
    要求:
        .NET SDK 8 或更高(建议已加入 PATH;否则自动在标准安装位置查找)
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csproj = Join-Path $root 'src\VisualDialoguer.App\VisualDialoguer.App.csproj'

# ---- 1. 定位 dotnet ----
function Find-DotNet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        'C:\Program Files\dotnet\dotnet.exe',
        'C:\Program Files (x86)\dotnet\dotnet.exe',
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    )
    foreach ($p in $candidates) {
        if (Test-Path -LiteralPath $p) { return $p }
    }
    throw '未找到 dotnet。请先安装 .NET SDK 8 或更高版本。'
}

$dotnet = Find-DotNet
Write-Host "[1/3] 使用 dotnet: $dotnet"

# ---- 2. 发布单文件 portable exe ----
Write-Host '[2/3] dotnet publish -c Release -r win-x64 --self-contained true ...'
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true
if ($LASTEXITCODE -ne 0) { throw "publish 失败,退出码 $LASTEXITCODE" }

# ---- 3. 拷贝产物到 dist\,清理非必需文件 ----
$pub = Join-Path $root 'src\VisualDialoguer.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish'
if (-not (Test-Path -LiteralPath $pub)) { throw "未找到发布目录: $pub" }

$dist = Join-Path $root 'dist'
if (-not (Test-Path -LiteralPath $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

# 只保留 exe;pdb 属调试符号,不随 portable 交付
Get-ChildItem -LiteralPath $pub -File | ForEach-Object {
    if ($_.Name -like '*.exe') {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $dist $_.Name) -Force
    }
}

$exe = Join-Path $dist 'VisualDialoguer.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'dist 目录中没有找到 VisualDialoguer.exe,构建可能未按预期产出。' }

$sizeMb = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
Write-Host "[3/3] 完成: $exe ($sizeMb MB)"
