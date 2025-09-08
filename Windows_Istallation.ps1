# Azure Gateway API - Windows Installation Script
# This script installs prerequisites and sets up the Azure Gateway API on Windows

param(
    [string]$InstallPath = "C:\AzureGateway",
    [string]$DataPath = "C:\AzureGatewayData",
    [switch]$SkipDotNet,
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

# Set verbose preference
if ($Verbose) {
    $VerbosePreference = "Continue"
}

Write-Host "=== Azure Gateway API Windows Installation ===" -ForegroundColor Green
Write-Host "Install Path: $InstallPath" -ForegroundColor Yellow
Write-Host "Data Path: $DataPath" -ForegroundColor Yellow

# Function to check if running as administrator
function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Function to check .NET 8 installation
function Test-DotNet8 {
    try {
        $dotnetInfo = & dotnet --list-runtimes 2>$null
        if ($dotnetInfo -like "*Microsoft.AspNetCore.App 8.*") {
            Write-Host "✓ .NET 8 ASP.NET Core Runtime found" -ForegroundColor Green
            return $true
        }
        return $false
    }
    catch {
        return $false
    }
}

# Function to install .NET 8
function Install-DotNet8 {
    Write-Host "Installing .NET 8 Runtime..." -ForegroundColor Yellow
    
    $downloadUrl = "https://download.microsoft.com/download/8/4/8/848f28ae-78ab-4661-8ebe-765312c38565/dotnet-hosting-8.0.0-win.exe"
    $installerPath = "$env:TEMP\dotnet-hosting-8.0.0-win.exe"
    
    try {
        Write-Host "Downloading .NET 8 Hosting Bundle..."
        Invoke-WebRequest -Uri $downloadUrl -OutFile $installerPath -UseBasicParsing
        
        Write-Host "Installing .NET 8..."
        Start-Process -FilePath $installerPath -ArgumentList "/quiet" -Wait
        
        Remove-Item $installerPath -Force
        
        # Verify installation
        if (Test-DotNet8) {
            Write-Host "✓ .NET 8 installed successfully" -ForegroundColor Green
        } else {
            throw "Failed to verify .NET 8 installation"
        }
    }
    catch {
        Write-Error "Failed to install .NET 8: $_"
        exit 1
    }
}

# Function to create directories
function New-DirectoryStructure {
    param([string]$BasePath, [string]$DataBasePath)
    
    Write-Host "Creating directory structure..." -ForegroundColor Yellow
    
    $directories = @(
        $BasePath,
        "$BasePath\logs",
        "$DataBasePath",
        "$DataBasePath\database",
        "$DataBasePath\incoming",
        "$DataBasePath\archive",
        "$DataBasePath\temp",
        "$DataBasePath\temp\api-data"
    )
    
    foreach ($dir in $directories) {
        if (!(Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            Write-Verbose "Created directory: $dir"
        }
    }
    
    Write-Host "✓ Directory structure created" -ForegroundColor Green
}

# Function to update configuration files
function Update-ConfigurationFiles {
    param([string]$AppPath, [string]$DataBasePath)
    
    Write-Host "Updating configuration files..." -ForegroundColor Yellow
    
    $configFiles = @(
        "$AppPath\appsettings.json",
        "$AppPath\appsettings.Development.json"
    )
    
    $dbPath = "$DataBasePath\database\databasegateway.db".Replace('\', '\\')
    $tempPath = "$DataBasePath\temp\api-data".Replace('\', '\\')
    $incomingPath = "$DataBasePath\incoming".Replace('\', '\\')
    $archivePath = "$DataBasePath\archive".Replace('\', '\\')
    
    foreach ($configFile in $configFiles) {
        if (Test-Path $configFile) {
            Write-Verbose "Updating $configFile"
            
            $content = Get-Content $configFile -Raw
            
            # Update paths
            $content = $content -replace '"Data Source=.*?"', "`"Data Source=$dbPath`""
            $content = $content -replace '"TempDirectory": ".*?"', "`"TempDirectory`": `"$tempPath`""
            $content = $content -replace '"FolderPath": ".*?"', "`"FolderPath`": `"$incomingPath`""
            $content = $content -replace '"ArchivePath": ".*?"', "`"ArchivePath`": `"$archivePath`""
            
            Set-Content -Path $configFile -Value $content -Encoding UTF8
            Write-Host "✓ Updated $configFile" -ForegroundColor Green
        }
    }
}

# Function to set up Windows service
function Install-WindowsService {
    param([string]$AppPath, [string]$ServiceName = "AzureGatewayAPI")
    
    Write-Host "Setting up Windows Service..." -ForegroundColor Yellow
    
    # Check if service already exists
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Write-Host "Service $ServiceName already exists. Stopping..." -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force
        Remove-Service -Name $ServiceName
    }
    
    # Create service
    $serviceArgs = @{
        Name = $ServiceName
        BinaryPathName = "`"$AppPath\AzureGateway.Api.exe`""
        DisplayName = "Azure Gateway API Service"
        Description = "Azure Gateway API - File monitoring and upload service"
        StartupType = "Automatic"
    }
    
    New-Service @serviceArgs
    Write-Host "✓ Windows Service created: $ServiceName" -ForegroundColor Green
}

# Function to create startup script
function New-StartupScript {
    param([string]$AppPath)
    
    $startScript = @"
@echo off
cd /d "$AppPath"
echo Starting Azure Gateway API...
dotnet AzureGateway.Api.dll
pause
"@
    
    Set-Content -Path "$AppPath\start.bat" -Value $startScript
    Write-Host "✓ Startup script created: $AppPath\start.bat" -ForegroundColor Green
}

# Function to set permissions
function Set-DirectoryPermissions {
    param([string]$Path)
    
    Write-Host "Setting directory permissions..." -ForegroundColor Yellow
    
    try {
        # Give IIS_IUSRS and NETWORK SERVICE full access to data directories
        $acl = Get-Acl $Path
        
        $accessRules = @(
            @{ Identity = "IIS_IUSRS"; Rights = "FullControl" },
            @{ Identity = "NETWORK SERVICE"; Rights = "FullControl" },
            @{ Identity = "Users"; Rights = "ReadAndExecute" }
        )
        
        foreach ($rule in $accessRules) {
            $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                $rule.Identity, $rule.Rights, "ContainerInherit,ObjectInherit", "None", "Allow"
            )
            $acl.SetAccessRule($accessRule)
        }
        
        Set-Acl -Path $Path -AclObject $acl
        Write-Host "✓ Permissions set for $Path" -ForegroundColor Green
    }
    catch {
        Write-Warning "Could not set permissions for $Path`: $_"
    }
}

# Main installation process
try {
    # Check administrator privileges
    if (!(Test-Administrator)) {
        Write-Error "This script must be run as Administrator. Please run PowerShell as Administrator and try again."
        exit 1
    }
    
    Write-Host "✓ Running as Administrator" -ForegroundColor Green
    
    # Check and install .NET 8
    if (!$SkipDotNet) {
        if (!(Test-DotNet8)) {
            Install-DotNet8
        } else {
            Write-Host "✓ .NET 8 is already installed" -ForegroundColor Green
        }
    }
    
    # Create directory structure
    New-DirectoryStructure -InstallDir $InstallPath -DataDir $DataPath -IncomingDir $IncomingPath -ArchiveDir $ArchivePath -TempDir $TempPath
    
    # Check if application files exist
    if (!(Test-Path "$InstallPath\AzureGateway.Api.dll")) {
        Write-Host "Application files not found. Please copy the published application to $InstallPath" -ForegroundColor Red
        Write-Host "You can publish the application using: dotnet publish -c Release -o `"$InstallPath`"" -ForegroundColor Yellow
        exit 0
    }
    
    # Update configuration files
    Update-ConfigurationFiles -AppPath $InstallPath -DatabasePath $DataPath -IncomingDir $IncomingPath -ArchiveDir $ArchivePath -TempDir $TempPath
    
    # Set permissions
    Set-DirectoryPermissions -Path $DataPath
    Set-DirectoryPermissions -Path $InstallPath
    
    # Create startup script
    New-StartupScript -AppPath $InstallPath
    
    # Install Windows service (optional)
    $installService = Read-Host "Do you want to install as Windows Service? (y/n) [default: y]"
    if ($installService -eq "" -or $installService -eq "y") {
        Install-WindowsService -AppPath $InstallPath
    }
    
    Write-Host ""
    Write-Host "=== Installation Complete ===" -ForegroundColor Green
    Write-Host "Application Path: $InstallPath" -ForegroundColor Yellow
    Write-Host "Database Path: $DataPath\database\databasegateway.db" -ForegroundColor Yellow
    Write-Host "Incoming Path: $IncomingPath" -ForegroundColor Yellow
    Write-Host "Archive Path: $ArchivePath" -ForegroundColor Yellow
    Write-Host "Temp Path: $TempPath" -ForegroundColor Yellow
    Write-Host "Logs: $InstallPath\logs\" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "1. Configure Azure Storage connection string in appsettings.json"
    Write-Host "2. Start the service or run manually using start.bat"
    Write-Host "3. Access the API at http://localhost:5097"
    Write-Host "4. View Swagger documentation at http://localhost:5097/swagger"
    
}
catch {
    Write-Error "Installation failed: $_"
    exit 1
}