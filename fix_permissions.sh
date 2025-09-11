#!/usr/bin/env bash

# Fix Directory Permissions for Azure Gateway IoT Deployment
# Run this script on your IoT device to fix permission issues

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging functions
log() { echo -e "${GREEN}[$(date +'%Y-%m-%d %H:%M:%S')] $1${NC}"; }
warn() { echo -e "${YELLOW}[$(date +'%Y-%m-%d %H:%M:%S')] WARNING: $1${NC}"; }
error() { echo -e "${RED}[$(date +'%Y-%m-%d %H:%M:%S')] ERROR: $1${NC}"; exit 1; }

# Default configuration
SERVICE_USER="azuregateway"
DATA_BASE_PATH="/var/azuregateway"
APP_PATH="/opt/azuregateway"

# Function to detect the current service user
detect_service_user() {
    log "Detecting service user..."
    
    # Check if service exists and get user
    if systemctl list-units --type=service | grep -q "azuregateway"; then
        SERVICE_USER=$(systemctl show azuregateway.service -p User --value 2>/dev/null || echo "")
    fi
    
    # Fallback checks
    if [ -z "$SERVICE_USER" ] || [ "$SERVICE_USER" = "root" ]; then
        # Check common usernames
        for user in azuregateway pi jetson ubuntu; do
            if id "$user" &>/dev/null; then
                SERVICE_USER="$user"
                break
            fi
        done
    fi
    
    log "Using service user: $SERVICE_USER"
}

# Function to detect paths from configuration
detect_paths_from_config() {
    log "Detecting paths from configuration..."
    
    local config_file="$APP_PATH/appsettings.json"
    
    if [ -f "$config_file" ]; then
        log "Found configuration file: $config_file"
        
        # Extract database path
        DB_PATH=$(grep -o '"Data Source=[^"]*"' "$config_file" | sed 's/"Data Source=\([^"]*\)"/\1/' | head -1)
        if [ -n "$DB_PATH" ]; then
            DATA_BASE_PATH=$(dirname "$DB_PATH" | sed 's|/database$||')
            log "Detected data base path from config: $DATA_BASE_PATH"
        fi
        
        # Extract other paths
        TEMP_PATH=$(grep -o '"TempDirectory":[^,}]*' "$config_file" | sed 's/"TempDirectory":[[:space:]]*"\([^"]*\)"/\1/')
        INCOMING_PATH=$(grep -o '"FolderPath":[^,}]*' "$config_file" | sed 's/"FolderPath":[[:space:]]*"\([^"]*\)"/\1/')
        ARCHIVE_PATH=$(grep -o '"ArchivePath":[^,}]*' "$config_file" | sed 's/"ArchivePath":[[:space:]]*"\([^"]*\)"/\1/')
        
        if [ -n "$TEMP_PATH" ]; then log "Detected temp path: $TEMP_PATH"; fi
        if [ -n "$INCOMING_PATH" ]; then log "Detected incoming path: $INCOMING_PATH"; fi
        if [ -n "$ARCHIVE_PATH" ]; then log "Detected archive path: $ARCHIVE_PATH"; fi
    else
        warn "Configuration file not found, using default paths"
    fi
}

# Function to create directories with proper permissions
create_directories() {
    log "Creating directory structure..."
    
    # Define the 4 main directories
    local directories=(
        "$DATA_BASE_PATH/database"
        "$DATA_BASE_PATH/incoming"
        "$DATA_BASE_PATH/archive"
        "$DATA_BASE_PATH/temp"
        "$DATA_BASE_PATH/logs"
    )
    
    # Add custom paths if detected
    [ -n "$TEMP_PATH" ] && directories+=("$TEMP_PATH")
    [ -n "$INCOMING_PATH" ] && directories+=("$INCOMING_PATH")
    [ -n "$ARCHIVE_PATH" ] && directories+=("$ARCHIVE_PATH")
    
    for dir in "${directories[@]}"; do
        if [ -n "$dir" ]; then
            log "Creating directory: $dir"
            sudo mkdir -p "$dir"
            
            # Set ownership
            sudo chown -R "$SERVICE_USER:$SERVICE_USER" "$dir"
            
            # Set permissions (rwx for user and group, read-only for others)
            sudo chmod -R 775 "$dir"
            
            log "✓ Created and configured: $dir"
        fi
    done
}

