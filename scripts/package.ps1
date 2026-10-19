param([string]$GameDir = 'E:\SteamLibrary\steamapps\common\SPAM',[string]$Version = '0.2.0')
$ErrorActionPreference = 'Stop'
$spamRoot = Split-Path -Parent $PSScriptRoot
$spamPackage = Join-Path $spamRoot "artifacts\SPAM-zhCN-$Version"
$spamPlugin = Join-Path $spamPackage 'BepInEx\plugins\SpamChinese'
New-Item -ItemType Directory -Path $spamPlugin -Force | Out-Null
dotnet build (Join-Path $spamRoot 'src\SpamChinese\SpamChinese.csproj') -c Release --nologo "-p:GameDir=$GameDir" -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath (Join-Path $spamRoot 'vendor\bepinex\winhttp.dll') -Destination $spamPackage
Copy-Item -LiteralPath (Join-Path $spamRoot 'vendor\bepinex\doorstop_config.ini') -Destination $spamPackage
Copy-Item -LiteralPath (Join-Path $spamRoot 'vendor\bepinex\BepInEx\core') -Destination (Join-Path $spamPackage 'BepInEx') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $spamRoot 'src\SpamChinese\bin\Release\netstandard2.1\SpamChinese.dll') -Destination $spamPlugin
Copy-Item -LiteralPath (Join-Path $spamRoot 'data\verified.json') -Destination $spamPlugin
if (Test-Path -LiteralPath (Join-Path $spamRoot 'data\drafts.seed.json')) { Copy-Item -LiteralPath (Join-Path $spamRoot 'data\drafts.seed.json') -Destination $spamPlugin }
$spamFonts = Join-Path $spamPlugin 'fonts'
New-Item -ItemType Directory -Path $spamFonts -Force | Out-Null
foreach ($spamFontFile in @('Body-ExtraBold.ttf','Title-KuaiLe.ttf','notosanssc-OFL.txt','zcoolkuaile-OFL.txt','sources-v2.json')) {
  Copy-Item -LiteralPath (Join-Path $spamRoot "data\fonts\$spamFontFile") -Destination $spamFonts
}
Copy-Item -LiteralPath (Join-Path $spamRoot 'scripts\install.ps1'),(Join-Path $spamRoot 'scripts\uninstall.ps1') -Destination $spamPackage
Copy-Item -LiteralPath (Join-Path $spamRoot 'scripts\install.cmd'),(Join-Path $spamRoot 'scripts\uninstall.cmd') -Destination $spamPackage
foreach ($spamScript in @('install.ps1','uninstall.ps1')) {
  $spamScriptPath=Join-Path $spamPackage $spamScript
  [IO.File]::WriteAllText($spamScriptPath,[IO.File]::ReadAllText($spamScriptPath),[Text.UTF8Encoding]::new($true))
}
Copy-Item -LiteralPath (Join-Path $spamRoot 'vendor\licenses') -Destination $spamPackage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $spamRoot 'README.md') -Destination $spamPackage
Copy-Item -LiteralPath (Join-Path $spamRoot 'artifacts\验证记录-0.2.md') -Destination (Join-Path $spamPackage '验证记录.md')
Copy-Item -LiteralPath (Join-Path $spamRoot 'review\手动验收流程-0.2.md'),(Join-Path $spamRoot 'review\手动验收记录-0.2.csv'),(Join-Path $spamRoot 'artifacts\字体预览-0.2.svg') -Destination $spamPackage
$spamManifest = @{schema=1;version=$Version;testedGameBuild='25941248';gameAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $GameDir 'SPAM_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash;files=@()}
foreach ($spamFile in Get-ChildItem -LiteralPath $spamPackage -File -Recurse) {
  if ($spamFile.Name -eq 'package-manifest.json') { continue }
  $spamManifest.files += @{path=[IO.Path]::GetRelativePath($spamPackage,$spamFile.FullName);sha256=(Get-FileHash -LiteralPath $spamFile.FullName -Algorithm SHA256).Hash}
}
$spamManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $spamPackage 'package-manifest.json') -Encoding utf8
Compress-Archive -Path (Join-Path $spamPackage '*') -DestinationPath (Join-Path $spamRoot "artifacts\SPAM-zhCN-$Version.zip") -CompressionLevel Fastest -Force
Write-Output "Package ready: $spamPackage"
