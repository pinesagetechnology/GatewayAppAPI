#!/bin/bash

# Azure Gateway API - Installation Test Script
# This script tests the installation and permissions

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log_info() { echo -e "${GREEN}✓${NC} $1"; }
log_warn() { echo -e "${YELLOW}⚠${NC} $1"; }
log_error() { echo -e "${RED}✗${NC} $1"; }
log_step() { echo -e "${BLUE}==>${NC} $1"; }

# Default values
INSTALL_PATH="/opt/azuregateway"
DATA_PATH=""
SERVICE_NAME="azuregateway"
SERVICE_USER="azuregateway"

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
        -h|--help)
            echo "Azure Gateway API - Installation Test Script"
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --install-path PATH    Installation directory (default: /opt/azuregateway)"
            echo "  --data-path PATH       Data directory (will prompt if not specified)"
            echo "  -h, --help            Show this help"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Prompt for data path if not specified
if [ -z "$DATA_PATH" ]; then
    echo ""
    echo -e "${BLUE}=== Data Directory Configuration ===${NC}"
    echo "Enter the data directory path that was configured during installation:"
    read -p "Data directory path: " DATA_PATH
    
    if [ -z "$DATA_PATH" ]; then
        log_error "Data path cannot be empty"
        exit 1
    fi
    
    # Expand tilde and make absolute
    DATA_PATH=$(eval echo "$DATA_PATH")
    if command -v realpath >/dev/null 2>&1; then
        DATA_PATH=$(realpath -m "$DATA_PATH")
    fi
fi

echo ""
echo -e "${GREEN}=== Azure Gateway API Installation Test ===${NC}"
echo "Install Path: $INSTALL_PATH"
echo "Data Path: $DATA_PATH"
echo "Service: $SERVICE_NAME"
echo ""

errors=0

# Test 1: Check .NET installation
log_step "Testing .NET installation..."
if command -v dotnet &> /dev/null && dotnet --list-runtimes | grep -q "Microsoft.AspNetCore.App 8"; then
    log_info ".NET 8 ASP.NET Core Runtime is installed"
else
    log_error ".NET 8 ASP.NET Core Runtime not found"
    ((errors++))
fi

# Test 2: Check directories
log_step "Testing directory structure..."
directories=(
    "$INSTALL_PATH"
    "$DATA_PATH"
    "$DATA_PATH/database"
    "$DATA_PATH/incoming"
    "$DATA_PATH/archive"
    "$DATA_PATH/temp"
)

for dir in "${directories[@]}"; do
    if [ -d "$dir" ]; then
        log_info "Directory exists: $dir"
    else
        log_error "Directory missing: $dir"
        ((errors++))
    fi
done

# Test 3: Check ownership
log_step "Testing directory ownership..."
for dir in "$INSTALL_PATH" "$DATA_PATH"; do
    if [ -d "$dir" ]; then
        owner=$(stat -c '%U:%G' "$dir" 2>/dev/null || stat -f '%Su:%Sg' "$dir" 2>/dev/null)
        if [ "$owner" = "$SERVICE_USER:$SERVICE_USER" ]; then
            log_info "Correct ownership for $dir: $owner"
        else
            log_warn "Incorrect ownership for $dir: $owner (expected: $SERVICE_USER:$SERVICE_USER)"
        fi
    fi
done

# Test 4: Check permissions
log_step "Testing directory permissions..."
for dir in "$DATA_PATH/incoming" "$DATA_PATH/archive" "$DATA_PATH/temp"; do
    if [ -d "$dir" ]; then
        perms=$(stat -c '%a' "$dir" 2>/dev/null || stat -f '%A' "$dir" 2>/dev/null)
        if [ "$perms" = "775" ]; then
            log_info "Correct permissions for $dir: $perms"
        else
            log_warn "Incorrect permissions for $dir: $perms (expected: 775)"
        fi
    fi
done

# Test 5: Check application files
log_step "Testing application files..."
if [ -f "$INSTALL_PATH/AzureGateway.Api.dll" ]; then
    log_info "Application files found"
else
    log_warn "Application files not found (manual deployment needed)"
fi

# Test 6: Check service configuration
log_step "Testing service configuration..."
if systemctl is-enabled "$SERVICE_NAME" &>/dev/null; then
    log_info "Service is configured and enabled"
else
    log_error "Service not configured or enabled"
    ((errors++))
fi

# Test 7: Test file operations as service user
log_step "Testing file operations as service user..."
if [ -d "$DATA_PATH/incoming" ]; then
    test_file="$DATA_PATH/incoming/test_$(date +%s).txt"
    if sudo -u "$SERVICE_USER" touch "$test_file" 2>/dev/null; then
        log_info "Service user can create files in incoming directory"
        sudo -u "$SERVICE_USER" rm -f "$test_file" 2>/dev/null || true
    else
        log_error "Service user cannot create files in incoming directory"
        ((errors++))
    fi
fi

# Test 8: Check ACL permissions
log_step "Testing ACL permissions..."
if command -v getfacl &> /dev/null; then
    for dir in "$DATA_PATH/incoming" "$DATA_PATH/archive" "$DATA_PATH/temp"; do
        if [ -d "$dir" ]; then
            if getfacl "$dir" 2>/dev/null | grep -q "user:$SERVICE_USER:rwx"; then
                log_info "ACL permissions set for $dir"
            else
                log_warn "ACL permissions not set for $dir"
            fi
        fi
    done
else
    log_warn "ACL tools not available - cannot test ACL permissions"
fi

# Summary
echo ""
if [ $errors -eq 0 ]; then
    echo -e "${GREEN}=== Test Results: PASSED ===${NC}"
    echo "All tests passed! Your Azure Gateway API installation is ready."
    echo ""
    echo "Next steps:"
    echo "1. Start the service: sudo systemctl start $SERVICE_NAME"
    echo "2. Check status: sudo systemctl status $SERVICE_NAME"
    echo "3. View logs: sudo journalctl -u $SERVICE_NAME -f"
    echo "4. Access API: http://localhost:5000"
    echo "5. Configure data sources via the API"
else
    echo -e "${RED}=== Test Results: FAILED ===${NC}"
    echo "Found $errors error(s). Please review and fix the issues above."
    echo ""
    echo "Common fixes:"
    echo "1. Run the installation script again"
    echo "2. Check file permissions: sudo chown -R $SERVICE_USER:$SERVICE_USER $DATA_PATH"
    echo "3. Check directory permissions: sudo chmod -R 775 $DATA_PATH"
    echo "4. Install .NET 8: https://docs.microsoft.com/en-us/dotnet/core/install/"
fi

exit $errors