# Function to set ACL permissions (Advanced)
setup_acl_permissions() {
    log "Setting up ACL permissions for better access control..."
    
    # Check if ACL tools are installed
    if ! command -v setfacl &> /dev/null; then
        log "Installing ACL tools..."
        if command -v apt-get &> /dev/null; then
            sudo apt-get update
            sudo apt-get install -y acl
        elif command -v yum &> /dev/null; then
            sudo yum install -y acl
        elif command -v dnf &> /dev/null; then
            sudo dnf install -y acl
        else
            warn "Could not install ACL tools automatically. Please install manually."
            return
        fi
    fi
    
    local directories=(
        "$DATA_BASE_PATH/database"
        "$DATA_BASE_PATH/incoming"
        "$DATA_BASE_PATH/archive"
        "$DATA_BASE_PATH/temp"
        "$DATA_BASE_PATH/logs"
    )
    
    # Add custom paths
    [ -n "$TEMP_PATH" ] && directories+=("$TEMP_PATH")
    [ -n "$INCOMING_PATH" ] && directories+=("$INCOMING_PATH")
    [ -n "$ARCHIVE_PATH" ] && directories+=("$ARCHIVE_PATH")
    
    for dir in "${directories[@]}"; do
        if [ -n "$dir" ] && [ -d "$dir" ]; then
            log "Setting ACL for: $dir"
            
            # Set ACL permissions for the service user
            sudo setfacl -R -m u:"$SERVICE_USER":rwx "$dir"
            sudo setfacl -R -m g:"$SERVICE_USER":rwx "$dir"
            
            # Set default ACL for new files/directories
            sudo setfacl -R -d -m u:"$SERVICE_USER":rwx "$dir"
            sudo setfacl -R -d -m g:"$SERVICE_USER":rwx "$dir"
            
            log "✓ ACL configured for: $dir"
        fi
    done
}

# Function to fix systemd service permissions
fix_systemd_service() {
    log "Checking and fixing systemd service configuration..."
    
    local service_file="/etc/systemd/system/azuregateway.service"
    
    if [ -f "$service_file" ]; then
        log "Found systemd service file"
        
        # Check if ReadWritePaths includes our data directories
        if ! grep -q "ReadWritePaths.*$DATA_BASE_PATH" "$service_file"; then
            log "Adding ReadWritePaths to systemd service..."
            
            # Create backup
            sudo cp "$service_file" "$service_file.backup"
            
            # Add ReadWritePaths if it doesn't exist
            if grep -q "ReadWritePaths=" "$service_file"; then
                sudo sed -i "s|ReadWritePaths=.*|ReadWritePaths=$APP_PATH $DATA_BASE_PATH|" "$service_file"
            else
                sudo sed -i "/\[Service\]/a ReadWritePaths=$APP_PATH $DATA_BASE_PATH" "$service_file"
            fi
            
            log "✓ Updated systemd service configuration"
            
            # Reload systemd
            sudo systemctl daemon-reload
            log "✓ Reloaded systemd daemon"
        else
            log "✓ Systemd service already configured correctly"
        fi
    else
        warn "Systemd service file not found at $service_file"
    fi
}

