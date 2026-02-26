Get-CimInstance Win32_Process |
    Where-Object { $_.CommandLine -and ($_.CommandLine -like '*TSIC*' -or $_.CommandLine -like '*dotnet*' -or $_.CommandLine -like '*OmniSharp*' -or $_.CommandLine -like '*razor*') } |
    Select-Object ProcessId, Name, @{N='CmdLine';E={$_.CommandLine.Substring(0, [Math]::Min(150, $_.CommandLine.Length))}} |
    Format-Table -AutoSize -Wrap
