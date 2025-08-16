# Win Key Remapper - Complete Installer & Manager
# Run as Administrator for installation/uninstallation
# Can run as regular user for startup management

param(
    [Parameter(HelpMessage = "Action to perform: install, uninstall, startup-enable, startup-disable, status, logs, or menu")]
    [ValidateSet("install", "uninstall", "startup-enable", "startup-disable", "status", "logs", "menu")]
    [string]$Action = "menu"
)

# Configuration
$AppName = "Win Key Remapper"
$ExeName = "WinKey_CommandPalette_Replacement.exe"
$ZipName = "WinKey_CommandPalette_Replacement.zip"
$InstallPath = "$env:ProgramFiles\$AppName"
$AppPath = Join-Path $InstallPath $ExeName
$DownloadUrl = "https://github.com/ArjunC1234/WinKey_CommandPallette_Replacement/releases/latest/download/$ZipName"

# Function to get best desktop path
function Get-DesktopPath {
    $desktopPaths = @(
        "$env:USERPROFILE\Desktop",
        "$env:PUBLIC\Desktop",
        "$env:HOMEDRIVE$env:HOMEPATH\Desktop"
    )
    
    foreach ($path in $desktopPaths) {
        if (Test-Path $path) {
            return $path
        }
    }
    
    return "$env:USERPROFILE\Desktop"
}

# Paths for shortcuts
$StartMenuShortcut = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\$AppName.lnk"
$DesktopShortcut = "$(Get-DesktopPath)\$AppName.lnk"

# Registry path for startup
$StartupRegPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$StartupRegName = "WinKeyRemapper"

# Scheduled Task name
$TaskName = "WinKeyRemapper"

# Colors for output
$ColorSuccess = "Green"
$ColorWarning = "Yellow"
$ColorError = "Red"
$ColorInfo = "Cyan"
$ColorPrompt = "White"

function Write-ColoredOutput {
    param([string]$Message, [string]$Color = "White")
    Write-Host $Message -ForegroundColor $Color
}

function Test-AdminRights {
    return ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
}

function Show-Header {
    Clear-Host
    Write-ColoredOutput "================================================================" $ColorInfo
    Write-ColoredOutput "           Win Key Remapper - Installer & Manager              " $ColorInfo
    Write-ColoredOutput "================================================================" $ColorInfo
    Write-ColoredOutput ""
}

function Show-Menu {
    Show-Header
    Write-ColoredOutput "Choose an action:" $ColorPrompt
    Write-ColoredOutput ""
    Write-ColoredOutput "1. Install $AppName" $ColorPrompt
    Write-ColoredOutput "2. Uninstall $AppName" $ColorPrompt
    Write-ColoredOutput "3. Enable startup (run when Windows starts)" $ColorPrompt
    Write-ColoredOutput "4. Disable startup" $ColorPrompt
    Write-ColoredOutput "5. Check status" $ColorPrompt
    Write-ColoredOutput "6. View startup logs" $ColorPrompt
    Write-ColoredOutput "7. Exit" $ColorPrompt
    Write-ColoredOutput ""
    
    $choice = Read-Host "Enter your choice (1-7)"
    
    switch ($choice) {
        "1" { Install-Application }
        "2" { Uninstall-Application }
        "3" { Enable-Startup }
        "4" { Disable-Startup }
        "5" { Show-Status }
        "6" { Show-StartupLogs }
        "7" { exit 0 }
        default { 
            Write-ColoredOutput "Invalid choice. Please try again." $ColorError
            Start-Sleep 2
            Show-Menu 
        }
    }
}

function Download-FileWithProgress {
    param([string]$Url, [string]$Destination)
    
    try {
        Write-ColoredOutput "Downloading from: $Url" $ColorInfo
        
        # Create WebClient with progress tracking
        $webClient = New-Object System.Net.WebClient
        
        # Register progress event
        $progressEvent = Register-ObjectEvent -InputObject $webClient -EventName "DownloadProgressChanged" -Action {
            $percent = [Math]::Round($EventArgs.ProgressPercentage, 1)
            $received = [Math]::Round($EventArgs.BytesReceived / 1MB, 2)
            $total = [Math]::Round($EventArgs.TotalBytesToReceive / 1MB, 2)
            Write-Progress -Activity "Downloading $using:AppName" -Status "$received MB / $total MB ($percent%)" -PercentComplete $percent
        }
        
        # Download the file
        $webClient.DownloadFile($Url, $Destination)
        
        # Cleanup
        Unregister-Event -SourceIdentifier $progressEvent.Name
        $webClient.Dispose()
        Write-Progress -Activity "Downloading" -Completed
        
        Write-ColoredOutput "Download completed successfully!" $ColorSuccess
        return $true
    }
    catch {
        Write-ColoredOutput "Download failed: $_" $ColorError
        return $false
    }
}

