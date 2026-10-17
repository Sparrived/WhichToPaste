# 一键构建 WhichToPaste 安装包
#
#   1. 生成应用图标（installer/assets/WhichToPaste.ico）
#   2. dotnet publish 自包含 win-x64 产物到 artifacts/payload
#   3. 用 Inno Setup 编译 installer/WhichToPaste.iss，输出到 artifacts/
#
# 依赖：.NET 10 SDK；Inno Setup 6 或 7（winget install JRSoftware.InnoSetup）。
# 也可把便携版解压到 .tools\innosetup-6.0.5\ 或任意位置，用 -IsccPath 指定。
#
# 例：
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -SkipPublish

[CmdletBinding()]
param(
    [string]$IsccPath,
    [switch]$SkipIcon,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\WhichToPaste\WhichToPaste.csproj'
$payload = Join-Path $root 'artifacts\payload'
$icon = Join-Path $PSScriptRoot 'assets\WhichToPaste.ico'
$iss = Join-Path $PSScriptRoot 'WhichToPaste.iss'

function Get-AppVersion {
    $version = & dotnet msbuild $project -getProperty:Version -nologo 2>$null
    if ($LASTEXITCODE -eq 0 -and $version) {
        return ($version.Trim() -split '\+')[0]
    }
    # 老 SDK 不支持 -getProperty 时退回读 csproj
    [xml]$xml = Get-Content -Raw $project
    $value = $xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $value) { return '1.0.0' }
    return ($value -split '\+')[0]
}

function Find-Iscc {
    param([string]$Explicit)

    if ($Explicit) {
        if (Test-Path $Explicit) { return (Resolve-Path $Explicit).Path }
        throw "找不到指定的 ISCC.exe：$Explicit"
    }

    $candidates = @(
        (Join-Path $root '.tools\innosetup-6.0.5\ISCC.exe'),
        (Join-Path $root '.tools\innosetup\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return (Resolve-Path $candidate).Path }
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    throw @'
找不到 Inno Setup 编译器 ISCC.exe。
请安装 Inno Setup 6/7：
  winget install JRSoftware.InnoSetup
或把便携版放到 .tools\innosetup-6.0.5\，或用 -IsccPath 指定 ISCC.exe 路径。
'@
}

$version = Get-AppVersion
Write-Host "[1/3] 版本号：$version"

if (-not $SkipIcon) {
    & (Join-Path $PSScriptRoot 'make-icon.ps1') -OutputPath $icon
    if (-not (Test-Path $icon)) { throw '图标生成失败' }
}
Write-Host "[2/3] 图标就绪：$icon"

if (-not $SkipPublish) {
    if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
    Write-Host '      正在发布自包含 win-x64 产物…'
    & dotnet publish $project `
        -c Release `
        -r win-x64 `
        -p:SelfContained=true `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        "-p:ApplicationIcon=$icon" `
        -o $payload
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（exit $LASTEXITCODE）" }
}
if (-not (Test-Path (Join-Path $payload 'WhichToPaste.exe'))) {
    throw "发布目录里没有 WhichToPaste.exe：$payload"
}
$payloadSize = [math]::Round(((Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "      发布完成：$payload（$payloadSize MB）"

$iscc = Find-Iscc $IsccPath
Write-Host "[3/3] 编译安装包：$iscc"
& $iscc "/DAppVersion=$version" $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败（exit $LASTEXITCODE）" }

$setup = Join-Path $root "artifacts\WhichToPaste-Setup-$version-x64.exe"
if (-not (Test-Path $setup)) { throw "没有找到安装包输出：$setup" }

$file = Get-Item $setup
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash
Write-Host ''
Write-Host '安装包构建完成：'
Write-Host "  路径：$setup"
Write-Host "  大小：$([math]::Round($file.Length / 1MB, 1)) MB"
Write-Host "  SHA256：$hash"
