<#
.SYNOPSIS
    Exports each worksheet from an Excel file to individual tab-delimited text files.

.DESCRIPTION
    This script opens an Excel workbook and exports each worksheet to a separate
    tab-delimited text file. The output files are named after the worksheet names.

.EXAMPLE
    .\Export-ExcelToTabs.ps1
    
    Prompts for Excel file path and output directory, then exports all worksheets.
#>

[CmdletBinding()]
param()

# Load Windows Forms assembly
Add-Type -AssemblyName System.Windows.Forms

# Function to validate file exists
function Test-FileExists {
    param([string]$Path)
    
    if (-not (Test-Path -Path $Path -PathType Leaf)) {
        Write-Error "File not found: $Path"
        return $false
    }
    
    if ($Path -notmatch '\.xlsx?$') {
        Write-Error "File must be an Excel file (.xls or .xlsx)"
        return $false
    }
    
    return $true
}

# Function to prompt for file with dialog
function Get-ExcelFilePath {
    $fileDialog = New-Object System.Windows.Forms.OpenFileDialog
    $fileDialog.Title = "Select Excel File"
    $fileDialog.InitialDirectory = [System.IO.Path]::Combine([System.Environment]::GetFolderPath('UserProfile'), 'Downloads')
    $fileDialog.Filter = "Excel Files (*.xlsx;*.xls)|*.xlsx;*.xls|All Files (*.*)|*.*"
    
    $result = $fileDialog.ShowDialog()
    
    if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
        return $fileDialog.FileName
    }
    else {
        return $null
    }
}

# Function to prompt for folder with dialog
function Get-FolderPath {
    param([string]$Title = "Select Folder")
    
    $folderDialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $folderDialog.Description = $Title
    $folderDialog.RootFolder = [System.Environment+SpecialFolder]::UserProfile
    
    $result = $folderDialog.ShowDialog()
    
    if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
        return $folderDialog.SelectedPath
    }
    else {
        return $null
    }
}

# Prompt for Excel file path
Write-Host "`n=== Excel Worksheet Exporter ===" -ForegroundColor Cyan
Write-Host "This script exports each worksheet to a tab-delimited text file.`n" -ForegroundColor Gray

do {
    Write-Host "Click 'Open' to select an Excel file, or press Cancel to enter path manually." -ForegroundColor Gray
    $excelPath = Get-ExcelFilePath
    
    if ([string]::IsNullOrWhiteSpace($excelPath)) {
        Write-Host "No file selected. Try again:`n" -ForegroundColor Yellow
        $manualEntry = Read-Host "Enter Excel file path manually (or leave blank to use file dialog again)"
        
        if ([string]::IsNullOrWhiteSpace($manualEntry)) {
            continue
        }
        
        $excelPath = $manualEntry.Trim('"')
    }
    
    $validFile = Test-FileExists -Path $excelPath
    
} while (-not $validFile)

# Prompt for output directory
Write-Host "`nExcel file: $excelPath" -ForegroundColor Green

do {
    Write-Host "`nClick 'OK' to select the output directory, or press Cancel to enter path manually." -ForegroundColor Gray
    $outputDir = Get-FolderPath -Title "Select Output Directory for Exported Files"
    
    if ([string]::IsNullOrWhiteSpace($outputDir)) {
        Write-Host "No directory selected. Try again:`n" -ForegroundColor Yellow
        $manualEntry = Read-Host "Enter output directory path manually (or leave blank to use folder dialog again)"
        
        if ([string]::IsNullOrWhiteSpace($manualEntry)) {
            continue
        }
        
        $outputDir = $manualEntry.Trim('"')
    }
    
    # Create directory if it doesn't exist
    if (-not (Test-Path -Path $outputDir)) {
        $create = Read-Host "Directory does not exist. Create it? (Y/N)"
        if ($create -eq 'Y' -or $create -eq 'y') {
            try {
                New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
                Write-Host "Directory created successfully." -ForegroundColor Green
                $validDir = $true
            }
            catch {
                Write-Host "Error creating directory: $_" -ForegroundColor Red
                $validDir = $false
            }
        }
        else {
            $validDir = $false
        }
    }
    else {
        $validDir = $true
    }
    
} while (-not $validDir)