# ===== SCHEDULED TASK FUNCTIONS =====

function Create-AdminStartupTask {
    param([string]$AppPath, [string]$AppName)
    
    try {
        # Create intelligent startup script that waits for PowerToys
        $startupScript = Create-IntelligentStartupScript -AppPath $AppPath -AppName $AppName
        
        # Create scheduled task action to run the startup script
        $action = New-ScheduledTaskAction -Execute "cmd.exe" -Argument "/c `"$startupScript`""
        
        # Create trigger for user logon
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
        
        # Create principal with highest privileges (no UAC prompt)
        $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
        
        # Create task settings
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -DontStopOnIdleEnd
        
        # Register the scheduled task
        Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
        
        Write-ColoredOutput "Created scheduled task for admin startup (no UAC prompts!)" $ColorSuccess
        Write-ColoredOutput "Task name: $TaskName" $ColorInfo
        return $true
    }
    catch {
        Write-ColoredOutput "Failed to create scheduled task: $_" $ColorError
        return $false
    }
}

function Remove-AdminStartupTask {
    try {
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        if ($task) {
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
            Write-ColoredOutput "Removed scheduled task: $TaskName" $ColorSuccess
            return $true
        } else {
            Write-ColoredOutput "Scheduled task not found: $TaskName" $ColorInfo
            return $false
        }
    }
    catch {
        Write-ColoredOutput "Failed to remove scheduled task: $_" $ColorError
        return $false
    }
}

function Test-AdminStartupTask {
    try {
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        return ($task -ne $null)
    }
    catch {
        return $false
    }
}

# ===== REGISTRY STARTUP FUNCTIONS =====

function Enable-RegistryStartup {
    param([string]$AppPath, [string]$AppName)
    
    try {
        # Create intelligent startup script that waits for PowerToys
        $startupScript = Create-IntelligentStartupScript -AppPath $AppPath -AppName $AppName
        
        # Register the script (not the direct EXE) for startup
        Set-ItemProperty -Path $StartupRegPath -Name $StartupRegName -Value "`"$startupScript`""
        Write-ColoredOutput "Added $AppName to Windows startup with PowerToys dependency check" $ColorSuccess
        Write-ColoredOutput "⚠ Warning: This method will show UAC prompts on startup" $ColorWarning
        return $true
    }
    catch {
        Write-ColoredOutput "Failed to add to startup: $_" $ColorError
        return $false
    }
}

function Disable-RegistryStartup {
    try {
        $regValue = Get-ItemProperty -Path $StartupRegPath -Name $StartupRegName -ErrorAction SilentlyContinue
        if ($regValue) {
            Remove-ItemProperty -Path $StartupRegPath -Name $StartupRegName -Force
            Write-ColoredOutput "Removed from Windows startup registry" $ColorSuccess
            return $true
        } else {
            Write-ColoredOutput "Startup entry not found in registry" $ColorInfo
            return $false
        }
    }
    catch {
        Write-ColoredOutput "Failed to remove from startup: $_" $ColorError
        return $false
    }
}

function Test-RegistryStartup {
    $regValue = Get-ItemProperty -Path $StartupRegPath -Name $StartupRegName -ErrorAction SilentlyContinue
    return ($regValue -ne $null)
}

