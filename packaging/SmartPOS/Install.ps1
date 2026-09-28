param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\SmartPOS'),
    [string]$StartMenuDirectory = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\SmartPOS'),
    [string]$UninstallRegistryKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SmartPOS',
    [switch]$SkipLaunch,
    [switch]$Silent
)

$ErrorActionPreference = 'Stop'
$sourceDirectory = $PSScriptRoot
$applicationFile = 'SmartPOS.App.exe'
$iconFile = 'SmartPOS.ico'

# Pre-load presentation framework
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase -ErrorAction SilentlyContinue

if (Get-Process -Name 'SmartPOS.App' -ErrorAction SilentlyContinue) {
    if ($Silent) {
        throw 'Close SmartPOS before installing an update.'
    } else {
        [System.Windows.MessageBox]::Show("Please close SmartPOS before running the installer.", "SmartPOS Installer", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning)
        exit 1
    }
}

foreach ($requiredFile in @($applicationFile, 'appsettings.json', 'Uninstall.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory $requiredFile) -PathType Leaf)) {
        throw "Installer payload is missing $requiredFile."
    }
}

function Execute-Step1 {
    # Step 1: Create directories
    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $StartMenuDirectory -Force | Out-Null
}

function Execute-Step2 {
    # Step 2: Copy files
    Copy-Item -LiteralPath (Join-Path $sourceDirectory $applicationFile) -Destination $InstallDirectory -Force
    Copy-Item -LiteralPath (Join-Path $sourceDirectory 'appsettings.json') -Destination $InstallDirectory -Force
    Copy-Item -LiteralPath (Join-Path $sourceDirectory 'Uninstall.ps1') -Destination $InstallDirectory -Force
    
    if (Test-Path -LiteralPath (Join-Path $sourceDirectory $iconFile) -PathType Leaf) {
        Copy-Item -LiteralPath (Join-Path $sourceDirectory $iconFile) -Destination $InstallDirectory -Force
    }
}

function Execute-Step3 {
    # Step 3: Create shortcuts on Desktop (all active desktop paths) and Start Menu
    $installedAppPath = Join-Path $InstallDirectory $applicationFile
    $installedIconPath = Join-Path $InstallDirectory $iconFile
    $targetIcon = if (Test-Path -LiteralPath $installedIconPath -PathType Leaf) { $installedIconPath } else { $installedAppPath }

    $shell = New-Object -ComObject WScript.Shell

    # Start Menu shortcut
    $startShortcut = $shell.CreateShortcut((Join-Path $StartMenuDirectory 'SmartPOS.lnk'))
    $startShortcut.TargetPath = $installedAppPath
    $startShortcut.WorkingDirectory = $InstallDirectory
    $startShortcut.IconLocation = "$targetIcon,0"
    $startShortcut.Description = 'SmartPOS offline point-of-sale system'
    $startShortcut.Save()

    # Desktop shortcuts (Handle User Desktop, Shell Desktop, and OneDrive Desktop)
    $desktopLocations = @(
        $shell.SpecialFolders.Item('Desktop'),
        [Environment]::GetFolderPath('Desktop'),
        (Join-Path $env:USERPROFILE 'Desktop'),
        (Join-Path $env:USERPROFILE 'OneDrive\Desktop')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Container) } | Select-Object -Unique

    foreach ($dt in $desktopLocations) {
        $desktopShortcut = $shell.CreateShortcut((Join-Path $dt 'SmartPOS.lnk'))
        $desktopShortcut.TargetPath = $installedAppPath
        $desktopShortcut.WorkingDirectory = $InstallDirectory
        $desktopShortcut.IconLocation = "$targetIcon,0"
        $desktopShortcut.Description = 'SmartPOS Point of Sale'
        $desktopShortcut.Save()
    }
}

function Execute-Step4 {
    # Step 4: Configure Registry
    $installedAppPath = Join-Path $InstallDirectory $applicationFile
    $installedIconPath = Join-Path $InstallDirectory $iconFile
    $targetIcon = if (Test-Path -LiteralPath $installedIconPath -PathType Leaf) { $installedIconPath } else { $installedAppPath }

    $uninstallCommand = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f (Join-Path $InstallDirectory 'Uninstall.ps1')
    New-Item -Path $UninstallRegistryKey -Force | Out-Null
    Set-ItemProperty -Path $UninstallRegistryKey -Name DisplayName -Value 'SmartPOS - Point of Sale'
    Set-ItemProperty -Path $UninstallRegistryKey -Name DisplayVersion -Value '1.0.0'
    Set-ItemProperty -Path $UninstallRegistryKey -Name Publisher -Value 'SmartPOS'
    Set-ItemProperty -Path $UninstallRegistryKey -Name InstallLocation -Value $InstallDirectory
    Set-ItemProperty -Path $UninstallRegistryKey -Name DisplayIcon -Value $targetIcon
    Set-ItemProperty -Path $UninstallRegistryKey -Name UninstallString -Value $uninstallCommand
    Set-ItemProperty -Path $UninstallRegistryKey -Name NoModify -Value 1 -Type DWord
    Set-ItemProperty -Path $UninstallRegistryKey -Name NoRepair -Value 1 -Type DWord
}

if ($Silent) {
    Execute-Step1
    Execute-Step2
    Execute-Step3
    Execute-Step4
    if (-not $SkipLaunch) {
        Start-Process -FilePath (Join-Path $InstallDirectory $applicationFile) -WorkingDirectory $InstallDirectory
    }
    exit 0
}

