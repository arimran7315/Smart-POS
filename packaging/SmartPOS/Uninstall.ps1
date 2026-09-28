param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\SmartPOS'),
    [string]$StartMenuDirectory = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\SmartPOS'),
    [string]$UninstallRegistryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SmartPOS'
)

$ErrorActionPreference = 'Stop'

if (Get-Process -Name 'SmartPOS.App' -ErrorAction SilentlyContinue) {
    throw 'Close SmartPOS before uninstalling it. Your database and logs in AppData will be kept.'
}

Remove-Item -LiteralPath $UninstallRegistryKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $StartMenuDirectory -Recurse -Force -ErrorAction SilentlyContinue

$shell = New-Object -ComObject WScript.Shell
$desktopLocations = @(
    $shell.SpecialFolders.Item('Desktop'),
    [Environment]::GetFolderPath('Desktop'),
    (Join-Path $env:USERPROFILE 'Desktop'),
    (Join-Path $env:USERPROFILE 'OneDrive\Desktop')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Container) } | Select-Object -Unique

foreach ($dt in $desktopLocations) {
    $lnk = Join-Path $dt 'SmartPOS.lnk'
    if (Test-Path -LiteralPath $lnk -PathType Leaf) {
        Remove-Item -LiteralPath $lnk -Force -ErrorAction SilentlyContinue
    }
}

$command = 'timeout /t 2 /nobreak >nul & rmdir /s /q "{0}"' -f $InstallDirectory
Start-Process -FilePath $env:ComSpec -ArgumentList @('/c', $command) -WindowStyle Hidden