function Create-IntelligentStartupScript {
    param([string]$AppPath, [string]$AppName)
    
    # Create startup script that waits for PowerToys
    $scriptContent = @"
@echo off
REM Intelligent Startup Script for Win Key Remapper
REM Waits for PowerToys to load first, then starts Win Key Remapper

echo %date% %time% - Starting Win Key Remapper intelligent startup >> "C:\temp\winkey-startup.log"

REM Wait for desktop to be ready (basic startup delay)
echo %date% %time% - Waiting for desktop stability... >> "C:\temp\winkey-startup.log"
timeout /t 5 /nobreak > nul

REM Check if PowerToys is running (try for up to 60 seconds)
set /a counter=0
:check_powertoys
set /a counter+=1

REM Check for PowerToys processes
tasklist /FI "IMAGENAME eq PowerToys.exe" | find /i "PowerToys.exe" > nul
if %errorlevel% == 0 (
    echo %date% %time% - PowerToys found running, proceeding... >> "C:\temp\winkey-startup.log"
    goto start_winkey
)

tasklist /FI "IMAGENAME eq PowerToys.Settings.exe" | find /i "PowerToys.Settings.exe" > nul
if %errorlevel% == 0 (
    echo %date% %time% - PowerToys Settings found, PowerToys likely running... >> "C:\temp\winkey-startup.log"
    goto start_winkey
)

REM Check for PowerToys Run (Command Palette component)
tasklist /FI "IMAGENAME eq PowerToys.PowerLauncher.exe" | find /i "PowerToys.PowerLauncher.exe" > nul
if %errorlevel% == 0 (
    echo %date% %time% - PowerToys PowerLauncher found, proceeding... >> "C:\temp\winkey-startup.log"
    goto start_winkey
)

REM If not found, wait and try again (up to 12 times = 60 seconds)
if %counter% LSS 12 (
    echo %date% %time% - PowerToys not found yet, waiting... (attempt %counter%/12) >> "C:\temp\winkey-startup.log"
    timeout /t 5 /nobreak > nul
    goto check_powertoys
)

REM PowerToys not found after timeout - start anyway with warning
echo %date% %time% - WARNING: PowerToys not detected after 60 seconds, starting Win Key Remapper anyway >> "C:\temp\winkey-startup.log"

:start_winkey
echo %date% %time% - Starting Win Key Remapper... >> "C:\temp\winkey-startup.log"

REM Change to app directory
cd /d "$($InstallPath.Replace('\', '\\'))"

REM Check if our EXE exists
if exist "$ExeName" (
    echo %date% %time% - Launching $ExeName >> "C:\temp\winkey-startup.log"
    start "" "$ExeName"
    
    REM Wait a moment and verify it started
    timeout /t 3 /nobreak > nul
    tasklist | findstr "$($ExeName.Replace('.exe', ''))" >> "C:\temp\winkey-startup.log" 2>&1
    if %errorlevel% == 0 (
        echo %date% %time% - SUCCESS: Win Key Remapper started and confirmed running >> "C:\temp\winkey-startup.log"
    ) else (
        echo %date% %time% - WARNING: Win Key Remapper may not have started properly >> "C:\temp\winkey-startup.log"
    )
) else (
    echo %date% %time% - ERROR: $ExeName not found in $InstallPath >> "C:\temp\winkey-startup.log"
)

echo %date% %time% - Startup script completed >> "C:\temp\winkey-startup.log"
"@

    # Save the startup script
    $scriptPath = Join-Path $InstallPath "WinKeyRemapper-Startup.bat"
    
    # Create temp directory for logs
    if (!(Test-Path "C:\temp")) {
        New-Item -ItemType Directory -Path "C:\temp" -Force | Out-Null
    }
    
    # Write the script
    $scriptContent | Out-File -FilePath $scriptPath -Encoding ASCII -Force
    
    Write-ColoredOutput "Created intelligent startup script: $scriptPath" $ColorSuccess
    Write-ColoredOutput "Logs will be written to: C:\temp\winkey-startup.log" $ColorInfo
    
    return $scriptPath
}

function Create-Shortcut {
    param([string]$TargetPath, [string]$ShortcutPath, [string]$Description = "")
    
    try {
        # Ensure the directory exists
        $shortcutDir = Split-Path $ShortcutPath -Parent
        if (!(Test-Path $shortcutDir)) {
            New-Item -ItemType Directory -Path $shortcutDir -Force | Out-Null
        }
        
        # Create the shortcut
        $WScriptShell = New-Object -ComObject WScript.Shell
        $Shortcut = $WScriptShell.CreateShortcut($ShortcutPath)
        $Shortcut.TargetPath = $TargetPath
        $Shortcut.Description = $Description
        $Shortcut.WorkingDirectory = Split-Path $TargetPath -Parent
        $Shortcut.Save()
        
        # Release COM objects
        [System.Runtime.Interopservices.Marshal]::ReleaseComObject($Shortcut) | Out-Null
        [System.Runtime.Interopservices.Marshal]::ReleaseComObject($WScriptShell) | Out-Null
        
        # Verify the shortcut was created
        if (Test-Path $ShortcutPath) {
            Write-ColoredOutput "Created shortcut: $(Split-Path $ShortcutPath -Leaf)" $ColorSuccess
            return $true
        } else {
            throw "Shortcut file was not created"
        }
    }
    catch {
        # Try alternative desktop path if it's a desktop shortcut
        if ($ShortcutPath -like "*Desktop*") {
            try {
                Write-ColoredOutput "Trying alternative desktop location..." $ColorWarning
                
                # Try Public Desktop
                $publicDesktop = "$env:PUBLIC\Desktop\$(Split-Path $ShortcutPath -Leaf)"
                $WScriptShell = New-Object -ComObject WScript.Shell
                $Shortcut = $WScriptShell.CreateShortcut($publicDesktop)
                $Shortcut.TargetPath = $TargetPath
                $Shortcut.Description = $Description
                $Shortcut.WorkingDirectory = Split-Path $TargetPath -Parent
                $Shortcut.Save()
                
                [System.Runtime.Interopservices.Marshal]::ReleaseComObject($Shortcut) | Out-Null
                [System.Runtime.Interopservices.Marshal]::ReleaseComObject($WScriptShell) | Out-Null
                
                if (Test-Path $publicDesktop) {
                    Write-ColoredOutput "Created shortcut on Public Desktop: $(Split-Path $publicDesktop -Leaf)" $ColorSuccess
                    return $true
                }
            }
            catch {
                Write-ColoredOutput "Alternative desktop location also failed" $ColorWarning
            }
        }
        
        Write-ColoredOutput "Failed to create shortcut: $_" $ColorError
        Write-ColoredOutput "You can manually create a shortcut to: $TargetPath" $ColorInfo
        return $false
    }
}

