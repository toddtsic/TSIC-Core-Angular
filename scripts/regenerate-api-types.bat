@echo off
setlocal enabledelayedexpansion

echo ========================================
echo   TypeScript API Type Regeneration
echo ========================================
echo.

REM Check if API is running
curl -s -k https://localhost:7215/swagger/v1/swagger.json >nul 2>&1
if errorlevel 1 (
    curl -s http://localhost:5022/swagger/v1/swagger.json >nul 2>&1
    if errorlevel 1 (
        echo API not running, starting it...
        
        cd /d "%~dp0..\TSIC-Core-Angular\src\backend\TSIC.API"
        start "TSIC API" dotnet watch run --launch-profile https --non-interactive
        
        echo Waiting for API to be ready...
        for /L %%i in (1,1,30) do (
            ping 127.0.0.1 -n 2 >nul
            curl -s -k https://localhost:7215/swagger/v1/swagger.json >nul 2>&1
            if not errorlevel 1 goto :api_ready
            curl -s http://localhost:5022/swagger/v1/swagger.json >nul 2>&1
            if not errorlevel 1 goto :api_ready
        )
        
        echo ERROR: API failed to start within 30 seconds
        exit /b 1
    )
)

:api_ready
echo API is ready
echo.

REM Run openapi-typescript-codegen generation
echo Generating TypeScript types with openapi-typescript-codegen...

cd /d "%~dp0..\TSIC-Core-Angular\src\frontend\tsic-app"
call npm run generate:api

if errorlevel 1 (
    echo ERROR: Type generation failed
    exit /b 1
)

echo Type generation completed
echo.

REM Verify generated types
echo Verifying generated types...

findstr /c:"playerId:" src\app\core\api\models\models\FamilyPlayerDto.ts >nul 2>&1
if errorlevel 1 (
    echo ERROR: Verification failed - playerId type incorrect
    exit /b 1
)

findstr /c:"export type FamilyPlayerDto" src\app\core\api\models\models\FamilyPlayerDto.ts >nul 2>&1
if errorlevel 1 (
    echo ERROR: Verification failed - FamilyPlayerDto not found
    exit /b 1
)

echo Generated types verified
echo.
echo ========================================
echo   Complete
echo ========================================