# WPF GUI Setup Window
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="SmartPOS Setup" Height="430" Width="560"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize"
        Background="#0F172A" WindowStyle="SingleBorderWindow"
        FontFamily="Segoe UI">
    <Window.Resources>
        <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="#F8FAFC"/>
        </Style>
    </Window.Resources>
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <Border Grid.Row="0" Background="#1E293B" CornerRadius="12" Padding="16" Margin="0,0,0,16" BorderBrush="#334155" BorderThickness="1">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <Border Grid.Column="0" Width="56" Height="56" CornerRadius="12" Background="#0284C7" Margin="0,0,16,0">
                    <TextBlock Text="POS" FontSize="20" FontWeight="Bold" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                </Border>
                <StackPanel Grid.Column="1" VerticalAlignment="Center">
                    <TextBlock Text="SmartPOS Setup" FontSize="22" FontWeight="Bold" Foreground="#38BDF8"/>
                    <TextBlock Text="Fast &amp; Modern Point of Sale System" FontSize="13" Foreground="#94A3B8" Margin="0,2,0,0"/>
                </StackPanel>
            </Grid>
        </Border>

        <!-- Body / Progress Area -->
        <Border Grid.Row="1" Background="#1E293B" CornerRadius="12" Padding="20" BorderBrush="#334155" BorderThickness="1">
            <StackPanel VerticalAlignment="Center">
                <TextBlock x:Name="StatusLabel" Text="Preparing installation..." FontSize="14" FontWeight="SemiBold" Foreground="#E2E8F0" Margin="0,0,0,12"/>
                
                <!-- Modern Progress Bar -->
                <Grid Height="24" Margin="0,0,0,10">
                    <Border Background="#0F172A" CornerRadius="12" BorderBrush="#334155" BorderThickness="1"/>
                    <ProgressBar x:Name="InstallProgressBar" Height="22" Minimum="0" Maximum="100" Value="0"
                                 Foreground="#0EA5E9" Background="Transparent" BorderThickness="0" Margin="1"/>
                </Grid>

                <DockPanel Margin="0,0,0,12">
                    <TextBlock x:Name="DetailLabel" Text="Checking system requirements..." FontSize="12" Foreground="#94A3B8" DockPanel.Dock="Left"/>
                    <TextBlock x:Name="PercentLabel" Text="0%" FontSize="12" FontWeight="Bold" Foreground="#38BDF8" DockPanel.Dock="Right"/>
                </DockPanel>

                <CheckBox x:Name="LaunchCheckBox" Content="Launch SmartPOS now" IsChecked="True" Foreground="#CBD5E1" Margin="0,8,0,0" FontSize="13" Visibility="Hidden"/>
            </StackPanel>
        </Border>

        <!-- Footer / Action Buttons -->
        <DockPanel Grid.Row="2" Margin="0,16,0,0">
            <TextBlock Text="SmartPOS v1.0.0" FontSize="12" Foreground="#64748B" VerticalAlignment="Center"/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Button x:Name="FinishButton" Content="Finish" Width="100" Height="36" Background="#0EA5E9" Foreground="White" FontWeight="SemiBold"
                        BorderThickness="0" Cursor="Hand" Visibility="Hidden"/>
            </StackPanel>
        </DockPanel>
    </Grid>
</Window>
"@

$reader = New-Object System.Xml.XmlNodeReader $xaml
$window = [System.Windows.Markup.XamlReader]::Load($reader)

$statusLabel = $window.FindName("StatusLabel")
$detailLabel = $window.FindName("DetailLabel")
$percentLabel = $window.FindName("PercentLabel")
$progressBar = $window.FindName("InstallProgressBar")
$launchCheckBox = $window.FindName("LaunchCheckBox")
$finishButton = $window.FindName("FinishButton")

$finishButton.Add_Click({
    if ($launchCheckBox.IsChecked -and -not $SkipLaunch) {
        Start-Process -FilePath (Join-Path $InstallDirectory $applicationFile) -WorkingDirectory $InstallDirectory
    }
    $window.Close()
})

$script:currentStep = 0
$dispatcherTimer = New-Object System.Windows.Threading.DispatcherTimer
$dispatcherTimer.Interval = [TimeSpan]::FromMilliseconds(350)

$dispatcherTimer.Add_Tick({
    $script:currentStep++
    switch ($script:currentStep) {
        1 {
            $progressBar.Value = 20
            $percentLabel.Text = "20%"
            $statusLabel.Text = "Installing SmartPOS..."
            $detailLabel.Text = "Creating installation directories..."
            Execute-Step1
        }
        2 {
            $progressBar.Value = 50
            $percentLabel.Text = "50%"
            $detailLabel.Text = "Copying SmartPOS application files..."
            Execute-Step2
        }
        3 {
            $progressBar.Value = 75
            $percentLabel.Text = "75%"
            $detailLabel.Text = "Creating Desktop and Start Menu shortcuts..."
            Execute-Step3
        }
        4 {
            $progressBar.Value = 90
            $percentLabel.Text = "90%"
            $detailLabel.Text = "Registering application in Windows..."
            Execute-Step4
        }
        5 {
            $progressBar.Value = 100
            $percentLabel.Text = "100%"
            $statusLabel.Text = "Installation Complete!"
            $statusLabel.Foreground = [System.Windows.Media.Brushes]::MediumSeaGreen
            $detailLabel.Text = "SmartPOS has been installed successfully."
            $launchCheckBox.Visibility = [System.Windows.Visibility]::Visible
            $finishButton.Visibility = [System.Windows.Visibility]::Visible
            $dispatcherTimer.Stop()
        }
    }
})

$window.Add_Loaded({
    $dispatcherTimer.Start()
})

$null = $window.ShowDialog()
