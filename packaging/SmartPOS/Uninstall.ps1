param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\SmartPOS'),
    [string]$StartMenuDirectory = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\SmartPOS'),
    [string]$DesktopShortcutPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'SmartPOS.lnk'),
    [string]$UninstallRegistryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SmartPOS'
)

$ErrorActionPreference = 'Stop'

if (Get-Process -Name 'SmartPOS.App' -ErrorAction SilentlyContinue) {
    throw 'Close SmartPOS before uninstalling it. Your database and logs in AppData will be kept.'
}

Remove-Item -LiteralPath $UninstallRegistryKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $StartMenuDirectory -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $DesktopShortcutPath -PathType Leaf) {
    Remove-Item -LiteralPath $DesktopShortcutPath -Force -ErrorAction SilentlyContinue
}
$command = 'timeout /t 2 /nobreak >nul & rmdir /s /q "{0}"' -f $InstallDirectory
Start-Process -FilePath $env:ComSpec -ArgumentList @('/c', $command) -WindowStyle Hidden

