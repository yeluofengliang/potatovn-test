<#
.SYNOPSIS
    不安装 Visual Studio 图形界面，用命令行编译插件。

.DESCRIPTION
    本脚本只负责调用 MSBuild，不负责安装依赖。
    编译插件需要以下两样东西，请先自行准备好：

      1. .NET 8 SDK        https://dotnet.microsoft.com/download
      2. MSBuild 与 WinUI 生成目标
         - 方式一：安装 "Visual Studio 2022 生成工具"（比完整 VS 小很多，无图形界面）
                   https://visualstudio.microsoft.com/zh-hans/downloads/#build-tools-for-visual-studio-2022
                   安装时勾选「WinUI 应用程序开发」工作负载
         - 方式二：已装完整 VS2022（勾选了 WinUI 工作负载）

    若只想试一把，可以用 -UseDotnet 改用 dotnet CLI 编译，
    但它对 WinUI3 项目的支持不如 MSBuild 稳妥，失败时请换回 MSBuild。

.EXAMPLE
    .\build.ps1

    # 改用 dotnet CLI 编译
    .\build.ps1 -UseDotnet
#>

param(
    # 改用 dotnet CLI 而不是 MSBuild
    [switch]$UseDotnet,

    # 强制重新拉取宿主源码
    [switch]$ForceRefreshHost
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$ProjectDir = 'PotatoVN.App.Plugin.ScrapeControl'
$Project    = Join-Path $ProjectDir 'PotatoVN.App.Plugin.ScrapeControl.csproj'
$HostDir    = 'PotatoVN'
$Output     = Join-Path $ProjectDir 'artifacts\plugin.pvnplugin.zip'

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Fail($msg) { Write-Host "`n[失败] $msg" -ForegroundColor Red }

Write-Host 'PotatoVN 插件命令行编译脚本' -ForegroundColor Green
Write-Host '（本脚本不安装依赖，请确保已装 .NET 8 SDK 与 MSBuild）' -ForegroundColor DarkGray

# ---------------------------------------------------------------- 1. 宿主源码

Write-Step '检查 PotatoVN 宿主源码'

$hostProject = Join-Path $HostDir 'GalgameManager.WinApp.Base\GalgameManager.WinApp.Base.csproj'

if ((Test-Path $hostProject) -and -not $ForceRefreshHost) {
    Write-Host '  已存在，跳过下载'
}
else {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Fail '需要 git 来下载宿主源码，请先安装 Git：https://git-scm.com/'
        exit 1
    }
    if ($ForceRefreshHost -and (Test-Path $HostDir)) {
        Write-Host '  清理旧的宿主源码...'
        Remove-Item $HostDir -Recurse -Force
    }
    Write-Host '  正在克隆 PotatoVN（约几十 MB，请稍候）...'
    git clone --depth 1 https://github.com/GoldenPotato137/PotatoVN.git $HostDir
    if ($LASTEXITCODE -ne 0) {
        Write-Fail '克隆失败，请检查网络'
        exit 1
    }
}

if (-not (Test-Path $hostProject)) {
    Write-Fail "宿主源码结构异常，未找到 $hostProject"
    exit 1
}
Write-Host '  宿主源码就绪' -ForegroundColor Green

# ---------------------------------------------------------------- 2. 编译

Write-Step '编译插件（Release）'

if ($UseDotnet) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Fail '未找到 dotnet，请先安装 .NET 8 SDK'
        exit 1
    }
    Write-Host '  使用 dotnet CLI...'
    dotnet build $Project -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Fail 'dotnet 编译失败。WinUI3 项目对 dotnet CLI 支持有限，请改用 MSBuild（去掉 -UseDotnet）'
        exit 1
    }
}
else {
    $msbuild = Get-Command msbuild -ErrorAction SilentlyContinue
    if (-not $msbuild) {
        # VS 生成工具默认不进 PATH，尝试用 vswhere 定位
        $vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
        if (Test-Path $vsWhere) {
            $installPath = & $vsWhere -latest -products * `
                -requires Microsoft.Component.MSBuild `
                -property installationPath 2>$null | Select-Object -First 1
            if ($installPath) {
                $candidate = Join-Path $installPath 'MSBuild\Current\Bin\MSBuild.exe'
                if (Test-Path $candidate) { $msbuild = Get-Command $candidate }
            }
        }
    }

    if (-not $msbuild) {
        Write-Fail @'
未找到 MSBuild。

请安装 "Visual Studio 2022 生成工具"：
  https://visualstudio.microsoft.com/zh-hans/downloads/#build-tools-for-visual-studio-2022
安装时务必勾选「WinUI 应用程序开发」工作负载。

安装完重开一个 PowerShell 窗口再运行本脚本。
'@
        exit 1
    }

    Write-Host "  使用 MSBuild: $($msbuild.Source)"
    & $msbuild.Source $Project /restore /p:Configuration=Release /p:Platform=AnyCPU
    if ($LASTEXITCODE -ne 0) {
        Write-Fail '编译失败，请查看上方错误信息'
        exit 1
    }
}

# ---------------------------------------------------------------- 3. 产物

Write-Step '检查产物'

if (-not (Test-Path $Output)) {
    Write-Fail "未找到 $Output"
    Write-Host '编译可能成功了但打包步骤未执行（打包仅在 Release 下运行）' -ForegroundColor Yellow
    exit 1
}

$size = [math]::Round((Get-Item $Output).Length / 1KB, 1)
Write-Host "  打包完成：$Output  ($size KB)" -ForegroundColor Green
Write-Host ''
Write-Host '接下来：' -ForegroundColor Cyan
Write-Host '  PotatoVN -> 设置 -> 插件 -> 右上角「添加插件」按钮 -> 选择该 zip 文件'
Write-Host '  （若看不到按钮，先到 设置 -> 其他设置 打开「开发者模式」）'