Write-Host "`nOutput directory: $outputDir" -ForegroundColor Green

# Check for existing .iif files
$existingFiles = Get-ChildItem -Path $outputDir -Filter "*.iif" -ErrorAction SilentlyContinue
if ($existingFiles.Count -gt 0) {
    Write-Host "`nWarning: Found $($existingFiles.Count) existing .iif file(s) in output directory." -ForegroundColor Yellow
    Write-Host "Choose an option:" -ForegroundColor Cyan
    Write-Host "  1 - Overwrite all existing files" -ForegroundColor Gray
    Write-Host "  2 - Skip existing files" -ForegroundColor Gray
    Write-Host "  3 - Prompt for each file" -ForegroundColor Gray
    Write-Host "  Q - Quit without exporting" -ForegroundColor Gray
    
    do {
        $overwriteChoice = Read-Host "`nYour choice (1/2/3/Q)"
        $validChoice = $overwriteChoice -match '^[123Qq]$'
        if (-not $validChoice) {
            Write-Host "Invalid choice. Please enter 1, 2, 3, or Q." -ForegroundColor Yellow
        }
    } while (-not $validChoice)
    
    if ($overwriteChoice -eq 'Q' -or $overwriteChoice -eq 'q') {
        Write-Host "`nExport cancelled by user." -ForegroundColor Yellow
        exit 0
    }
}
else {
    $overwriteChoice = '1' # No existing files, proceed normally
}

Write-Host "`nStarting export..." -ForegroundColor Cyan

# Export worksheets
try {
    # Create Excel COM object
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    
    Write-Host "Opening workbook..." -ForegroundColor Gray
    $workbook = $excel.Workbooks.Open($excelPath)
    
    $sheetCount = $workbook.Worksheets.Count
    Write-Host "Found $sheetCount worksheet(s).`n" -ForegroundColor Gray
    
    $exportedCount = 0
    $skippedCount = 0
    $failedSheets = @()
    
    foreach ($sheet in $workbook.Worksheets) {
        $sheetName = $sheet.Name
        
        # Sanitize sheet name for file system
        $sanitizedName = $sheetName -replace '[\\/:*?"<>|]', '_'
        $outputFile = Join-Path $outputDir "$sanitizedName.iif"
        
        # Check if file exists and handle based on user choice
        $shouldExport = $true
        if (Test-Path $outputFile) {
            switch ($overwriteChoice) {
                '1' { 
                    # Overwrite all
                    $shouldExport = $true 
                }
                '2' { 
                    # Skip all
                    Write-Host "[SKIP] $sheetName (file exists)" -ForegroundColor DarkGray
                    $skippedCount++
                    $shouldExport = $false
                }
                '3' { 
                    # Prompt for each
                    $response = Read-Host "File exists: $sanitizedName.iif - Overwrite? (Y/N)"
                    $shouldExport = ($response -eq 'Y' -or $response -eq 'y')
                    if (-not $shouldExport) {
                        Write-Host "[SKIP] $sheetName" -ForegroundColor DarkGray
                        $skippedCount++
                    }
                }
            }
        }
        
        if ($shouldExport) {
            try {
                # xlTextWindows = 20, but using 42 for Unicode Text which preserves tabs
                $sheet.SaveAs($outputFile, 42)
                Write-Host "[OK] $sheetName" -ForegroundColor Green
                $exportedCount++
            }
            catch {
                Write-Host "[FAIL] $sheetName - $_" -ForegroundColor Red
                $failedSheets += $sheetName
            }
        }
    }
    
    # Close workbook without saving
    $workbook.Close($false)
    $excel.Quit()
    
    # Release COM objects
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($sheet) | Out-Null
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($workbook) | Out-Null
    [System.Runtime.Interopservices.Marshal]::ReleaseComObject($excel) | Out-Null
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
    
    # Summary
    Write-Host "`n=== Export Complete ===" -ForegroundColor Cyan
    Write-Host "Successfully exported: $exportedCount of $sheetCount worksheet(s)" -ForegroundColor Green
    
    if ($skippedCount -gt 0) {
        Write-Host "Skipped (file exists): $skippedCount worksheet(s)" -ForegroundColor DarkGray
    }
    
    if ($failedSheets.Count -gt 0) {
        Write-Host "`nFailed worksheets:" -ForegroundColor Yellow
        $failedSheets | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    }
    
    Write-Host "`nOutput location: $outputDir" -ForegroundColor Gray
}
catch {
    Write-Host "`nError: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    
    # Cleanup on error
    if ($workbook) { $workbook.Close($false) }
    if ($excel) { $excel.Quit() }
    
    exit 1
}

