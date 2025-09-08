# Function to create startup script
create_startup_script() {
    cat > "$INSTALL_PATH/start.sh" << EOF
#!/bin/bash
cd "$INSTALL_PATH"
echo "Starting Azure Gateway API..."
dotnet AzureGateway.Api.dll
EOF

    chmod +x "$INSTALL_PATH/start.sh"
    chown "$SERVICE_USER:$SERVICE_USER" "$INSTALL_PATH/start.sh"
    #!/bin/bash

# Azure Gateway API - Linux Installation Script
# This script installs prerequisites and sets up the Azure Gateway API on Linux

set -e  # Exit on any error

# Default configuration
INSTALL_PATH="/opt/azuregateway"
DATA_PATH="/var/azuregateway"
SERVICE_NAME="azuregateway"
SERVICE_USER="azuregateway"
SKIP_DOTNET=false
VERBOSE=false

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Parse command line arguments
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
        --skip-dotnet)
            SKIP_DOTNET=true
            shift
            ;;
        --verbose)
            VERBOSE=true
            shift
            ;;
        -h|--help)
            echo "Azure Gateway API Linux Installation Script"
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --install-path PATH    Installation directory (default: /opt/azuregateway)"
            echo "  --data-path PATH       Data directory (default: /var/azuregateway)"
            echo "  --skip-dotnet         Skip .NET installation"
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

# Logging functions
log_info() {
    echo -e "${GREEN}✓${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}⚠${NC} $1"
}

log_error() {
    echo -e "${RED}✗${NC} $1"
}

log_step() {
    echo -e "${BLUE}==>${NC} $1"
}

verbose_log() {
    if [ "$VERBOSE" = true ]; then
        echo -e "${BLUE}[VERBOSE]${NC} $1"
    fi
}

# Function to detect Linux distribution
detect_distro() {
    if [ -f /etc/os-release ]; then
        . /etc/os-release
        DISTRO=$ID
        VERSION=$VERSION_ID
    else
        log_error "Cannot detect Linux distribution"
        exit 1
    fi
    
    verbose_log "Detected distribution: $DISTRO $VERSION"
}

# Function to check if running as root
check_root() {
    if [ "$EUID" -ne 0 ]; then
        log_error "This script must be run as root (use sudo)"
        exit 1
    fi
    log_info "Running as root"
}

# Function to install package manager packages
install_packages() {
    log_step "Installing required packages..."
    
    case $DISTRO in
        ubuntu|debian)
            apt-get update
            apt-get install -y curl wget gpg software-properties-common apt-transport-https
            ;;
        centos|rhel|fedora)
            if command -v dnf &> /dev/null; then
                dnf install -y curl wget gpg
            else
                yum install -y curl wget gpg
            fi
            ;;
        *)
            log_warn "Unsupported distribution: $DISTRO. Please install curl, wget, and gpg manually."
            ;;
    esac
    
    log_info "Required packages installed"
}

# Function to check .NET 8 installation
check_dotnet8() {
    if command -v dotnet &> /dev/null; then
        if dotnet --list-runtimes | grep -q "Microsoft.AspNetCore.App 8"; then
            log_info ".NET 8 ASP.NET Core Runtime found"
            return 0
        fi
    fi
    return 1
}

# Function to install .NET 8
install_dotnet8() {
    log_step "Installing .NET 8 Runtime..."
    
    case $DISTRO in
        ubuntu|debian)
            # Add Microsoft package repository
            wget https://packages.microsoft.com/config/$DISTRO/$VERSION/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
            dpkg -i packages-microsoft-prod.deb
            rm packages-microsoft-prod.deb
            
            apt-get update
            apt-get install -y aspnetcore-runtime-8.0
            ;;
        centos|rhel)
            # Add Microsoft package repository
            rpm --import https://packages.microsoft.com/keys/microsoft.asc
            wget -O /etc/yum.repos.d/microsoft-prod.repo https://packages.microsoft.com/config/$DISTRO/$VERSION/prod.repo
            
            if command -v dnf &> /dev/null; then
                dnf install -y aspnetcore-runtime-8.0
            else
                yum install -y aspnetcore-runtime-8.0
            fi
            ;;
        fedora)
            rpm --import https://packages.microsoft.com/keys/microsoft.asc
            wget -O /etc/yum.repos.d/microsoft-prod.repo https://packages.microsoft.com/config/fedora/$VERSION/prod.repo
            dnf install -y aspnetcore-runtime-8.0
            ;;
        *)
            log_error "Automatic .NET installation not supported for $DISTRO"
            log_info "Please install .NET 8 ASP.NET Core Runtime manually from:"
            log_info "https://docs.microsoft.com/en-us/dotnet/core/install/"
            exit 1
            ;;
    esac
    
    # Verify installation
    if check_dotnet8; then
        log_info ".NET 8 installed successfully"
    else
        log_error "Failed to verify .NET 8 installation"
        exit 1
    fi
}

# Function to create system user
create_service_user() {
    log_step "Creating service user..."
    
    if ! id "$SERVICE_USER" &>/dev/null; then
        useradd --system --home-dir "$INSTALL_PATH" --shell /bin/false "$SERVICE_USER"
        log_info "Created user: $SERVICE_USER"
    else
        log_info "User $SERVICE_USER already exists"
    fi
}