function Stop-ApplicationProcess {
    try {
        # Try both possible process names
        $processNames = @("WinKey_CommandPallette_Replacement", "WinKeyRemapper", "WinKey_CommandPalette_Replacement")
        $foundProcesses = @()
        
        foreach ($name in $processNames) {
            $processes = Get-Process -Name $name -ErrorAction SilentlyContinue
            if ($processes) {
                $foundProcesses += $processes
            }
        }
        
        if ($foundProcesses.Count -gt 0) {
            Write-ColoredOutput "Stopping running instances..." $ColorWarning
            $foundProcesses | Stop-Process -Force
            Start-Sleep -Seconds 2
            Write-ColoredOutput "Stopped $($foundProcesses.Count) running instance(s)" $ColorSuccess
            return $true
        }
        else {
            Write-ColoredOutput "No running instances found" $ColorInfo
            return $true
        }
    }
    catch {
        Write-ColoredOutput "Failed to stop processes: $_" $ColorError
        return $false
    }
}

function Install-Application {
    Show-Header
    Write-ColoredOutput "Starting installation..." $ColorInfo
    Write-ColoredOutput ""
    
    # Check admin rights
    if (-not (Test-AdminRights)) {
        Write-ColoredOutput "ERROR: Installation requires Administrator privileges!" $ColorError
        Write-ColoredOutput "Please right-click PowerShell and select 'Run as Administrator'" $ColorWarning
        Write-ColoredOutput ""
        Read-Host "Press Enter to continue"
        Show-Menu
        return
    }
    
    try {
        # Stop any running instances
        Stop-ApplicationProcess
        
        # Create installation directory
        Write-ColoredOutput "Creating installation directory..." $ColorInfo
        if (!(Test-Path $InstallPath)) {
            New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
        }
        
        # Download the ZIP file
        $tempZip = Join-Path $env:TEMP $ZipName
        if (Download-FileWithProgress -Url $DownloadUrl -Destination $tempZip) {
            
            # Extract ZIP to installation directory
            Write-ColoredOutput "Extracting files to: $InstallPath..." $ColorInfo
            
            # Remove old installation if it exists
            if (Test-Path $InstallPath) {
                Write-ColoredOutput "Removing previous installation..." $ColorWarning
                Remove-Item $InstallPath -Recurse -Force
            }
            
            # Create installation directory
            New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
            
            # Extract the ZIP
            try {
                Expand-Archive -Path $tempZip -DestinationPath $InstallPath -Force
                Write-ColoredOutput "Extraction completed!" $ColorSuccess
                
                # List what was extracted (for debugging)
                $extractedFiles = Get-ChildItem $InstallPath
                Write-ColoredOutput "Extracted $($extractedFiles.Count) files:" $ColorInfo
                foreach ($file in $extractedFiles) {
                    Write-ColoredOutput "  - $($file.Name)" $ColorInfo
                }
                
                # Verify the main EXE exists
                if (Test-Path $AppPath) {
                    Write-ColoredOutput "Main executable found: $ExeName" $ColorSuccess
                } else {
                    throw "Main executable not found after extraction: $ExeName"
                }
                
                # Clean up temp ZIP
                Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
                
            } catch {
                throw "Failed to extract ZIP file: $_"
            }
            
            # Create Start Menu shortcut
            Write-ColoredOutput "Creating Start Menu shortcut..." $ColorInfo
            Create-Shortcut -TargetPath $AppPath -ShortcutPath $StartMenuShortcut -Description $AppName
            
            # Ask about desktop shortcut
            Write-ColoredOutput ""
            $createDesktop = Read-Host "Create desktop shortcut? (y/n)"
            if ($createDesktop -eq 'y' -or $createDesktop -eq 'Y') {
                Create-Shortcut -TargetPath $AppPath -ShortcutPath $DesktopShortcut -Description $AppName
            }
            
            # Ask about startup (simplified during installation)
            Write-ColoredOutput ""
            $addToStartup = Read-Host "Add to Windows startup? (y/n)"
            if ($addToStartup -eq 'y' -or $addToStartup -eq 'Y') {
                Configure-StartupDuringInstall -AppPath $AppPath -AppName $AppName
            }
            
            Write-ColoredOutput ""
            Write-ColoredOutput "================================================================" $ColorSuccess
            Write-ColoredOutput "                    INSTALLATION COMPLETE!                     " $ColorSuccess
            Write-ColoredOutput "================================================================" $ColorSuccess
            Write-ColoredOutput ""
            
            # Ask to start the application
            $startNow = Read-Host "Start $AppName now? (y/n)"
            if ($startNow -eq 'y' -or $startNow -eq 'Y') {
                Write-ColoredOutput "Starting $AppName..." $ColorInfo
                
                try {
                    # First verify the file exists and is executable
                    if (!(Test-Path $AppPath)) {
                        throw "Application file not found at: $AppPath"
                    }
                    
                    # Try to start the application
                    Write-ColoredOutput "Launching: $AppPath" $ColorInfo
                    
                    # Try method 1: Start as admin
                    try {
                        $process = Start-Process $AppPath -Verb RunAs -PassThru
                        Start-Sleep -Seconds 2
                        
                        if ($process -and !$process.HasExited) {
                            Write-ColoredOutput "$AppName started successfully (Admin mode)!" $ColorSuccess
                        } else {
                            throw "Process exited immediately"
                        }
                    }
                    catch {
                        Write-ColoredOutput "Admin start failed, trying normal mode..." $ColorWarning
                        
                        # Try method 2: Start normally
                        try {
                            $process = Start-Process $AppPath -PassThru
                            Start-Sleep -Seconds 2
                            
                            if ($process -and !$process.HasExited) {
                                Write-ColoredOutput "$AppName started successfully!" $ColorSuccess
                            } else {
                                throw "Process exited immediately in normal mode too"
                            }
                        }
                        catch {
                            Write-ColoredOutput "Normal start also failed, trying direct execution..." $ColorWarning
                            
                            # Try method 3: Direct execution
                            & $AppPath
                            Write-ColoredOutput "Attempted direct execution of $AppName" $ColorInfo
                        }
                    }
                }
                catch {
                    Write-Host "Failed to start app automatically" -ForegroundColor Red
                    Write-ColoredOutput "You can manually start it from: $AppPath" $ColorInfo
                    Write-ColoredOutput "Or use the Start Menu shortcut" $ColorInfo
                }
            }
            
        }
        else {
            throw "Download failed"
        }
        
    }
    catch {
        Write-ColoredOutput "Installation failed: $_" $ColorError
    }
    
    Write-ColoredOutput ""
    Read-Host "Press Enter to return to menu"
    Show-Menu
}

