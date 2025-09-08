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
echo "  Source Path: ${SOURCE_PATH:-'Auto-detect next to this script'}"
echo ""

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    log_error "This script must be run as root (use sudo)"
    exit 1
fi

# Step 1: Run prerequisites installation
log_step "Step 1: Installing prerequisites and setting up environment"
if [ "$SKIP_DOTNET" = true ]; then
    bash Linux_Installation.sh --install-path "$INSTALL_PATH" --data-path "$DATA_PATH" --skip-dotnet
else
    bash Linux_Installation.sh --install-path "$INSTALL_PATH" --data-path "$DATA_PATH"
fi

if [ $? -ne 0 ]; then
    log_error "Prerequisites installation failed"
    exit 1
fi

log_info "Prerequisites installation completed"

# Step 2: Auto-detect source path relative to this script when not supplied, then deploy
if [ -z "$SOURCE_PATH" ]; then
    SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
    if [ -d "$SCRIPT_DIR/AzureGateway.Api" ]; then
        SOURCE_PATH="$SCRIPT_DIR/AzureGateway.Api"
    else
        SOURCE_PATH="$SCRIPT_DIR"
    fi
fi

# Normalize source path (expand ~ and make absolute)
SOURCE_PATH=$(eval echo "$SOURCE_PATH")
if command -v realpath >/dev/null 2>&1; then
    SOURCE_PATH=$(realpath -m "$SOURCE_PATH")
fi

if [ -n "$SOURCE_PATH" ]; then
    log_step "Step 2: Deploying application files"
    if [ ! -d "$SOURCE_PATH" ]; then
        log_error "Source path not found: $SOURCE_PATH"
        exit 1
    fi

    SRC_FOR_COPY="$SOURCE_PATH"

    if [ ! -f "$SOURCE_PATH/AzureGateway.Api.dll" ]; then
        log_step "Publish output not found. Attempting to build and publish the project."

        # Try to locate the project file
        PROJECT_PATH=""
        if [ -f "$SOURCE_PATH/AzureGateway.Api.csproj" ]; then
            PROJECT_PATH="$SOURCE_PATH/AzureGateway.Api.csproj"
        elif [ -f "$SOURCE_PATH/AzureGateway.Api/AzureGateway.Api.csproj" ]; then
            PROJECT_PATH="$SOURCE_PATH/AzureGateway.Api/AzureGateway.Api.csproj"
        else
            # Fallback: first csproj under the directory
            PROJECT_PATH=$(find "$SOURCE_PATH" -maxdepth 2 -type f -name "*.csproj" | head -n 1)
        fi

        if [ -z "$PROJECT_PATH" ]; then
            log_error "Could not find a .csproj under: $SOURCE_PATH"
            exit 1
        fi

        # Ensure dotnet is available (prior steps should have installed it)
        if ! command -v dotnet >/dev/null 2>&1; then
            log_error "dotnet CLI not found. Install .NET or run with --skip-dotnet after manual install."
            exit 1
        fi

        PUBLISH_DIR="$DATA_PATH/temp/publish-$(date +%s)"
        mkdir -p "$PUBLISH_DIR"
        log_step "Publishing project: $PROJECT_PATH -> $PUBLISH_DIR"
        if ! dotnet publish "$PROJECT_PATH" -c Release -o "$PUBLISH_DIR"; then
            log_error "dotnet publish failed"
            exit 1
        fi

        if [ ! -f "$PUBLISH_DIR/AzureGateway.Api.dll" ]; then
            log_error "Publish succeeded but AzureGateway.Api.dll not found in: $PUBLISH_DIR"
            exit 1
        fi

        SRC_FOR_COPY="$PUBLISH_DIR"
        log_info "Publish completed. Using published output for deployment."
    fi

    cp -r "$SRC_FOR_COPY"/* "$INSTALL_PATH/"
    chown -R azuregateway:azuregateway "$INSTALL_PATH"
    chmod +x "$INSTALL_PATH"/*.dll || true
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


