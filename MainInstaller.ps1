# Azure Gateway API - Main Installation Script
# This is the main script that orchestrates the entire installation process

param(
    [string]$InstallPath = "",
    [string]$DataPath = "",
    [string]$SourcePath = "",
    [switch]$Linux,
    [switch]$SkipDotNet,
    [switch]$SkipValidation,
    [switch]$Verbose,
    [switch]$Help
)

if ($Help) {
    Write-Host @"
Azure Gateway API - Main Installation Script

USAGE:
    Windows: .\install-azuregateway.ps1 [options]
    Linux:   ./install-azuregateway.sh [options]

OPTIONS:
    -InstallPath <path>    Installation directory 
                          Default: Windows: C:\AzureGateway, Linux: /opt/azuregateway
    -DataPath <path>       Data directory
                          Default: Windows: C:\AzureGatewayData, Linux: /var/azuregateway
    -SourcePath <path>     Path to published application files
                          If not specified, will prompt for manual deployment
    -SkipDotNet           Skip .NET 8 installation check
    -SkipValidation       Skip post-installation validation
    -Verbose              Enable verbose output
    -Linux                Generate Linux version of this script
    -Help                 Show this help message

EXAMPLES:
    # Basic installation
    .\install-azuregateway.ps1
    
    # Custom paths
    .\install-azuregateway.ps1 -InstallPath "D:\Apps\AzureGateway" -DataPath "D:\Data\AzureGateway"
    
    # With application source
    .\install-azuregateway.ps1 -SourcePath ".\publish"
    
    # Linux installation
    sudo ./install-azuregateway.sh --install-path /opt/myapp --data-path /var/myapp

PREREQUISITES:
    Windows: PowerShell 5.1+, Administrator privileges
    Linux:   Bash, root/sudo privileges

"@
    exit 0
}

if ($Linux) {
    # Generate Linux version
    Write-Host @'
#!/bin/bash
# Azure Gateway API - Main Installation Script for Linux

set -e

# Default values
INSTALL_PATH="/opt/azuregateway"
DATA_PATH="/var/azuregateway"
SOURCE_PATH=""
SKIP_DOTNET=false
SKIP_VALIDATION=false
VERBOSE=false

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --install-path)
            INSTALL_PATH="$2"
            shift 2
            ;;
        --data-path)
            DATA_PATH="$2"
            shift 2
            ;;
        --source-path)
            SOURCE_PATH="$2"
            shift 2
            ;;
        --skip-dotnet)
            SKIP_DOTNET=true
            shift
            ;;
        --skip-validation)
            SKIP_VALIDATION=true
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        -h|--help)
            echo "Azure Gateway API - Main Installation Script for Linux"
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --install-path PATH    Installation directory (default: /opt/azuregateway)"
            echo "  --data-path PATH       Data directory (default: /var/azuregateway)"
            echo "  --source-path PATH     Path to published application files"
            echo "  --skip-dotnet         Skip .NET installation"
            echo "  --skip-validation     Skip post-installation validation"
            echo "  --verbose             Verbose output"
            echo "  -h, --help            Show this help"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

log_info() { echo -e "${GREEN}✓${NC} $1"; }
log_warn() { echo -e "${YELLOW}⚠${NC} $1"; }
log_error() { echo -e "${RED}✗${NC} $1"; }
log_step() { echo -e "${BLUE}==>${NC} $1"; }

echo -e "${GREEN}=== Azure Gateway API Linux Installation ===${NC}"
echo "This script will install Azure Gateway API on your Linux system"
echo ""
echo "Configuration:"
echo "  Install Path: $INSTALL_PATH"
echo "  Data Path: $DATA_PATH"
echo "  Source Path: ${SOURCE_PATH:-'Not specified (manual deployment)'}"
echo ""

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    log_error "This script must be run as root (use sudo)"
    exit 1
fi

# Step 1: Run prerequisites installation
log_step "Step 1: Installing prerequisites and setting up environment"
if [ "$SKIP_DOTNET" = true ]; then
    bash install.sh --install-path "$INSTALL_PATH" --data-path "$DATA_PATH" --skip-dotnet
else
    bash install.sh --install-path "$INSTALL_PATH" --data-path "$DATA_PATH"
fi

if [ $? -ne 0 ]; then
    log_error "Prerequisites installation failed"
    exit 1
fi

log_info "Prerequisites installation completed"