function Uninstall-Application {
    Show-Header
    Write-ColoredOutput "Starting uninstallation..." $ColorWarning
    Write-ColoredOutput ""
    
    # Check admin rights
    if (-not (Test-AdminRights)) {
        Write-ColoredOutput "ERROR: Uninstallation requires Administrator privileges!" $ColorError
        Write-ColoredOutput "Please right-click PowerShell and select 'Run as Administrator'" $ColorWarning
        Write-ColoredOutput ""
        Read-Host "Press Enter to continue"
        Show-Menu
        return
    }
    
    # Confirm uninstallation
    $confirm = Read-Host "Are you sure you want to uninstall $AppName? (y/n)"
    if ($confirm -ne 'y' -and $confirm -ne 'Y') {
        Write-ColoredOutput "Uninstallation cancelled." $ColorInfo
        Start-Sleep 2
        Show-Menu
        return
    }
    
    try {
        # Stop any running processes
        Stop-ApplicationProcess
        
        # Remove installation directory (including startup script)
        if (Test-Path $InstallPath) {
            Write-ColoredOutput "Removing installation files..." $ColorInfo
            Remove-Item $InstallPath -Recurse -Force
            Write-ColoredOutput "Removed: $InstallPath" $ColorSuccess
        }
        
        # Remove shortcuts and startup entries
        Write-ColoredOutput "Removing shortcuts and startup entries..." $ColorInfo
        
        # Remove both types of startup entries
        if (Test-RegistryStartup) {
            Disable-RegistryStartup
        }
        
        if (Test-AdminStartupTask) {
            Remove-AdminStartupTask
        }
        
        if (Test-Path $StartMenuShortcut) {
            Remove-Item $StartMenuShortcut -Force
            Write-ColoredOutput "Removed Start Menu shortcut" $ColorSuccess
        }
        
        if (Test-Path $DesktopShortcut) {
            Remove-Item $DesktopShortcut -Force
            Write-ColoredOutput "Removed desktop shortcut" $ColorSuccess
        }
        
        # Also check public desktop
        $publicDesktop = "$env:PUBLIC\Desktop\$AppName.lnk"
        if (Test-Path $publicDesktop) {
            Remove-Item $publicDesktop -Force
            Write-ColoredOutput "Removed public desktop shortcut" $ColorSuccess
        }
        
        Write-ColoredOutput ""
        Write-ColoredOutput "================================================================" $ColorSuccess
        Write-ColoredOutput "                  UNINSTALLATION COMPLETE!                     " $ColorSuccess
        Write-ColoredOutput "================================================================" $ColorSuccess
        Write-ColoredOutput "$AppName has been completely removed from your system." $ColorSuccess
        
    }
    catch {
        Write-ColoredOutput "Uninstallation failed: $_" $ColorError
    }
    
    Write-ColoredOutput ""
    Read-Host "Press Enter to return to menu"
    Show-Menu
}

