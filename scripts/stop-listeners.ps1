$ports = 7215,5022
$pids = @()
foreach ($p in $ports) {
    $lines = netstat -ano -p tcp | Select-String ":$p"
    foreach ($line in $lines) {
        $cols = ($line -replace '\s+', ' ' -split ' ')
        $pidVar = $cols[-1]
        if ($pidVar -and ($pids -notcontains $pidVar)) { $pids += [int]$pidVar }
    }
}

if ($pids.Count -eq 0) {
    Write-Host 'No processes found listening on ports 7215 or 5022.'
    exit 0
}

Write-Host ('Found PIDs: {0}' -f ($pids -join ', '))
foreach ($pidVar in $pids) {
    $proc = Get-Process -Id $pidVar -ErrorAction SilentlyContinue
    if ($proc) { Write-Host ('PID {0} : {1}' -f $proc.Id, $proc.ProcessName) }
}

Write-Host 'Stopping processes...'
foreach ($pidVar in $pids) {
    try {
        Stop-Process -Id $pidVar -Force -ErrorAction Stop
        Write-Host ('Stopped PID {0}' -f $pidVar)
    } catch {
        Write-Warning ('Failed to stop PID {0}: {1}' -f $pidVar, $_)
    }
}
