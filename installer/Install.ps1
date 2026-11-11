<#
.SYNOPSIS
Installs Linux File Reader for Windows locally with Start Menu and Desktop shortcuts.
#>

param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\Linux File Reader",
    [switch]$CreateDesktopShortcut = $true,
    [switch]$LaunchApp = $true
)

$ErrorActionPreference = "Stop"
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  Installing Linux File Reader for Windows..." -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

# 1. Paths
$repoRoot = Resolve-Path "$PSScriptRoot\.."
$guiPublish = "$repoRoot\src\LinuxReader.Gui\bin\Release\net8.0-windows\win-x64\publish"
$cliPublish = "$repoRoot\src\LinuxReader.Cli\bin\Release\net8.0\win-x64\publish"
$iconSource = "$repoRoot\src\LinuxReader.Gui\Assets\app.ico"

# Build & publish if binaries do not exist
if (-not (Test-Path "$guiPublish\LinuxReader.exe")) {
    Write-Host "Building Release binaries..." -ForegroundColor Yellow
    dotnet publish "$repoRoot\src\LinuxReader.Gui\LinuxReader.Gui.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
}
if (-not (Test-Path "$cliPublish\linux-reader-cli.exe")) {
    dotnet publish "$repoRoot\src\LinuxReader.Cli\LinuxReader.Cli.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
}

# 2. Create Target Directory
Write-Host "Installing to: $InstallDir" -ForegroundColor Green
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# 3. Copy Application Files
Copy-Item -Path "$guiPublish\LinuxReader.exe" -Destination "$InstallDir\LinuxReader.exe" -Force
Copy-Item -Path "$cliPublish\linux-reader-cli.exe" -Destination "$InstallDir\linux-reader-cli.exe" -Force
Copy-Item -Path $iconSource -Destination "$InstallDir\app.ico" -Force
Copy-Item -Path "$PSScriptRoot\Uninstall.ps1" -Destination "$InstallDir\Uninstall.ps1" -Force

# 4. Create Shortcuts using WScript.Shell
$wsh = New-Object -ComObject WScript.Shell

# Start Menu Shortcut
$startMenuPrograms = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$appStartFolder = Join-Path $startMenuPrograms "Linux File Reader"
New-Item -ItemType Directory -Force -Path $appStartFolder | Out-Null

$shortcut = $wsh.CreateShortcut("$appStartFolder\Linux File Reader.lnk")
$shortcut.TargetPath = "$InstallDir\LinuxReader.exe"
$shortcut.WorkingDirectory = $InstallDir
$shortcut.IconLocation = "$InstallDir\app.ico,0"
$shortcut.Description = "Safe, read-only Linux filesystem reader for Windows."
$shortcut.Save()

$cliShortcut = $wsh.CreateShortcut("$appStartFolder\Linux Reader CLI.lnk")
$cliShortcut.TargetPath = "$InstallDir\linux-reader-cli.exe"
$cliShortcut.WorkingDirectory = $InstallDir
$cliShortcut.IconLocation = "$InstallDir\app.ico,0"
$cliShortcut.Description = "Linux File Reader Command Line Tool"
$cliShortcut.Save()

# Desktop Shortcut
if ($CreateDesktopShortcut) {
    $desktopFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
    $deskShortcut = $wsh.CreateShortcut("$desktopFolder\Linux File Reader.lnk")
    $deskShortcut.TargetPath = "$InstallDir\LinuxReader.exe"
    $deskShortcut.WorkingDirectory = $InstallDir
    $deskShortcut.IconLocation = "$InstallDir\app.ico,0"
    $deskShortcut.Description = "Safe, read-only Linux filesystem reader for Windows."
    $deskShortcut.Save()
    Write-Host "Created Desktop shortcut." -ForegroundColor Gray
}

# 5. Register in Windows Add/Remove Programs (Installed apps)
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LinuxReader"
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name "DisplayName" -Value "Linux File Reader for Windows"
Set-ItemProperty -Path $uninstallKey -Name "DisplayVersion" -Value "1.2.0"
Set-ItemProperty -Path $uninstallKey -Name "Publisher" -Value "Naveen Kumar Yeluri & Antigravity"
Set-ItemProperty -Path $uninstallKey -Name "DisplayIcon" -Value "$InstallDir\app.ico"
Set-ItemProperty -Path $uninstallKey -Name "InstallLocation" -Value $InstallDir
Set-ItemProperty -Path $uninstallKey -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$InstallDir\Uninstall.ps1`""
Set-ItemProperty -Path $uninstallKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name "NoRepair" -Value 1 -Type DWord

Write-Host "Registered in Windows Installed Apps." -ForegroundColor Green
Write-Host "Installation completed successfully!" -ForegroundColor Cyan

if ($LaunchApp) {
    Write-Host "Launching Linux File Reader..." -ForegroundColor Green
    Start-Process -FilePath "$InstallDir\LinuxReader.exe"
}
