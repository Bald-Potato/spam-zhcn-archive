param([string]$GameDir = 'E:\SteamLibrary\steamapps\common\SPAM')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name SPAM -ErrorAction SilentlyContinue) { throw '请先关闭 SPAM，再安装补丁。' }
$spamGameRoot = [IO.Path]::GetFullPath($GameDir).TrimEnd('\')
if (-not (Test-Path -LiteralPath (Join-Path $spamGameRoot 'SPAM.exe'))) { throw '该目录不是 SPAM 安装目录。请使用 install.cmd -GameDir "游戏完整路径"。' }
$spamManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$spamInstallRecord = Join-Path $spamGameRoot 'spam-zh-install.json'
$spamPrevious = if (Test-Path -LiteralPath $spamInstallRecord) { Get-Content -LiteralPath $spamInstallRecord -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
$spamOwned = @{}; if ($spamPrevious) { foreach ($spamFile in $spamPrevious.files) { $spamOwned[$spamFile.path]=$spamFile.sha256 } }
$spamCopies = @()
foreach ($spamFile in $spamManifest.files) {
  if ($spamFile.path -in @('install.ps1','uninstall.ps1','install.cmd','uninstall.cmd','README.md','验证记录.md','手动验收流程.md','手动验收记录.csv','手动验收流程-0.2.md','手动验收记录-0.2.csv','字体预览-0.2.svg')) { continue }
  $spamSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $spamFile.path))
  $spamTarget = [IO.Path]::GetFullPath((Join-Path $spamGameRoot $spamFile.path))
  if (-not $spamTarget.StartsWith($spamGameRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or -not $spamSource.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '补丁清单路径越界。' }
  if ((Get-FileHash -LiteralPath $spamSource -Algorithm SHA256).Hash -ne $spamFile.sha256) { throw "补丁文件校验失败：$($spamFile.path)" }
  if (Test-Path -LiteralPath $spamTarget) {
    $spamHash=(Get-FileHash -LiteralPath $spamTarget -Algorithm SHA256).Hash
    if ($spamHash -ne $spamFile.sha256 -and (-not $spamOwned.ContainsKey($spamFile.path) -or $spamOwned[$spamFile.path] -ne $spamHash)) { throw "已有文件不是本补丁管理的版本，停止覆盖：$($spamFile.path)" }
  }
  $spamCopies += @{source=$spamSource;target=$spamTarget;path=$spamFile.path;sha256=$spamFile.sha256}
}
# All destination paths and ownership checks complete before copying any files.
$spamNextPaths=@{}; foreach($spamCopy in $spamCopies) { $spamNextPaths[$spamCopy.path]=$true }
$spamObsoleteFonts=@()
if($spamPrevious) {
  foreach($spamOldFile in $spamPrevious.files) {
    if(-not $spamNextPaths.ContainsKey($spamOldFile.path) -and $spamOldFile.path.StartsWith('BepInEx\plugins\SpamChinese\fonts\',[StringComparison]::OrdinalIgnoreCase)) {
      $spamOldPath=[IO.Path]::GetFullPath((Join-Path $spamGameRoot $spamOldFile.path))
      if(-not $spamOldPath.StartsWith($spamGameRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '旧字体清单路径越界。' }
      if((Test-Path -LiteralPath $spamOldPath) -and (Get-FileHash -LiteralPath $spamOldPath -Algorithm SHA256).Hash -eq $spamOldFile.sha256) { $spamObsoleteFonts += $spamOldPath }
    }
  }
}
foreach ($spamCopy in $spamCopies) {
  New-Item -ItemType Directory -Path (Split-Path -Parent $spamCopy.target) -Force | Out-Null
  Copy-Item -LiteralPath $spamCopy.source -Destination $spamCopy.target -Force
}
foreach($spamOldPath in $spamObsoleteFonts) { Remove-Item -LiteralPath $spamOldPath }
@{schema=1;version=$spamManifest.version;files=@($spamCopies | ForEach-Object { @{path=$_.path;sha256=$_.sha256} })} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $spamInstallRecord -Encoding utf8
Write-Output '汉化补丁已安装。游戏语言选择 English；按住 F8 查看英文原文。'
