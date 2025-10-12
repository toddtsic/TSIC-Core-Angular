# Launch .NET API
Start-Process powershell -WorkingDirectory "$PSScriptRoot\..\TSIC-Core-Angular\src\backend\TSIC.API" -ArgumentList "dotnet run; Pause"

# Launch Angular UI
Start-Process powershell -WorkingDirectory "$PSScriptRoot\..\TSIC-Core-Angular\src\frontend\tsic-app" -ArgumentList "ng serve; Pause"
