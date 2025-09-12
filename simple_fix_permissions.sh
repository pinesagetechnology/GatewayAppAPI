#!/usr/bin/env bash

# Simple Permission Fix Script
# Asks for a directory path and fixes permissions for both service and user access

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

# Default service user
SERVICE_USER="azuregateway"
CURRENT_USER=$(logname 2>/dev/null || who am i | awk '{print $1}' || echo "$SUDO_USER")

echo -e "${BLUE}=== Simple Permission Fix Script ===${NC}"
echo ""

# Check if running with sudo
if [ "$EUID" -ne 0 ]; then
    error "This script must be run with sudo"
fi

# Ask for directory path
echo -e "${YELLOW}Enter the directory path to fix permissions:${NC}"
read -p "Directory path: " TARGET_DIR

# Validate directory path
if [ -z "$TARGET_DIR" ]; then
    error "No directory path provided"
fi

# Expand tilde and resolve path
TARGET_DIR=$(eval echo "$TARGET_DIR")

# Check if directory exists
if [ ! -d "$TARGET_DIR" ]; then
    warn "Directory does not exist. Creating: $TARGET_DIR"
    mkdir -p "$TARGET_DIR"
fi

log "Target directory: $TARGET_DIR"
log "Service user: $SERVICE_USER"
log "Current user: $CURRENT_USER"

# Function to fix permissions
fix_permissions() {
    log "Setting ownership and permissions..."
    
    # Set ownership to service user
    chown -R "$SERVICE_USER:$SERVICE_USER" "$TARGET_DIR"
    
    # Set permissions (rwx for user and group, read-only for others)
    chmod -R 775 "$TARGET_DIR"
    
    log "✓ Ownership and permissions set"
}

# Function to install ACL if needed
install_acl() {
    if ! command -v setfacl &> /dev/null; then
        log "Installing ACL tools..."
        if command -v apt-get &> /dev/null; then
            apt-get update
            apt-get install -y acl
        elif command -v yum &> /dev/null; then
            yum install -y acl
        elif command -v dnf &> /dev/null; then
            dnf install -y acl
        else
            warn "Could not install ACL tools automatically"
            return 1
        fi
    fi
    return 0
}

# Function to set ACL permissions
set_acl_permissions() {
    if install_acl; then
        log "Setting ACL permissions..."
        
        # Set ACL for service user
        setfacl -R -m u:"$SERVICE_USER":rwx "$TARGET_DIR"
        setfacl -R -d -m u:"$SERVICE_USER":rwx "$TARGET_DIR"
        
        # Set ACL for current user
        if [ -n "$CURRENT_USER" ] && [ "$CURRENT_USER" != "root" ]; then
            setfacl -R -m u:"$CURRENT_USER":rwx "$TARGET_DIR"
            setfacl -R -d -m u:"$CURRENT_USER":rwx "$TARGET_DIR"
            log "✓ ACL permissions set for both $SERVICE_USER and $CURRENT_USER"
        else
            log "✓ ACL permissions set for $SERVICE_USER"
        fi
    else
        warn "ACL permissions not set (ACL tools not available)"
    fi
}

# Function to add user to group
add_user_to_group() {
    if [ -n "$CURRENT_USER" ] && [ "$CURRENT_USER" != "root" ]; then
        log "Adding $CURRENT_USER to $SERVICE_USER group..."
        usermod -a -G "$SERVICE_USER" "$CURRENT_USER"
        log "✓ Added $CURRENT_USER to $SERVICE_USER group"
    fi
}

# Function to test permissions
test_permissions() {
    log "Testing permissions..."
    
    local test_file="$TARGET_DIR/.permission_test_$$"
    
    # Test service user access
    if sudo -u "$SERVICE_USER" touch "$test_file" 2>/dev/null; then
        sudo -u "$SERVICE_USER" rm -f "$test_file"
        log "✓ $SERVICE_USER can write to directory"
    else
        error "✗ $SERVICE_USER cannot write to directory"
    fi
    
    # Test current user access
    if [ -n "$CURRENT_USER" ] && [ "$CURRENT_USER" != "root" ]; then
        if sudo -u "$CURRENT_USER" touch "$test_file" 2>/dev/null; then
            sudo -u "$CURRENT_USER" rm -f "$test_file"
            log "✓ $CURRENT_USER can write to directory"
        else
            warn "✗ $CURRENT_USER cannot write to directory (may need to log out/in)"
        fi
    fi
}

# Function to show final information
show_summary() {
    echo ""
    echo -e "${BLUE}=== Summary ===${NC}"
    echo "Directory: $TARGET_DIR"
    echo "Service User: $SERVICE_USER"
    echo "Current User: $CURRENT_USER"
    echo ""
    echo -e "${BLUE}=== Directory Info ===${NC}"
    ls -la "$TARGET_DIR" | head -5
    echo ""
    if command -v getfacl &> /dev/null; then
        echo -e "${BLUE}=== ACL Permissions ===${NC}"
        getfacl "$TARGET_DIR" 2>/dev/null | head -10
    fi
    echo ""
    echo -e "${BLUE}=== Next Steps ===${NC}"
    echo "1. The directory permissions have been fixed"
    echo "2. You can now copy files to: $TARGET_DIR"
    echo "3. If you still can't access, try: newgrp $SERVICE_USER"
    echo "4. Or log out and log back in"
}

# Main execution
main() {
    log "Starting permission fix for: $TARGET_DIR"
    
    # Validate users exist
    if ! id "$SERVICE_USER" &>/dev/null; then
        error "Service user '$SERVICE_USER' does not exist"
    fi
    
    if [ -n "$CURRENT_USER" ] && [ "$CURRENT_USER" != "root" ]; then
        if ! id "$CURRENT_USER" &>/dev/null; then
            error "Current user '$CURRENT_USER' does not exist"
        fi
    fi
    
    fix_permissions
    set_acl_permissions
    add_user_to_group
    test_permissions
    show_summary
    
    log "Permission fix completed successfully!"
}

# Run main function
main "$@"
