$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$publishDirectory = Join-Path $repositoryRoot 'artifacts\SmartPOS-win-x64'
$executablePath = Join-Path $publishDirectory 'SmartPOS.App.exe'
$settingsPath = Join-Path $publishDirectory 'appsettings.json'
$installScriptPath = Join-Path $publishDirectory 'Install.ps1'
$installCommandPath = Join-Path $publishDirectory 'Install.cmd'
$uninstallScriptPath = Join-Path $publishDirectory 'Uninstall.ps1'
$outputPath = Join-Path $repositoryRoot 'artifacts\SmartPOS-Setup.exe'
$sedPath = Join-Path $repositoryRoot 'artifacts\SmartPOS-Setup.sed'
$iexpressPath = Join-Path $env:WINDIR 'System32\iexpress.exe'

if (-not (Test-Path -LiteralPath $iexpressPath -PathType Leaf)) {
    throw 'Windows IExpress is required to build the setup executable.'
}
foreach ($file in @($executablePath, $settingsPath)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Publish the self-contained x64 application first. Missing: $file"
    }
}

$iconScriptPath = Join-Path $PSScriptRoot 'SmartPOS.ico'
$iconPublishPath = Join-Path $publishDirectory 'SmartPOS.ico'
if (Test-Path -LiteralPath $iconScriptPath -PathType Leaf) {
    Copy-Item -LiteralPath $iconScriptPath -Destination $iconPublishPath -Force
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $installScriptPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.cmd') -Destination $installCommandPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $uninstallScriptPath -Force

$files = @($executablePath, $settingsPath, $installScriptPath, $installCommandPath, $uninstallScriptPath)
if (Test-Path -LiteralPath $iconPublishPath -PathType Leaf) {
    $files += $iconPublishPath
}
$fileSection = for ($index = 0; $index -lt $files.Count; $index++) { "%FILE$index%=" }
$stringSection = for ($index = 0; $index -lt $files.Count; $index++) { "FILE$index=`"$([System.IO.Path]::GetFileName($files[$index]))`"" }

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$outputPath
FriendlyName=SmartPOS Setup
AppLaunched=cmd.exe /c Install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
$($stringSection -join "`r`n")
[SourceFiles]
SourceFiles0=$publishDirectory\
[SourceFiles0]
$($fileSection -join "`r`n")
"@
$null = Set-Content -LiteralPath $sedPath -Value $sed -Encoding ASCII
if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
    $artifactsRoot = (Resolve-Path (Join-Path $repositoryRoot 'artifacts')).Path.TrimEnd('\') + '\'
    $resolvedOutputPath = [System.IO.Path]::GetFullPath($outputPath)
    if (-not $resolvedOutputPath.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The generated installer target must remain inside the repository artifacts directory.'
    }
    Remove-Item -LiteralPath $resolvedOutputPath -Force
}
$iexpress = Start-Process -FilePath $iexpressPath -ArgumentList @('/N', '/Q', $sedPath) -Wait -PassThru
if ($iexpress.ExitCode -ne 0) { throw "IExpress failed with exit code $($iexpress.ExitCode)." }
if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) { throw 'IExpress did not create the setup executable.' }

$iconScript = Join-Path $PSScriptRoot 'Set-ExeIcon.py'
$iconPath = Join-Path $PSScriptRoot 'SmartPOS.ico'
if ((Test-Path -LiteralPath $iconScript -PathType Leaf) -and (Test-Path -LiteralPath $iconPath -PathType Leaf)) {
    try {
        & python $iconScript $outputPath $iconPath | Out-Null
    } catch {
        Write-Warning "Could not update setup executable icon: $_"
    }
}

Get-Item -LiteralPath $outputPath | Select-Object FullName, Length, LastWriteTime