function Configure-StartupDuringInstall {
    param([string]$AppPath, [string]$AppName)
    
    Write-ColoredOutput ""
    Write-ColoredOutput "Choose startup method:" $ColorPrompt
    Write-ColoredOutput "1. 🚀 Scheduled Task (RECOMMENDED - No UAC prompts)" $ColorSuccess
    Write-ColoredOutput "2. 📋 Registry (Shows UAC prompt on every boot)" $ColorWarning
    Write-ColoredOutput ""
    
    $choice = Read-Host "Enter your choice (1-2)"
    
    switch ($choice) {
        "1" {
            if (Create-AdminStartupTask -AppPath $AppPath -AppName $AppName) {
                Write-ColoredOutput "✅ Scheduled task startup configured (no UAC prompts)!" $ColorSuccess
            }
        }
        "2" {
            if (Enable-RegistryStartup -AppPath $AppPath -AppName $AppName) {
                Write-ColoredOutput "✅ Registry startup configured!" $ColorSuccess
            }
        }
        default {
            Write-ColoredOutput "Invalid choice, using recommended scheduled task method..." $ColorWarning
            if (Create-AdminStartupTask -AppPath $AppPath -AppName $AppName) {
                Write-ColoredOutput "✅ Scheduled task startup configured (no UAC prompts)!" $ColorSuccess
            }
        }
    }
}

function Enable-Startup {
    Show-Header
    Write-ColoredOutput "Choose startup method:" $ColorPrompt
    Write-ColoredOutput ""
    
    # Check if app is installed
    if (!(Test-Path $AppPath)) {
        Write-ColoredOutput "ERROR: $AppName is not installed!" $ColorError
        Write-ColoredOutput "Please install the application first." $ColorWarning
        Write-ColoredOutput ""
        Read-Host "Press Enter to continue"
        Show-Menu
        return
    }
    
    # Check if already enabled
    $regStartup = Test-RegistryStartup
    $taskStartup = Test-AdminStartupTask
    
    if ($regStartup -or $taskStartup) {
        Write-ColoredOutput "Startup is already enabled:" $ColorWarning
        if ($regStartup) { Write-ColoredOutput "  - Registry method (with UAC prompts)" $ColorWarning }
        if ($taskStartup) { Write-ColoredOutput "  - Scheduled task method (no UAC prompts)" $ColorSuccess }
        Write-ColoredOutput ""
        
        $replace = Read-Host "Replace current startup method? (y/n)"
        if ($replace -ne 'y' -and $replace -ne 'Y') {
            Show-Menu
            return
        }
        
        # Remove existing methods
        if ($regStartup) { Disable-RegistryStartup }
        if ($taskStartup) { Remove-AdminStartupTask }
    }
    
    Write-ColoredOutput "Choose startup method:" $ColorPrompt
    Write-ColoredOutput ""
    Write-ColoredOutput "1. 🚀 Scheduled Task (RECOMMENDED - No UAC prompts)" $ColorSuccess
    Write-ColoredOutput "2. 📋 Registry (Shows UAC prompt on every boot)" $ColorWarning
    Write-ColoredOutput "3. Cancel" $ColorInfo
    Write-ColoredOutput ""
    
    $choice = Read-Host "Enter your choice (1-3)"
    
    switch ($choice) {
        "1" {
            if (Create-AdminStartupTask -AppPath $AppPath -AppName $AppName) {
                Write-ColoredOutput ""
                Write-ColoredOutput "✅ SUCCESS: Scheduled task startup enabled!" $ColorSuccess
                Write-ColoredOutput "Benefits:" $ColorInfo
                Write-ColoredOutput "  - No UAC prompts on startup" $ColorSuccess
                Write-ColoredOutput "  - Waits for PowerToys to load first" $ColorSuccess
                Write-ColoredOutput "  - Automatic admin privileges" $ColorSuccess
                Write-ColoredOutput "  - Startup logging enabled" $ColorSuccess
            }
        }
        "2" {
            if (Enable-RegistryStartup -AppPath $AppPath -AppName $AppName) {
                Write-ColoredOutput ""
                Write-ColoredOutput "✅ Registry startup enabled!" $ColorSuccess
                Write-ColoredOutput "⚠ Note: You will see UAC prompts on each startup" $ColorWarning
            }
        }
        "3" {
            Write-ColoredOutput "Startup configuration cancelled." $ColorInfo
        }
        default {
            Write-ColoredOutput "Invalid choice." $ColorError
        }
    }
    
    Write-ColoredOutput ""
    Read-Host "Press Enter to return to menu"
    Show-Menu
}

