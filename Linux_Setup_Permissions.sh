#!/bin/bash

# Azure Gateway API - Raspberry Pi Folder Permissions Setup Script
# This script sets up proper permissions for the Azure Gateway API to access folders

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
API_USER="azuregateway"
API_GROUP="azuregateway"
SERVICE_NAME="azuregateway-api"
BASE_DIR="/home/alirk/azuregateway"
INCOMING_DIR="$BASE_DIR/incoming"
ARCHIVE_DIR="$BASE_DIR/archive"
LOGS_DIR="$BASE_DIR/logs"
TEMP_DIR="$BASE_DIR/temp"

echo -e "${BLUE}=== Azure Gateway API - Raspberry Pi Permissions Setup ===${NC}"
echo ""

# Function to print status
print_status() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    print_error "This script must be run as root (use sudo)"
    exit 1
fi

print_status "Setting up folder permissions for Azure Gateway API..."

# 1. Create API user and group if they don't exist
print_status "Creating API user and group..."
if ! id "$API_USER" &>/dev/null; then
    useradd -r -s /bin/false -d "$BASE_DIR" -c "Azure Gateway API Service" "$API_USER"
    print_status "Created user: $API_USER"
else
    print_status "User $API_USER already exists"
fi

if ! getent group "$API_GROUP" &>/dev/null; then
    groupadd "$API_GROUP"
    print_status "Created group: $API_GROUP"
else
    print_status "Group $API_GROUP already exists"
fi

# Add API user to the group
usermod -a -G "$API_GROUP" "$API_USER"
print_status "Added $API_USER to group $API_GROUP"

# 2. Create base directory structure
print_status "Creating directory structure..."
mkdir -p "$BASE_DIR"
mkdir -p "$INCOMING_DIR"
mkdir -p "$ARCHIVE_DIR"
mkdir -p "$LOGS_DIR"
mkdir -p "$TEMP_DIR"

# 3. Set ownership
print_status "Setting ownership..."
chown -R "$API_USER:$API_GROUP" "$BASE_DIR"

# 4. Set permissions
print_status "Setting permissions..."

# Base directory: read/execute for owner and group
chmod 755 "$BASE_DIR"

# Incoming directory: full access for owner and group, read for others
chmod 775 "$INCOMING_DIR"

# Archive directory: full access for owner and group, read for others
chmod 775 "$ARCHIVE_DIR"

# Logs directory: full access for owner and group, read for others
chmod 775 "$LOGS_DIR"

# Temp directory: full access for owner and group, read for others
chmod 775 "$TEMP_DIR"

# 5. Set up ACL (Access Control Lists) for better permission management
print_status "Setting up ACL permissions..."

# Install ACL tools if not present
if ! command -v setfacl &> /dev/null; then
    print_status "Installing ACL tools..."
    apt-get update
    apt-get install -y acl
fi

# Set ACL permissions
setfacl -R -m u:"$API_USER":rwx "$INCOMING_DIR"
setfacl -R -m g:"$API_GROUP":rwx "$INCOMING_DIR"
setfacl -R -m u:"$API_USER":rwx "$ARCHIVE_DIR"
setfacl -R -m g:"$API_GROUP":rwx "$ARCHIVE_DIR"
setfacl -R -m u:"$API_USER":rwx "$LOGS_DIR"
setfacl -R -m g:"$API_GROUP":rwx "$LOGS_DIR"
setfacl -R -m u:"$API_USER":rwx "$TEMP_DIR"
setfacl -R -m g:"$API_GROUP":rwx "$TEMP_DIR"

# Set default ACL for new files
setfacl -R -d -m u:"$API_USER":rwx "$INCOMING_DIR"
setfacl -R -d -m g:"$API_GROUP":rwx "$INCOMING_DIR"
setfacl -R -d -m u:"$API_USER":rwx "$ARCHIVE_DIR"
setfacl -R -d -m g:"$API_GROUP":rwx "$ARCHIVE_DIR"
setfacl -R -d -m u:"$API_USER":rwx "$LOGS_DIR"
setfacl -R -d -m g:"$API_GROUP":rwx "$LOGS_DIR"
setfacl -R -d -m u:"$API_USER":rwx "$TEMP_DIR"
setfacl -R -d -m g:"$API_GROUP":rwx "$TEMP_DIR"

