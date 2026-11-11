<#
.SYNOPSIS
Uninstalls Linux File Reader for Windows cleanly.
#>

$ErrorActionPreference = "SilentlyContinue"

$InstallDir = $PSScriptRoot

# 1. Remove Desktop Shortcut
$desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)) "Linux File Reader.lnk"
if (Test-Path $desktopShortcut) {
    Remove-Item $desktopShortcut -Force
}

# 2. Remove Start Menu folder
$startMenuPrograms = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$appStartFolder = Join-Path $startMenuPrograms "Linux File Reader"
if (Test-Path $appStartFolder) {
    Remove-Item $appStartFolder -Recurse -Force
}

# 3. Remove Registry Entry
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LinuxReader"
if (Test-Path $uninstallKey) {
    Remove-Item -Path $uninstallKey -Recurse -Force
}

# 4. Remove installation files (schedule self-deletion of install folder)
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 & rmdir /s /q `"$InstallDir`"" -WindowStyle Hidden

[System.Windows.Forms.MessageBox]::Show("Linux File Reader for Windows has been successfully uninstalled.", "Uninstall Complete", 0, 64)
