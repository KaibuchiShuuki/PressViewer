<#
.SYNOPSIS
    构建并发布 PressViewer：publish 主程序与统计服务、打包 zip、创建 GitHub Release 并上传附件。

.DESCRIPTION
    版本号取自 PressViewer.csproj 的 <Version>，发布包命名为 PressViewer-<version>-win-x64.zip，
    zip 内顶层是 PressViewer/ 目录（含主程序与服务），不含 data/ 与 *.pdb。

    上传用的 token 从 git 凭据（git credential fill）里取，不落盘。
    正文一律转成 UTF-8 字节数组再提交，避免 PowerShell 按 ANSI 编码发出中文导致乱码。

.NOTES
    需要 .NET 10 SDK 与联网。发布前请先关闭正在运行的 PressViewer，
    否则 app/PressViewer.dll 被占用，publish 会失败。
#>

param(
    [string]$DotnetRoot = 'D:\soft\dotnet10',
    [string]$AppDir = 'D:\soft\PressViewer\app',
    [string]$ServiceDir = 'D:\soft\PressViewer\service',
    [string]$Repo = 'KaibuchiShuuki/PressViewer',
    [string]$Notes,
    [switch]$SkipPublish,
    [switch]$SkipUpload
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $DotnetRoot 'dotnet.exe'

$csproj = Join-Path $repoRoot 'PressViewer.csproj'
$match = Select-String -Path $csproj -Pattern '<Version>(.*?)</Version>' | Select-Object -First 1
if (-not $match) { throw '读不到版本号。' }
$version = $match.Matches[0].Groups[1].Value.Trim()
$tag = "v$version"
$zipName = "PressViewer-$version-win-x64.zip"
Write-Host "版本：$version"

# ---------- 1. 发布 ----------
if (-not $SkipPublish) {
    $running = Get-Process -Name 'PressViewer' -ErrorAction SilentlyContinue
    if ($running) {
        throw "PressViewer 正在运行（PID $($running.Id)），请先从托盘退出再发布。"
    }

    $env:DOTNET_ROOT = $DotnetRoot

    Write-Host '发布主程序 ...'
    & $dotnet publish (Join-Path $repoRoot 'PressViewer.csproj') -c Release -r win-x64 `
        --self-contained true -p:IncludeLocalModules=false -o $AppDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw '主程序发布失败。' }

    Write-Host '发布统计服务 ...'
    $svcProject = Join-Path $repoRoot 'XAssistant.Service\XAssistant.Service.csproj'
    $svcStage = Join-Path $env:TEMP 'pressviewer-service-publish'
    if (Test-Path $svcStage) { Remove-Item $svcStage -Recurse -Force }
    & $dotnet publish $svcProject -c Release -r win-x64 --self-contained true -o $svcStage | Out-Null
    if ($LASTEXITCODE -ne 0) { throw '服务发布失败。' }

    Write-Host '复制服务文件（服务正在运行时会被跳过，需先停服务）...'
    & robocopy $svcStage $ServiceDir /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "服务文件复制失败（robocopy $LASTEXITCODE）。" }
}

# ---------- 2. 打包 ----------
$stage = Join-Path $env:TEMP 'pressviewer-release-stage'
$zipPath = Join-Path $env:TEMP $zipName
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$root = Join-Path $stage 'PressViewer'
New-Item -ItemType Directory -Path $root -Force | Out-Null
& robocopy $AppDir $root /E /XD data /XF *.pdb | Out-Null
if ($LASTEXITCODE -ge 8) { throw "打包主程序失败（robocopy $LASTEXITCODE）。" }
& robocopy $ServiceDir (Join-Path $root 'service') /E /XD data /XF *.pdb | Out-Null
if ($LASTEXITCODE -ge 8) { throw "打包服务失败（robocopy $LASTEXITCODE）。" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
$zip = Get-Item $zipPath
Write-Host ("打包完成：{0}（{1:N0} 字节）" -f $zipPath, $zip.Length)

if ($SkipUpload) { return }

# ---------- 3. 上传 ----------
$cred = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
$token = ($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=', ''
if ([string]::IsNullOrWhiteSpace($token)) { throw '拿不到 GitHub token。' }

$headers = @{
    Authorization          = "token $token"
    'Accept'               = 'application/vnd.github+json'
    'User-Agent'           = 'PressViewer-release'
    'X-GitHub-Api-Version' = '2022-11-28'
}

if ([string]::IsNullOrWhiteSpace($Notes)) {
    $Notes = "PressViewer $version。更新内容见仓库的 CHANGELOG。"
}

$body = @{
    tag_name         = $tag
    target_commitish = 'master'
    name             = "PressViewer $version"
    body             = $Notes
    draft            = $false
    prerelease       = $false
} | ConvertTo-Json -Depth 4 -Compress

$bytes = [Text.Encoding]::UTF8.GetBytes($body)
$release = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$Repo/releases" `
    -Headers $headers -Body $bytes -ContentType 'application/json; charset=utf-8'
Write-Host ("Release：{0}" -f $release.html_url)

$uploadUrl = ($release.upload_url -replace '\{\?name,label\}', '') + "?name=$zipName"
$asset = Invoke-RestMethod -Method Post -Uri $uploadUrl `
    -Headers @{ Authorization = "token $token"; 'User-Agent' = 'PressViewer-release' } `
    -ContentType 'application/zip' -InFile $zipPath
Write-Host ("附件：{0}（{1:N0} 字节）" -f $asset.name, $asset.size)
Write-Host ("下载：{0}" -f $asset.browser_download_url)