# Function to count transactions in an IIF file
function Get-IifTransactionCount {
    param(
        [string]$FilePath
    )
    
    try {
        $lines = Get-Content -Path $FilePath -Encoding UTF8
        $transactionCount = 0
        
        foreach ($line in $lines) {
            # Count TRNS lines (each represents one transaction)
            if ($line -match '^TRNS\t') {
                $transactionCount++
            }
        }
        
        return $transactionCount
    }
    catch {
        Write-Host "[WARN] Could not count transactions in $FilePath" -ForegroundColor Yellow
        return 0
    }
}

# Consolidate IIF files into single master file
function Consolidate-IifFiles {
    param(
        [string]$SourceDirectory,
        [string]$OutputFilePath
    )
    
    Write-Host "`n=== Consolidating IIF Files ===" -ForegroundColor Cyan
    
    $iifFiles = Get-ChildItem -Path $SourceDirectory -Filter "*.iif" -ErrorAction SilentlyContinue | 
                Where-Object { $_.Name -ne "consolodated.iif" }
    
    if ($iifFiles.Count -eq 0) {
        Write-Host "No IIF files found to consolidate." -ForegroundColor Yellow
        return $false
    }
    
    Write-Host "Found $($iifFiles.Count) IIF file(s) to consolidate.`n" -ForegroundColor Gray
    
    try {
        # Collect all headers and data in interleaved blocks (one IIF file at a time)
        $consolidatedBlocks = @()
        $sourceTransactionCounts = @{}
        $totalSourceTransactions = 0
        
        foreach ($file in $iifFiles) {
            Write-Host "Reading: $($file.Name)" -ForegroundColor Gray
            
            try {
                $lines = Get-Content -Path $file.FullName -Encoding UTF8
                
                $lineCount = 0
                $headerCount = 0
                $dataCount = 0
                $fileContent = @()
                $hasHeaders = $false
                $hasData = $false
                
                foreach ($line in $lines) {
                    $lineCount++
                    $trimmedLine = $line.TrimEnd("`r", "`n")
                    
                    # Skip empty lines and !ENDDATA
                    if ([string]::IsNullOrWhiteSpace($trimmedLine) -or $trimmedLine -eq "!ENDDATA") {
                        continue
                    }
                    
                    # Track if this file has IIF headers
                    if ($trimmedLine -match '^!') {
                        $hasHeaders = $true
                        $headerCount++
                        $fileContent += $trimmedLine
                    }
                    # Include all non-header lines (assume they're valid IIF data if file has headers)
                    else {
                        $dataCount++
                        $hasData = $true
                        $fileContent += $trimmedLine
                    }
                }
                
                # Only include this file if it has BOTH headers and data
                if ($hasHeaders -and $hasData) {
                    # Count transactions in this file
                    $transactionCount = 0
                    foreach ($line in $fileContent) {
                        if ($line -match '^TRNS\t') {
                            $transactionCount++
                        }
                    }
                    
                    $sourceTransactionCounts[$file.Name] = $transactionCount
                    $totalSourceTransactions += $transactionCount
                    
                    Write-Host "  [INCLUDE] Headers: $headerCount, Data: $dataCount, Transactions: $transactionCount" -ForegroundColor Green
                    $consolidatedBlocks += $fileContent
                }
                elseif ($hasHeaders -and -not $hasData) {
                    Write-Host "  [SKIP] Headers only, no data" -ForegroundColor Yellow
                }
                else {
                    Write-Host "  [SKIP] Not an IIF file (no headers)" -ForegroundColor DarkGray
                }
            }
            catch {
                Write-Host "[FAIL] Error reading $($file.Name): $_" -ForegroundColor Red
                return $false
            }
        }
        
        # Write consolidated file
        Write-Host "`nWriting consolidated file..." -ForegroundColor Gray
        $consolidatedContent = @()
        
        # Add all content blocks (headers + data for each file, in order)
        foreach ($block in $consolidatedBlocks) {
            $consolidatedContent += $block
        }
        
        # Write to file with proper encoding (no !ENDDATA - QB doesn't need it)
        $consolidatedContent | Out-File -FilePath $OutputFilePath -Encoding UTF8 -Force
        
        # Count transactions in consolidated file
        $consolidatedTransactions = Get-IifTransactionCount -FilePath $OutputFilePath
        
        Write-Host "`n=== Consolidation Summary ===" -ForegroundColor Cyan
        Write-Host "Total lines: $($consolidatedContent.Count)" -ForegroundColor Gray
        Write-Host "`nTransaction Counts:" -ForegroundColor Gray
        
        foreach ($fileName in $sourceTransactionCounts.Keys | Sort-Object) {
            Write-Host "  $fileName : $($sourceTransactionCounts[$fileName]) transactions" -ForegroundColor Gray
        }
        
        Write-Host "`nSource Total: $totalSourceTransactions transactions" -ForegroundColor Yellow
        Write-Host "Consolidated: $consolidatedTransactions transactions" -ForegroundColor Yellow
        
        if ($consolidatedTransactions -eq $totalSourceTransactions) {
            Write-Host "`n[VALIDATED] All transactions included successfully!" -ForegroundColor Green
        }
        else {
            $diff = $totalSourceTransactions - $consolidatedTransactions
            Write-Host "`n[WARNING] Transaction count mismatch! Missing $diff transactions." -ForegroundColor Red
        }
        
        Write-Host "`n[OK] Created: consolodated.iif" -ForegroundColor Green
        
        return $true
    }
    catch {
        Write-Host "[FAIL] Error consolidating files: $_" -ForegroundColor Red
        return $false
    }
}

# Offer to consolidate IIF files
if ($exportedCount -gt 0) {
    Write-Host "`n" -ForegroundColor Cyan
    $consolidate = Read-Host "Consolidate exported IIF files into single file? (Y/N)"
    if ($consolidate -eq 'Y' -or $consolidate -eq 'y') {
        $consolidatedPath = Join-Path $outputDir "consolodated.iif"
        $success = Consolidate-IifFiles -SourceDirectory $outputDir -OutputFilePath $consolidatedPath
        
        if ($success) {
            Write-Host "`nConsolidated file location: $consolidatedPath" -ForegroundColor Green
            
            $openFile = Read-Host "Open consolidation summary? (Y/N)"
            if ($openFile -eq 'Y' -or $openFile -eq 'y') {
                notepad $consolidatedPath
            }
        }
    }
}

# Offer to open output directory (after all processing)
$openDir = Read-Host "`nOpen output directory in Explorer? (Y/N)"
if ($openDir -eq 'Y' -or $openDir -eq 'y') {
    Start-Process explorer.exe -ArgumentList $outputDir
}

Write-Host "`nPress any key to exit..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