# 6. Create systemd service file
print_status "Creating systemd service configuration..."
SERVICE_FILE="/etc/systemd/system/$SERVICE_NAME.service"

cat > "$SERVICE_FILE" << EOF
[Unit]
Description=Azure Gateway API Service
After=network.target

[Service]
Type=notify
User=$API_USER
Group=$API_GROUP
WorkingDirectory=$BASE_DIR
ExecStart=/usr/bin/dotnet $BASE_DIR/AzureGateway.Api.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=$SERVICE_NAME
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://+:5000

# Security settings
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=$BASE_DIR
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectControlGroups=true

[Install]
WantedBy=multi-user.target
EOF

# 7. Reload systemd and enable service
print_status "Reloading systemd configuration..."
systemctl daemon-reload
systemctl enable "$SERVICE_NAME"

# 8. Create a test script to verify permissions
print_status "Creating permission test script..."
TEST_SCRIPT="$BASE_DIR/test_permissions.sh"

cat > "$TEST_SCRIPT" << 'EOF'
#!/bin/bash
echo "=== Permission Test ==="
echo "Current user: $(whoami)"
echo "Current group: $(groups)"
echo ""

echo "Testing folder access..."
BASE_DIR="/home/alirk/azuregateway"
INCOMING_DIR="$BASE_DIR/incoming"
ARCHIVE_DIR="$BASE_DIR/archive"
LOGS_DIR="$BASE_DIR/logs"
TEMP_DIR="$BASE_DIR/temp"

for dir in "$INCOMING_DIR" "$ARCHIVE_DIR" "$LOGS_DIR" "$TEMP_DIR"; do
    if [ -d "$dir" ]; then
        echo "✓ $dir exists"
        if [ -w "$dir" ]; then
            echo "✓ $dir is writable"
        else
            echo "✗ $dir is NOT writable"
        fi
    else
        echo "✗ $dir does not exist"
    fi
done

echo ""
echo "Testing file creation..."
TEST_FILE="$INCOMING_DIR/test_$(date +%s).txt"
if echo "test" > "$TEST_FILE" 2>/dev/null; then
    echo "✓ Can create files in incoming directory"
    rm -f "$TEST_FILE"
else
    echo "✗ Cannot create files in incoming directory"
fi
EOF

chmod +x "$TEST_SCRIPT"
chown "$API_USER:$API_GROUP" "$TEST_SCRIPT"

# 9. Display summary
echo ""
echo -e "${GREEN}=== Setup Complete ===${NC}"
echo ""
print_status "Directory structure created:"
echo "  Base: $BASE_DIR"
echo "  Incoming: $INCOMING_DIR"
echo "  Archive: $ARCHIVE_DIR"
echo "  Logs: $LOGS_DIR"
echo "  Temp: $TEMP_DIR"
echo ""
print_status "User/Group: $API_USER:$API_GROUP"
print_status "Service: $SERVICE_NAME"
echo ""
print_status "To test permissions, run:"
echo "  sudo -u $API_USER $TEST_SCRIPT"
echo ""
print_status "To start the service:"
echo "  sudo systemctl start $SERVICE_NAME"
echo ""
print_status "To check service status:"
echo "  sudo systemctl status $SERVICE_NAME"
echo ""
print_status "To view logs:"
echo "  sudo journalctl -u $SERVICE_NAME -f"
echo ""

# 10. Test permissions as the API user
print_status "Testing permissions as API user..."
if sudo -u "$API_USER" "$TEST_SCRIPT"; then
    print_status "Permission test completed successfully!"
else
    print_warning "Permission test had issues. Check the output above."
fi

echo ""
echo -e "${GREEN}Setup completed! Your Azure Gateway API should now have proper access to all folders.${NC}"