# Function to create directory structure
create_directories() {
    log_step "Creating directory structure..."
    
    directories=(
        "$INSTALL_PATH"
        "$INSTALL_PATH/logs"
        "$DATA_PATH"
        "$DATA_PATH/database"
        "$DATA_PATH/incoming"
        "$DATA_PATH/archive"
        "$DATA_PATH/temp"
        "$DATA_PATH/temp/api-data"
    )
    
    for dir in "${directories[@]}"; do
        mkdir -p "$dir"
        verbose_log "Created directory: $dir"
    done
    
    # Set ownership
    chown -R "$SERVICE_USER:$SERVICE_USER" "$INSTALL_PATH"
    chown -R "$SERVICE_USER:$SERVICE_USER" "$DATA_PATH"
    
    # Set permissions
    chmod 755 "$INSTALL_PATH"
    chmod 755 "$DATA_PATH"
    chmod 775 "$DATA_PATH/incoming"
    chmod 775 "$DATA_PATH/archive"
    chmod 775 "$DATA_PATH/temp"
    
    log_info "Directory structure created"
}

# Function to update configuration files
update_config_files() {
    log_step "Updating configuration files..."
    
    local config_files=("$INSTALL_PATH/appsettings.json" "$INSTALL_PATH/appsettings.Development.json")
    
    for config_file in "${config_files[@]}"; do
        if [ -f "$config_file" ]; then
            verbose_log "Updating $config_file"
            
            # Create backup
            cp "$config_file" "$config_file.backup"
            
            # Update paths using sed
            sed -i "s|\"Data Source=.*\"|\"Data Source=$DATA_PATH/database/databasegateway.db\"|g" "$config_file"
            sed -i "s|\"TempDirectory\": \".*\"|\"TempDirectory\": \"$DATA_PATH/temp/api-data\"|g" "$config_file"
            sed -i "s|\"FolderPath\": \".*\"|\"FolderPath\": \"$DATA_PATH/incoming\"|g" "$config_file"
            sed -i "s|\"ArchivePath\": \".*\"|\"ArchivePath\": \"$DATA_PATH/archive\"|g" "$config_file"
            
            log_info "Updated $config_file"
        fi
    done
}

# Function to create systemd service
create_systemd_service() {
    log_step "Creating systemd service..."
    
    cat > "/etc/systemd/system/$SERVICE_NAME.service" << EOF
[Unit]
Description=Azure Gateway API Service
After=network.target

[Service]
Type=notify
User=$SERVICE_USER
Group=$SERVICE_USER
WorkingDirectory=$INSTALL_PATH
ExecStart=/usr/bin/dotnet $INSTALL_PATH/AzureGateway.Api.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=$SERVICE_NAME
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
EOF

    # Reload systemd and enable service
    systemctl daemon-reload
    systemctl enable "$SERVICE_NAME"
    
    log_info "Systemd service created and enabled"
}

# Function to create startup script
create_startup_script() {
    cat > "$INSTALL_PATH/start.sh" << EOF
#!/bin/bash
cd "$INSTALL_PATH"
echo "Starting Azure Gateway API..."
dotnet AzureGateway.Api.dll
EOF

    chmod +x "$INSTALL_PATH/start.sh"
    chown "$SERVICE_USER:$SERVICE_USER" "$INSTALL_PATH/start.sh"
    
    log_info "Startup script created: $INSTALL_PATH/start.sh"
}

# Function to setup log rotation
setup_log_rotation() {
    log_step "Setting up log rotation..."
    
    cat > "/etc/logrotate.d/$SERVICE_NAME" << EOF
$INSTALL_PATH/logs/*.log {
    daily
    missingok
    rotate 30
    compress
    delaycompress
    notifempty
    create 644 $SERVICE_USER $SERVICE_USER
    postrotate
        systemctl reload $SERVICE_NAME || true
    endscript
}
EOF

    log_info "Log rotation configured"
}

# Main installation process
main() {
    echo -e "${GREEN}=== Azure Gateway API Linux Installation ===${NC}"
    echo "Install Path: $INSTALL_PATH"
    echo "Data Path: $DATA_PATH"
    echo "Service Name: $SERVICE_NAME"
    echo ""

    # Check prerequisites
    check_root
    detect_distro
    install_packages

    # Install .NET 8 if needed
    if [ "$SKIP_DOTNET" = false ]; then
        if ! check_dotnet8; then
            install_dotnet8
        else
            log_info ".NET 8 is already installed"
        fi
    fi

    # Create user and directories
    create_service_user
    create_directories

    # Check if application files exist
    if [ ! -f "$INSTALL_PATH/AzureGateway.Api.dll" ]; then
        log_warn "Application files not found in $INSTALL_PATH"
        log_info "Please publish the application to this directory:"
        echo "  cd path/to/AzureGateway.Api"
        echo "  dotnet publish -c Release -o \"$INSTALL_PATH\""
        echo "  sudo chown -R $SERVICE_USER:$SERVICE_USER \"$INSTALL_PATH\""
        echo ""
        log_info "Creating deployment guide..."
        create_deployment_guide
        log_info "Run this script again after copying the application files"
        exit 0
    fi

    # Configure application
    update_config_files
    create_systemd_service
    create_startup_script
    setup_log_rotation
    create_deployment_guide

    echo ""
    echo -e "${GREEN}=== Installation Complete ===${NC}"
    echo "Application Path: $INSTALL_PATH"
    echo "Data Path: $DATA_PATH"
    echo "Database: $DATA_PATH/database/databasegateway.db"
    echo "Service: $SERVICE_NAME"
    echo ""
    echo -e "${BLUE}Next Steps:${NC}"
    echo "1. Configure Azure Storage connection string in appsettings.json"
    echo "2. Start the service: sudo systemctl start $SERVICE_NAME"
    echo "3. Check status: sudo systemctl status $SERVICE_NAME"
    echo "4. Access API at http://localhost:5097"
    echo "5. View Swagger at http://localhost:5097/swagger"
    echo ""
    echo "For detailed instructions, see: $INSTALL_PATH/DEPLOYMENT_GUIDE.txt"
}

# Run main function
main "$@"