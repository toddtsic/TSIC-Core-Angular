# ============================================================================
# 06-Configure-Firewall.ps1 — Open HTTP (80) and HTTPS (443) inbound
# ============================================================================
# Idempotent — skips rules that already exist.
# ============================================================================

#Requires -RunAsAdministrator

Write-Host ""
Write-Host "[Step 6] Configuring firewall rules..." -ForegroundColor Green

$rules = @(
    @{ Name = 'TSIC HTTP Inbound';  Port = 80  },
    @{ Name = 'TSIC HTTPS Inbound'; Port = 443 }
)

foreach ($rule in $rules) {
    $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "  Firewall rule exists: $($rule.Name)" -ForegroundColor DarkGray
    }
    else {
        New-NetFirewallRule -DisplayName $rule.Name `
            -Direction Inbound `
            -Protocol TCP `
            -LocalPort $rule.Port `
            -Action Allow | Out-Null
        Write-Host "  Created firewall rule: $($rule.Name) (TCP $($rule.Port))" -ForegroundColor Green
    }
}

Write-Host "[Step 6] Complete." -ForegroundColor Green
