param([string]$GameDir = 'E:\SteamLibrary\steamapps\common\SPAM')
$ErrorActionPreference = 'Stop'
if (Get-Process -Name SPAM -ErrorAction SilentlyContinue) { throw '请先关闭 SPAM，再卸载补丁。' }
$spamGameRoot=[IO.Path]::GetFullPath($GameDir).TrimEnd('\')
$spamRecordPath=Join-Path $spamGameRoot 'spam-zh-install.json'
if (-not (Test-Path -LiteralPath $spamRecordPath)) { throw '未找到本补丁的安装记录。' }
$spamRecord=Get-Content -LiteralPath $spamRecordPath -Raw -Encoding UTF8 | ConvertFrom-Json
$spamOtherPlugins=@(Get-ChildItem -LiteralPath (Join-Path $spamGameRoot 'BepInEx\plugins') -File -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Extension -eq '.dll' -and -not $_.FullName.StartsWith((Join-Path $spamGameRoot 'BepInEx\plugins\SpamChinese')+'\',[StringComparison]::OrdinalIgnoreCase) })
foreach ($spamFile in $spamRecord.files) {
  $spamTarget=[IO.Path]::GetFullPath((Join-Path $spamGameRoot $spamFile.path))
  if (-not $spamTarget.StartsWith($spamGameRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw '卸载清单路径越界。' }
  if ($spamOtherPlugins.Count -gt 0 -and -not $spamFile.path.StartsWith('BepInEx\plugins\SpamChinese\')) { continue }
  if (Test-Path -LiteralPath $spamTarget) {
    if ((Get-FileHash -LiteralPath $spamTarget -Algorithm SHA256).Hash -eq $spamFile.sha256) { Remove-Item -LiteralPath $spamTarget }
    else { Write-Output "保留安装后发生变化的文件：$($spamFile.path)" }
  }
}
Remove-Item -LiteralPath $spamRecordPath
# User-generated draft caches/config are retained; no recursive directory deletion.
Write-Output '已移除汉化插件；个人缓存和配置保留。下次启动恢复原版文字。'