function Disable-Startup {
    Show-Header
    Write-ColoredOutput "Disabling startup..." $ColorInfo
    Write-ColoredOutput ""
    
    $regStartup = Test-RegistryStartup
    $taskStartup = Test-AdminStartupTask
    
    if ($regStartup -or $taskStartup) {
        if ($regStartup) { 
            Disable-RegistryStartup 
        }
        if ($taskStartup) { 
            Remove-AdminStartupTask 
        }
        Write-ColoredOutput "$AppName will no longer start with Windows." $ColorSuccess
    }
    else {
        Write-ColoredOutput "$AppName startup is already disabled." $ColorInfo
    }
    
    Write-ColoredOutput ""
    Read-Host "Press Enter to return to menu"
    Show-Menu
}

function Show-Status {
    Show-Header
    Write-ColoredOutput "Current Status:" $ColorPrompt
    Write-ColoredOutput ""
    
    # Check installation status
    if (Test-Path $AppPath) {
        Write-ColoredOutput "✓ $AppName is installed" $ColorSuccess
        Write-ColoredOutput "  Location: $AppPath" $ColorInfo
        
        # Check if running
        $processNames = @("WinKey_CommandPallette_Replacement", "WinKeyRemapper", "WinKey_CommandPalette_Replacement")
        $runningProcess = $null
        
        foreach ($name in $processNames) {
            $runningProcess = Get-Process -Name $name -ErrorAction SilentlyContinue
            if ($runningProcess) { break }
        }
        
        if ($runningProcess) {
            Write-ColoredOutput "✓ $AppName is currently running" $ColorSuccess
        }
        else {
            Write-ColoredOutput "○ $AppName is not running" $ColorInfo
        }
        
    }
    else {
        Write-ColoredOutput "✗ $AppName is not installed" $ColorError
    }
    
    Write-ColoredOutput ""
    
    # Check startup status
    $regStartup = Test-RegistryStartup
    $taskStartup = Test-AdminStartupTask
    
    if ($regStartup -or $taskStartup) {
        Write-ColoredOutput "Startup Status:" $ColorPrompt
        
        if ($taskStartup) {
            Write-ColoredOutput "✓ Scheduled Task startup enabled (NO UAC prompts)" $ColorSuccess
            
            # Get task details
            try {
                $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
                if ($task) {
                    Write-ColoredOutput "  Task State: $($task.State)" $ColorInfo
                    Write-ColoredOutput "  Run Level: Highest (Admin privileges)" $ColorInfo
                }
            }
            catch {
                Write-ColoredOutput "  Task details unavailable" $ColorWarning
            }
        }
        
        if ($regStartup) {
            Write-ColoredOutput "⚠ Registry startup enabled (shows UAC prompts)" $ColorWarning
            $regValue = Get-ItemProperty -Path $StartupRegPath -Name $StartupRegName -ErrorAction SilentlyContinue
            if ($regValue) {
                Write-ColoredOutput "  Registry: $($regValue.$StartupRegName)" $ColorInfo
            }
        }
        
        # Check if startup script exists
        $startupScript = Join-Path $InstallPath "WinKeyRemapper-Startup.bat"
        if (Test-Path $startupScript) {
            Write-ColoredOutput "✓ Intelligent startup script exists" $ColorSuccess
        }
        
        # Check for startup logs
        if (Test-Path "C:\temp\winkey-startup.log") {
            $logLines = Get-Content "C:\temp\winkey-startup.log" -Tail 3 -ErrorAction SilentlyContinue
            if ($logLines) {
                Write-ColoredOutput "📋 Recent startup log entries:" $ColorInfo
                foreach ($line in $logLines) {
                    Write-ColoredOutput "  $line" $ColorInfo
                }
            }
        }
    }
    else {
        Write-ColoredOutput "○ Startup disabled" $ColorInfo
    }
    
    Write-ColoredOutput ""
    
    # Check shortcuts
    Write-ColoredOutput "Shortcuts:" $ColorPrompt
    if (Test-Path $StartMenuShortcut) {
        Write-ColoredOutput "✓ Start Menu shortcut exists" $ColorSuccess
    }
    
    if (Test-Path $DesktopShortcut) {
        Write-ColoredOutput "✓ Desktop shortcut exists" $ColorSuccess
    }
    
    $publicDesktop = "$env:PUBLIC\Desktop\$AppName.lnk"
    if (Test-Path $publicDesktop) {
        Write-ColoredOutput "✓ Public Desktop shortcut exists" $ColorSuccess
    }
    
    Write-ColoredOutput ""
    Read-Host "Press Enter to return to menu"
    Show-Menu
}

