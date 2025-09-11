#!/usr/bin/env bash

# Fix User Access to Azure Gateway Directories
# This adds your current user to the azuregateway group so you can copy files

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

# Configuration
SERVICE_USER="azuregateway"
CURRENT_USER=$(logname 2>/dev/null || who am i | awk '{print $1}' || echo "$SUDO_USER")
DATA_PATH="/var/azuregateway"

echo -e "${BLUE}=== Fix User Access to Azure Gateway Directories ===${NC}"
echo ""

# Check if running with sudo
if [ "$EUID" -ne 0 ]; then
    error "This script must be run with sudo"
fi

log "Current user: $CURRENT_USER"
log "Service user: $SERVICE_USER"
log "Data path: $DATA_PATH"

# Function to add user to group
add_user_to_group() {
    log "Adding $CURRENT_USER to $SERVICE_USER group..."
    
    # Add current user to the service group
    usermod -a -G "$SERVICE_USER" "$CURRENT_USER"
    
    log "✓ Added $CURRENT_USER to $SERVICE_USER group"
}

# Function to set ACL permissions for current user
set_acl_permissions() {
    log "Setting ACL permissions for $CURRENT_USER..."
    
    # Install ACL if not present
    if ! command -v setfacl &> /dev/null; then
        log "Installing ACL tools..."
        if command -v apt-get &> /dev/null; then
            apt-get update
            apt-get install -y acl
        elif command -v yum &> /dev/null; then
            yum install -y acl
        elif command -v dnf &> /dev/null; then
            dnf install -y acl
        fi
    fi
    
    # Directories to fix
    directories=(
        "$DATA_PATH/incoming"
        "$DATA_PATH/archive" 
        "$DATA_PATH/temp"
        "$DATA_PATH/database"
    )
    
    for dir in "${directories[@]}"; do
        if [ -d "$dir" ]; then
            log "Setting ACL for: $dir"
            
            # Give current user read/write/execute access
            setfacl -R -m u:"$CURRENT_USER":rwx "$dir"
            # Set default ACL for new files
            setfacl -R -d -m u:"$CURRENT_USER":rwx "$dir"
            
            log "✓ ACL set for: $dir"
        else
            warn "Directory not found: $dir"
        fi
    done
}

# Function to test access
test_access() {
    log "Testing file access for $CURRENT_USER..."
    
    local test_file="$DATA_PATH/incoming/test_user_access_$$"
    
    # Test as current user (need to drop privileges)
    if sudo -u "$CURRENT_USER" touch "$test_file" 2>/dev/null; then
        sudo -u "$CURRENT_USER" rm -f "$test_file"
        log "✓ $CURRENT_USER can create files in incoming directory"
        return 0
    else
        error "✗ $CURRENT_USER still cannot create files in incoming directory"
        return 1
    fi
}

# Function to show directory info
show_directory_info() {
    log "Directory information:"
    echo ""
    
    for dir in "$DATA_PATH/incoming" "$DATA_PATH/archive" "$DATA_PATH/temp"; do
        if [ -d "$dir" ]; then
            echo -e "${BLUE}$dir:${NC}"
            ls -la "$dir" | head -2
            if command -v getfacl &> /dev/null; then
                echo "ACL permissions:"
                getfacl "$dir" 2>/dev/null | grep -E "(user:|group:)" || echo "No ACL set"
            fi
            echo ""
        fi
    done
}

# Function to create a file copy helper script
create_copy_helper() {
    local helper_script="/usr/local/bin/copy-to-incoming"
    
    cat > "$helper_script" << EOF
#!/usr/bin/env bash
# Helper script to copy files to Azure Gateway incoming directory

INCOMING_DIR="$DATA_PATH/incoming"

if [ \$# -eq 0 ]; then
    echo "Usage: copy-to-incoming <file1> [file2] [file3] ..."
    echo "Example: copy-to-incoming /path/to/data.json"
    exit 1
fi

for file in "\$@"; do
    if [ -f "\$file" ]; then
        filename=\$(basename "\$file")
        echo "Copying \$file to \$INCOMING_DIR/\$filename"
        cp "\$file" "\$INCOMING_DIR/\$filename"
        echo "✓ Copied \$filename"
    else
        echo "✗ File not found: \$file"
    fi
done
EOF
    
    chmod +x "$helper_script"
    log "✓ Created helper script: $helper_script"
    log "  Usage: copy-to-incoming /path/to/file.json"
}

# Main execution
main() {
    log "Starting user access fix..."
    
    # Validate current user
    if [ -z "$CURRENT_USER" ] || [ "$CURRENT_USER" = "root" ]; then
        error "Could not determine current user. Please run: sudo -u YOUR_USERNAME $0"
    fi
    
    # Check if user exists
    if ! id "$CURRENT_USER" &>/dev/null; then
        error "User '$CURRENT_USER' does not exist"
    fi
    
    # Check if service user exists  
    if ! id "$SERVICE_USER" &>/dev/null; then
        error "Service user '$SERVICE_USER' does not exist. Run the main permission fix script first."
    fi
    
    add_user_to_group
    set_acl_permissions
    create_copy_helper
    
    log "Waiting 2 seconds for group membership to take effect..."
    sleep 2
    
    if test_access; then
        log "✓ User access fix completed successfully!"
    else
        warn "Access test failed. You may need to log out and back in for group changes to take effect."
    fi
    
    show_directory_info
    
    echo ""
    echo -e "${BLUE}=== Summary ===${NC}"
    echo "✓ Added $CURRENT_USER to $SERVICE_USER group"
    echo "✓ Set ACL permissions for user access"
    echo "✓ Created copy helper script"
    echo ""
    echo -e "${BLUE}=== How to copy files now ===${NC}"
    echo "Method 1: Direct copy"
    echo "  cp /path/to/file.json $DATA_PATH/incoming/"
    echo ""
    echo "Method 2: Using helper script"
    echo "  copy-to-incoming /path/to/file.json"
    echo ""
    echo -e "${BLUE}=== Important ===${NC}"
    echo "If copying still fails, you may need to:"
    echo "1. Log out and log back in (for group membership)"
    echo "2. Or run: newgrp $SERVICE_USER"
    echo ""
}

# Run main function
main "$@"