# Function to test directory access
test_directory_access() {
    log "Testing directory access..."
    
    local directories=(
        "$DATA_BASE_PATH/database"
        "$DATA_BASE_PATH/incoming"
        "$DATA_BASE_PATH/archive"
        "$DATA_BASE_PATH/temp"
    )
    
    # Add custom paths
    [ -n "$TEMP_PATH" ] && directories+=("$TEMP_PATH")
    [ -n "$INCOMING_PATH" ] && directories+=("$INCOMING_PATH")
    [ -n "$ARCHIVE_PATH" ] && directories+=("$ARCHIVE_PATH")
    
    local test_failed=false
    
    for dir in "${directories[@]}"; do
        if [ -n "$dir" ] && [ -d "$dir" ]; then
            # Test as service user
            if sudo -u "$SERVICE_USER" test -w "$dir"; then
                log "✓ $SERVICE_USER can write to: $dir"
            else
                error "✗ $SERVICE_USER cannot write to: $dir"
                test_failed=true
            fi
            
            # Test creating a file
            local test_file="$dir/.permission_test_$$"
            if sudo -u "$SERVICE_USER" touch "$test_file" 2>/dev/null; then
                sudo -u "$SERVICE_USER" rm -f "$test_file"
                log "✓ File creation test passed: $dir"
            else
                error "✗ File creation test failed: $dir"
                test_failed=true
            fi
        fi
    done
    
    if [ "$test_failed" = true ]; then
        error "Some permission tests failed. Please check the errors above."
    else
        log "✓ All permission tests passed!"
    fi
}

# Function to create a test script for ongoing monitoring
create_test_script() {
    log "Creating ongoing permission test script..."
    
    cat > "/tmp/test_permissions.sh" << 'EOF'
#!/bin/bash
# Quick permission test script

SERVICE_USER="azuregateway"
DATA_PATH="/var/azuregateway"

echo "Testing permissions for $SERVICE_USER..."

for dir in "$DATA_PATH/database" "$DATA_PATH/incoming" "$DATA_PATH/archive" "$DATA_PATH/temp"; do
    if [ -d "$dir" ]; then
        if sudo -u "$SERVICE_USER" test -w "$dir"; then
            echo "✓ $dir - OK"
        else
            echo "✗ $dir - PERMISSION DENIED"
        fi
    else
        echo "? $dir - DOES NOT EXIST"
    fi
done
EOF
    
    chmod +x "/tmp/test_permissions.sh"
    log "✓ Test script created at /tmp/test_permissions.sh"
}

# Function to show final information
show_final_info() {
    log "Permission fix completed!"
    echo ""
    echo -e "${BLUE}=== Configuration Summary ===${NC}"
    echo "Service User: $SERVICE_USER"
    echo "Data Base Path: $DATA_BASE_PATH"
    echo "App Path: $APP_PATH"
    echo ""
    echo -e "${BLUE}=== Directory Structure ===${NC}"
    echo "Database: $DATA_BASE_PATH/database"
    echo "Incoming: $DATA_BASE_PATH/incoming"
    echo "Archive:  $DATA_BASE_PATH/archive"
    echo "Temp:     $DATA_BASE_PATH/temp"
    echo "Logs:     $DATA_BASE_PATH/logs"
    echo ""
    echo -e "${BLUE}=== Next Steps ===${NC}"
    echo "1. Restart the service: sudo systemctl restart azuregateway"
    echo "2. Check service status: sudo systemctl status azuregateway"
    echo "3. Test permissions: /tmp/test_permissions.sh"
    echo "4. View logs: sudo journalctl -u azuregateway -f"
    echo ""
    echo -e "${BLUE}=== Useful Commands ===${NC}"
    echo "Check directory permissions: ls -la $DATA_BASE_PATH"
    echo "Check ACL permissions: getfacl $DATA_BASE_PATH/incoming"
    echo "Test as service user: sudo -u $SERVICE_USER ls -la $DATA_BASE_PATH/incoming"
}

# Main execution
main() {
    log "Starting permission fix for Azure Gateway directories..."
    
    # Check if running as root or with sudo
    if [ "$EUID" -ne 0 ]; then
        error "This script must be run as root or with sudo"
    fi
    
    detect_service_user
    detect_paths_from_config
    create_directories
    setup_acl_permissions
    fix_systemd_service
    test_directory_access
    create_test_script
    show_final_info
    
    log "Permission fix script completed successfully!"
}

# Handle script interruption
trap 'error "Script interrupted"' INT TERM

# Run main function
main "$@"