function Show-StartupLogs {
    Show-Header
    Write-ColoredOutput "Startup Logs Viewer" $ColorPrompt
    Write-ColoredOutput ""
    
    $logPath = "C:\temp\winkey-startup.log"
    
    if (Test-Path $logPath) {
        try {
            $logContent = Get-Content $logPath -ErrorAction Stop
            
            if ($logContent.Count -eq 0) {
                Write-ColoredOutput "Log file exists but is empty" $ColorWarning
            } else {
                Write-ColoredOutput "Showing last 20 entries from startup log:" $ColorInfo
                Write-ColoredOutput "Log location: $logPath" $ColorInfo
                Write-ColoredOutput ("=" * 60) $ColorInfo
                
                # Show last 20 lines
                $logContent | Select-Object -Last 20 | ForEach-Object {
                    if ($_ -match "ERROR") {
                        Write-ColoredOutput $_ $ColorError
                    } elseif ($_ -match "WARNING") {
                        Write-ColoredOutput $_ $ColorWarning
                    } elseif ($_ -match "SUCCESS") {
                        Write-ColoredOutput $_ $ColorSuccess
                    } else {
                        Write-ColoredOutput $_ $ColorInfo
                    }
                }
                
                Write-ColoredOutput ("=" * 60) $ColorInfo
                Write-ColoredOutput ""
                Write-ColoredOutput "Log analysis:" $ColorPrompt
                
                # Analyze logs
                $recentEntries = $logContent | Select-Object -Last 50
                $lastStartAttempt = $recentEntries | Where-Object { $_ -match "Starting Win Key Remapper intelligent startup" } | Select-Object -Last 1
                
                if ($lastStartAttempt) {
                    Write-ColoredOutput "✓ Found recent startup attempt" $ColorSuccess
                    
                    $powerToysFound = $recentEntries | Where-Object { $_ -match "PowerToys found running" } | Select-Object -Last 1
                    if ($powerToysFound) {
                        Write-ColoredOutput "✓ PowerToys was detected before starting" $ColorSuccess
                    } else {
                        Write-ColoredOutput "⚠ PowerToys may not have been detected" $ColorWarning
                    }
                    
                    $successEntry = $recentEntries | Where-Object { $_ -match "SUCCESS.*Win Key Remapper started" } | Select-Object -Last 1
                    if ($successEntry) {
                        Write-ColoredOutput "✓ App reported successful startup" $ColorSuccess
                    } else {
                        Write-ColoredOutput "⚠ No success confirmation found" $ColorWarning
                    }
                } else {
                    Write-ColoredOutput "ℹ No recent startup attempts found in log" $ColorInfo
                }
            }
        }
        catch {
            Write-ColoredOutput "Error reading log file: $_" $ColorError
        }
    } else {
        Write-ColoredOutput "No startup log found at: $logPath" $ColorWarning
        Write-ColoredOutput "This is normal if:" $ColorInfo
        Write-ColoredOutput "- App hasn't been installed with startup enabled" $ColorInfo
        Write-ColoredOutput "- No restart has occurred since enabling startup" $ColorInfo
        Write-ColoredOutput "- Startup is using direct method (not intelligent script)" $ColorInfo
    }
    
    Write-ColoredOutput ""
    Write-ColoredOutput "Options:" $ColorPrompt
    Write-ColoredOutput "- 'o' to open log file in notepad" $ColorPrompt
    Write-ColoredOutput "- 'c' to clear log file" $ColorPrompt
    Write-ColoredOutput "- Enter to return to menu" $ColorPrompt
    
    $choice = Read-Host "Your choice"
    
    switch ($choice.ToLower()) {
        "o" {
            if (Test-Path $logPath) {
                try {
                    Start-Process "notepad.exe" -ArgumentList $logPath
                } catch {
                    Write-ColoredOutput "Could not open notepad: $_" $ColorError
                    Start-Sleep 2
                }
            }
        }
        "c" {
            if (Test-Path $logPath) {
                try {
                    Remove-Item $logPath -Force
                    Write-ColoredOutput "Log file cleared" $ColorSuccess
                    Start-Sleep 2
                } catch {
                    Write-ColoredOutput "Could not clear log: $_" $ColorError
                    Start-Sleep 2
                }
            }
        }
    }
    
    Show-Menu
}

# Main execution
try {
    # Handle command line parameters
    switch ($Action) {
        "install" { Install-Application }
        "uninstall" { Uninstall-Application }
        "startup-enable" { Enable-Startup }
        "startup-disable" { Disable-Startup }
        "status" { Show-Status }
        "logs" { Show-StartupLogs }
        "menu" { Show-Menu }
    }
}
catch {
    Write-ColoredOutput "An unexpected error occurred: $_" $ColorError
    Read-Host "Press Enter to exit"
}