# Step 2: Deploy application if source provided
if [ -n "$SOURCE_PATH" ]; then
    log_step "Step 2: Deploying application files"
    
    if [ ! -d "$SOURCE_PATH" ]; then
        log_error "Source path not found: $SOURCE_PATH"
        exit 1
    fi
    
    if [ ! -f "$SOURCE_PATH/AzureGateway.Api.dll" ]; then
        log_error "Application DLL not found in source path: $SOURCE_PATH"
        log_info "Please ensure you have published the application:"
        echo "  cd path/to/AzureGateway.Api"
        echo "  dotnet publish -c Release -o \"$SOURCE_PATH\""
        exit 1
    fi
    
    # Copy files
    cp -r "$SOURCE_PATH"/* "$INSTALL_PATH/"
    chown -R azuregateway:azuregateway "$INSTALL_PATH"
    chmod +x "$INSTALL_PATH"/*.dll
    
    log_info "Application files deployed"
else
    log_step "Step 2: Application deployment skipped"
    log_warn "No source path provided. You need to manually deploy the application:"
    echo "  1. Publish: dotnet publish -c Release -o publish"
    echo "  2. Copy: sudo cp -r publish/* \"$INSTALL_PATH/\""
    echo "  3. Set ownership: sudo chown -R azuregateway:azuregateway \"$INSTALL_PATH\""
fi

# Step 3: Validation
if [ "$SKIP_VALIDATION" = false ]; then
    log_step "Step 3: Validating installation"
    
    # Create validation script inline
    cat > /tmp/validate-config.sh << 'EOF'
#!/bin/bash
INSTALL_PATH="$1"
DATA_PATH="$2"

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_success() { echo -e "${GREEN}✓${NC} $1"; }
log_error() { echo -e "${RED}✗${NC} $1"; }
log_warn() { echo -e "${YELLOW}⚠${NC} $1"; }

errors=0

# Check .NET
if command -v dotnet &> /dev/null && dotnet --list-runtimes | grep -q "Microsoft.AspNetCore.App 8"; then
    log_success ".NET 8 is installed"
else
    log_error ".NET 8 not found"
    ((errors++))
fi

# Check directories
for dir in "$INSTALL_PATH" "$DATA_PATH" "$DATA_PATH/database" "$DATA_PATH/incoming" "$DATA_PATH/archive"; do
    if [ -d "$dir" ]; then
        log_success "Directory exists: $dir"
    else
        log_error "Directory missing: $dir"
        ((errors++))
    fi
done

# Check application
if [ -f "$INSTALL_PATH/AzureGateway.Api.dll" ]; then
    log_success "Application files found"
else
    log_warn "Application files not found (manual deployment needed)"
fi

# Check service
if systemctl is-enabled azuregateway &>/dev/null; then
    log_success "Service is configured"
else
    log_error "Service not configured"
    ((errors++))
fi

exit $errors
EOF

    chmod +x /tmp/validate-config.sh
    
    if /tmp/validate-config.sh "$INSTALL_PATH" "$DATA_PATH"; then
        log_info "Validation passed"
    else
        log_warn "Validation found issues - please review and fix"
    fi
    
    rm /tmp/validate-config.sh
else
    log_step "Step 3: Validation skipped"
fi

# Final instructions
echo ""
echo -e "${GREEN}=== Installation Summary ===${NC}"
echo "Install Path: $INSTALL_PATH"
echo "Data Path: $DATA_PATH"
echo "Service: azuregateway"
echo ""
echo -e "${BLUE}Next Steps:${NC}"

if [ -f "$INSTALL_PATH/AzureGateway.Api.dll" ]; then
    echo "1. Configure Azure Storage in: $INSTALL_PATH/appsettings.json"
    echo "2. Start the service: sudo systemctl start azuregateway"
    echo "3. Enable auto-start: sudo systemctl enable azuregateway"
    echo "4. Check status: sudo systemctl status azuregateway"
    echo "5. View logs: sudo journalctl -u azuregateway -f"
    echo "6. Access API: http://localhost:5097"
else
    echo "1. Deploy application files to: $INSTALL_PATH"
    echo "2. Configure Azure Storage connection"
    echo "3. Start the service"
fi

echo ""
echo "For detailed instructions: $INSTALL_PATH/DEPLOYMENT_GUIDE.txt"
'@
    exit 0
}

# Windows PowerShell version starts here
$ErrorActionPreference = "Stop"

# Set default paths for Windows
if (!$InstallPath) { $InstallPath = "C:\AzureGateway" }
if (!$DataPath) { $DataPath = "C:\AzureGatewayData" }

Write-Host "=== Azure Gateway API Windows Installation ===" -ForegroundColor Green
Write-Host "This script will install Azure Gateway API on your Windows system" -ForegroundColor White
Write-Host ""
Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Install Path: $InstallPath"
Write-Host "  Data Path: $DataPath"
Write-Host "  Source Path: $(if ($SourcePath) { $SourcePath } else { 'Not specified (manual deployment)' })"
Write-Host ""

# Function to check if running as administrator
function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Check administrator privileges
if (!(Test-Administrator)) {
    Write-Error "This script must be run as Administrator. Please run PowerShell as Administrator and try again."
    exit 1
}

Write-Host "✓ Running as Administrator" -ForegroundColor Green

try {
    # Step 1: Run prerequisites installation
    Write-Host ""
    Write-Host "==> Step 1: Installing prerequisites and setting up environment" -ForegroundColor Blue
    
    $installArgs = @("-InstallPath", $InstallPath, "-DataPath", $DataPath)
    if ($SkipDotNet) { $installArgs += "-SkipDotNet" }
    if ($Verbose) { $installArgs += "-Verbose" }
    
    & ".\install.ps1" @installArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "Prerequisites installation failed"
    }
    
    Write-Host "✓ Prerequisites installation completed" -ForegroundColor Green

    # Step 2: Deploy application if source provided
    Write-Host ""
    Write-Host "==> Step 2: Deploying application files" -ForegroundColor Blue
    
    if ($SourcePath) {
        if (!(Test-Path $SourcePath)) {
            throw "Source path not found: $SourcePath"
        }
        
        if (!(Test-Path "$SourcePath\AzureGateway.Api.dll")) {
            Write-Error "Application DLL not found in source path: $SourcePath"
            Write-Host "Please ensure you have published the application:" -ForegroundColor Yellow
            Write-Host "  cd path\to\AzureGateway.Api"
            Write-Host "  dotnet publish -c Release -o `"$SourcePath`""
            exit 1
        }
        
        # Copy files
        Copy-Item "$SourcePath\*" $InstallPath -Recurse -Force
        Write-Host "✓ Application files deployed" -ForegroundColor Green
    } else {
        Write-Host "⚠ Application deployment skipped" -ForegroundColor Yellow
        Write-Host "No source path provided. You need to manually deploy the application:" -ForegroundColor Yellow
        Write-Host "  1. Publish: dotnet publish -c Release -o publish"
        Write-Host "  2. Copy: Copy-Item publish\* `"$InstallPath`" -Recurse -Force"
    }

    # Step 3: Validation
    if (!$SkipValidation) {
        Write-Host ""
        Write-Host "==> Step 3: Validating installation" -ForegroundColor Blue
        
        $validationArgs = @()
        if ($InstallPath) { $validationArgs += @("-InstallPath", $InstallPath) }
        if ($DataPath) { $validationArgs += @("-DataPath", $DataPath) }
        
        & ".\validate-config.ps1" @validationArgs
        
        Write-Host "✓ Validation completed" -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "==> Step 3: Validation skipped" -ForegroundColor Blue
    }

    # Final instructions
    Write-Host ""
    Write-Host "=== Installation Summary ===" -ForegroundColor Green
    if ($InstallPath) { Write-Host "Install Path: $InstallPath" -ForegroundColor Yellow }
    if ($DataPath) { Write-Host "Data Path: $DataPath" -ForegroundColor Yellow }
    # Additional path outputs omitted unless defined elsewhere
    Write-Host "Service: AzureGatewayAPI" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    
    $installDir = if ($InstallPath) { $InstallPath } else { "installation directory" }
    
    if (Test-Path "$installDir\AzureGateway.Api.dll") {
        Write-Host "1. Configure Azure Storage in: $installDir\appsettings.json"
        Write-Host "2. Start the service: sc start AzureGatewayAPI"
        Write-Host "3. Check status: sc query AzureGatewayAPI"
        Write-Host "4. View logs in: $installDir\logs\"
        Write-Host "5. Access API: http://localhost:5097"
        Write-Host "6. Swagger UI: http://localhost:5097/swagger"
    } else {
        Write-Host "1. Deploy application files to: $installDir"
        Write-Host "2. Configure Azure Storage connection"
        Write-Host "3. Start the service or run manually"
    }

} catch {
    Write-Host ""
    Write-Host "Installation failed: $_" -ForegroundColor Red
    Write-Host "Please check the error messages above and try again." -ForegroundColor Yellow
    exit 1
}