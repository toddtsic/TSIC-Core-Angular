# ============================================
# TSIC-Net-Angular Project Setup Script
# ============================================
# This script creates the complete solution structure for TSIC-Net-Angular
# with .NET 10 backend and Angular 18 frontend

param(
    [string]$SolutionName = "TSIC-Net-Angular",
    [string]$RootPath = "$PSScriptRoot\..",
    [switch]$SkipAngular
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  TSIC-Net-Angular Setup" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Create root directory
$projectRoot = Join-Path $RootPath $SolutionName
if (Test-Path $projectRoot) {
    Write-Host "Directory '$projectRoot' already exists." -ForegroundColor Yellow
    $response = Read-Host "Do you want to delete it and start fresh? (y/n)"
    if ($response -eq 'y') {
        Remove-Item -Path $projectRoot -Recurse -Force
        Write-Host "Deleted existing directory." -ForegroundColor Green
    } else {
        Write-Host "Exiting setup." -ForegroundColor Red
        exit
    }
}

New-Item -ItemType Directory -Path $projectRoot | Out-Null
Set-Location $projectRoot

Write-Host "Created project root: $projectRoot" -ForegroundColor Green
Write-Host ""

# ============================================
# CREATE SOLUTION
# ============================================

Write-Host "Creating solution..." -ForegroundColor Yellow
dotnet new sln -n $SolutionName
Write-Host "✓ Solution created" -ForegroundColor Green
Write-Host ""

# ============================================
# CREATE BACKEND PROJECTS
# ============================================

Write-Host "Creating backend projects..." -ForegroundColor Yellow

# Domain
Write-Host "  - Creating TSIC.Domain..." -ForegroundColor Gray
dotnet new classlib -n TSIC.Domain -o src/backend/TSIC.Domain -f net10.0
dotnet sln add src/backend/TSIC.Domain

# Application
Write-Host "  - Creating TSIC.Application..." -ForegroundColor Gray
dotnet new classlib -n TSIC.Application -o src/backend/TSIC.Application -f net10.0
dotnet sln add src/backend/TSIC.Application

# Infrastructure
Write-Host "  - Creating TSIC.Infrastructure..." -ForegroundColor Gray
dotnet new classlib -n TSIC.Infrastructure -o src/backend/TSIC.Infrastructure -f net10.0
dotnet sln add src/backend/TSIC.Infrastructure

# API
Write-Host "  - Creating TSIC.API..." -ForegroundColor Gray
dotnet new webapi -n TSIC.API -o src/backend/TSIC.API -f net10.0
dotnet sln add src/backend/TSIC.API

# Tests
Write-Host "  - Creating TSIC.Tests..." -ForegroundColor Gray
dotnet new xunit -n TSIC.Tests -o src/backend/TSIC.Tests -f net10.0
dotnet sln add src/backend/TSIC.Tests

Write-Host "✓ Backend projects created" -ForegroundColor Green
Write-Host ""

# ============================================
# CREATE SERVICE LAYER PROJECTS
# ============================================

Write-Host "Creating service layer projects..." -ForegroundColor Yellow

# API Client
Write-Host "  - Creating TSIC.ApiClient..." -ForegroundColor Gray
dotnet new classlib -n TSIC.ApiClient -o src/services/TSIC.ApiClient -f net10.0
dotnet sln add src/services/TSIC.ApiClient

# API Client Tests
Write-Host "  - Creating TSIC.ApiClient.Tests..." -ForegroundColor Gray
dotnet new xunit -n TSIC.ApiClient.Tests -o src/services/TSIC.ApiClient.Tests -f net10.0
dotnet sln add src/services/TSIC.ApiClient.Tests

Write-Host "✓ Service layer projects created" -ForegroundColor Green
Write-Host ""

# ============================================
# SETUP PROJECT REFERENCES
# ============================================

Write-Host "Setting up project references..." -ForegroundColor Yellow

dotnet add src/backend/TSIC.Application reference src/backend/TSIC.Domain
dotnet add src/backend/TSIC.Infrastructure reference src/backend/TSIC.Application
dotnet add src/backend/TSIC.Infrastructure reference src/backend/TSIC.Domain
dotnet add src/backend/TSIC.API reference src/backend/TSIC.Application
dotnet add src/backend/TSIC.API reference src/backend/TSIC.Infrastructure
dotnet add src/backend/TSIC.Tests reference src/backend/TSIC.API
dotnet add src/backend/TSIC.Tests reference src/backend/TSIC.Application
dotnet add src/services/TSIC.ApiClient.Tests reference src/services/TSIC.ApiClient

Write-Host "✓ Project references configured" -ForegroundColor Green
Write-Host ""

# ============================================
# INSTALL NUGET PACKAGES
# ============================================

Write-Host "Installing NuGet packages..." -ForegroundColor Yellow

# API Project
Write-Host "  - TSIC.API packages..." -ForegroundColor Gray
dotnet add src/backend/TSIC.API package Microsoft.EntityFrameworkCore.Design
dotnet add src/backend/TSIC.API package Microsoft.EntityFrameworkCore.SqlServer
dotnet add src/backend/TSIC.API package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/backend/TSIC.API package Swashbuckle.AspNetCore

# Application Project - FIXED: Removed AutoMapper here, will add via Infrastructure's dependency
Write-Host "  - TSIC.Application packages..." -ForegroundColor Gray
dotnet add src/backend/TSIC.Application package FluentValidation
dotnet add src/backend/TSIC.Application package FluentValidation.DependencyInjectionExtensions

# Infrastructure Project - FIXED: AutoMapper.Extensions brings in AutoMapper as dependency
Write-Host "  - TSIC.Infrastructure packages..." -ForegroundColor Gray
dotnet add src/backend/TSIC.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer
dotnet add src/backend/TSIC.Infrastructure package Microsoft.EntityFrameworkCore.Tools
dotnet add src/backend/TSIC.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add src/backend/TSIC.Infrastructure package AutoMapper.Extensions.Microsoft.DependencyInjection

# API Client Project
Write-Host "  - TSIC.ApiClient packages..." -ForegroundColor Gray
dotnet add src/services/TSIC.ApiClient package Microsoft.Extensions.Http
dotnet add src/services/TSIC.ApiClient package System.Net.Http.Json

# Test Projects
Write-Host "  - Test project packages..." -ForegroundColor Gray
dotnet add src/backend/TSIC.Tests package Moq
dotnet add src/backend/TSIC.Tests package FluentAssertions
dotnet add src/backend/TSIC.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add src/backend/TSIC.Tests package coverlet.collector

dotnet add src/services/TSIC.ApiClient.Tests package Moq
dotnet add src/services/TSIC.ApiClient.Tests package FluentAssertions
dotnet add src/services/TSIC.ApiClient.Tests package RichardSzalay.MockHttp

Write-Host "✓ NuGet packages installed" -ForegroundColor Green
Write-Host ""

# ============================================
# CREATE DIRECTORY STRUCTURE
# ============================================

Write-Host "Creating directory structure..." -ForegroundColor Yellow

# Domain directories
New-Item -ItemType Directory -Path "src/backend/TSIC.Domain/Entities" -Force | Out-Null
New-Item -ItemType Directory -Path "src/backend/TSIC.Domain/Interfaces" -Force | Out-Null
New-Item -ItemType Directory -Path "src/backend/TSIC.Domain/Enums" -Force | Out-Null

# Application directories
New-Item -ItemType Directory -Path "src/backend/TSIC.Application/Services" -Force | Out-Null
New-Item -ItemType Directory -Path "src/backend/TSIC.Application/DTOs" -Force | Out-Null
New-Item -ItemType Directory -Path "src/backend/TSIC.Application/Validators" -Force | Out-Null
New-Item -ItemType Directory -Path "src/backend/TSIC.Application/Mappings" -Force | Out-Null

# Infrastructure directories
New-Item -ItemType Directory -Path "src/backend/TSIC.Infrastructure/Data/Repositories" -Force | Out-Null
New-Item -ItemType Directory -Path "src/backend/TSIC.Infrastructure/Data/SqlDbContext/Context" -Force | Out-Null
New-Item -ItemType Directory -Path "src/backend/TSIC.Infrastructure/Data/SqlDbContext/Entities" -Force | Out-Null

# API Controllers directory
New-Item -ItemType Directory -Path "src/backend/TSIC.API/Controllers" -Force | Out-Null

Write-Host "✓ Directory structure created" -ForegroundColor Green
Write-Host ""

# ============================================
# CREATE TEST CONTROLLER
# ============================================

Write-Host "Creating test controller..." -ForegroundColor Yellow

$testController = @"
using Microsoft.AspNetCore.Mvc;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly ILogger<TestController> _logger;

    public TestController(ILogger<TestController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<string> Get()
    {
        _logger.LogInformation("Test endpoint called successfully");
        return Ok("api returning successfully from test");
    }

    [HttpGet("health")]
    public ActionResult<object> GetHealth()
    {
        return Ok(new
        {
            status = "healthy",
            message = "TSIC API is running",
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
        });
    }
}
"@

Set-Content -Path "src/backend/TSIC.API/Controllers/TestController.cs" -Value $testController
Write-Host "✓ Test controller created" -ForegroundColor Green
Write-Host ""

# ============================================
# UPDATE PROGRAM.CS
# ============================================

Write-Host "Updating Program.cs..." -ForegroundColor Yellow

$programCs = @"
var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "TSIC API", Version = "v1" });
});

// CORS for Angular
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TSIC API V1");
    });
}

app.UseHttpsRedirection();
app.UseCors("AllowAngularApp");
app.UseAuthorization();
app.MapControllers();

app.Run();
"@

Set-Content -Path "src/backend/TSIC.API/Program.cs" -Value $programCs
Write-Host "✓ Program.cs updated" -ForegroundColor Green
Write-Host ""

# ============================================
# CREATE APPSETTINGS
# ============================================

Write-Host "Creating appsettings.json..." -ForegroundColor Yellow

$appsettings = @"
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.\\SS2016;Database=TSICV5;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
"@

Set-Content -Path "src/backend/TSIC.API/appsettings.json" -Value $appsettings

$appsettingsDev = @"
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.\\SS2016;Database=TSICV5;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
"@

Set-Content -Path "src/backend/TSIC.API/appsettings.Development.json" -Value $appsettingsDev
Write-Host "✓ appsettings files created" -ForegroundColor Green
Write-Host ""

# ============================================
# BUILD SOLUTION
# ============================================

Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build --configuration Debug
if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Solution built successfully" -ForegroundColor Green
} else {
    Write-Host "✗ Build failed" -ForegroundColor Red
    exit 1
}
Write-Host ""

# ============================================
# CREATE ANGULAR APP
# ============================================

if (-not $SkipAngular) {
    Write-Host "Creating Angular application..." -ForegroundColor Yellow
    Write-Host "This may take a few minutes..." -ForegroundColor Gray
    Write-Host ""
    
    # Check if Angular CLI is installed
    try {
        $ngVersion = ng version 2>&1
        Write-Host "Angular CLI found" -ForegroundColor Green
    } catch {
        Write-Host "Angular CLI not found. Installing globally..." -ForegroundColor Yellow
        npm install -g @angular/cli@latest
    }
    
    New-Item -ItemType Directory -Path "src/frontend" -Force | Out-Null
    Set-Location "src/frontend"
    
    # Create Angular app
    ng new tsic-app --style=scss --routing=true --ssr=true --standalone=true --skip-git=true
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Angular app created" -ForegroundColor Green
        
        Set-Location "tsic-app"
        
        # Create environments directory
        New-Item -ItemType Directory -Path "src/environments" -Force | Out-Null
        
        # Create environment files
        $envDev = @"
export const environment = {
  production: false,
  apiUrl: 'https://localhost:7001'
};
"@
        Set-Content -Path "src/environments/environment.development.ts" -Value $envDev
        
        $env = @"
export const environment = {
  production: true,
  apiUrl: 'https://your-production-api-url.com'
};
"@
        Set-Content -Path "src/environments/environment.ts" -Value $env
        
        # Create core services directory
        New-Item -ItemType Directory -Path "src/app/core/services" -Force | Out-Null
        
        # Create API Service
        $apiService = @"
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class ApiService {
  private http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  testConnection(): Observable<string> {
    return this.http.get(``${this.baseUrl}/api/test``, { responseType: 'text' });
  }

  getHealth(): Observable<any> {
    return this.http.get(``${this.baseUrl}/api/test/health``);
  }
}
"@
        Set-Content -Path "src/app/core/services/api.service.ts" -Value $apiService
        
        # Update app.config.ts
        $appConfig = @"
import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { provideClientHydration } from '@angular/platform-browser';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(withInterceptorsFromDi()),
    provideClientHydration()
  ]
};
"@
        Set-Content -Path "src/app/app.config.ts" -Value $appConfig
        
        # Update app.component.ts
        $appComponent = @"
import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { ApiService } from './core/services/api.service';

@Component({
  selector: 'tsic-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent implements OnInit {
  private apiService = inject(ApiService);
  
  loading = signal(false);
  apiResponse = signal<string | null>(null);
  error = signal<string | null>(null);
  healthData = signal<any>(null);

  ngOnInit() {
    this.testApi();
  }

  testApi() {
    this.loading.set(true);
    this.error.set(null);
    this.apiResponse.set(null);
    this.healthData.set(null);

    this.apiService.testConnection().subscribe({
      next: (response) => {
        this.apiResponse.set(response);
        this.loading.set(false);
        
        this.apiService.getHealth().subscribe({
          next: (health) => this.healthData.set(health),
          error: (err) => console.error('Health check failed:', err)
        });
      },
      error: (err) => {
        this.error.set(``Failed to connect to API: ${err.message}``);
        this.loading.set(false);
        console.error('API Error:', err);
      }
    });
  }
}
"@
        Set-Content -Path "src/app/app.component.ts" -Value $appComponent
        
        # Update app.component.html
        $appHtml = @"
<div class="app-container">
  <header>
    <h1>TSIC Next Generation</h1>
  </header>

  <main>
    <div class="test-section">
      <h2>API Connection Test</h2>
      
      @if (loading()) {
        <div class="status loading">
          <span class="spinner"></span>
          Testing API connection...
        </div>
      }
      
      @if (apiResponse()) {
        <div class="status success">
          <strong>✓ Success!</strong>
          <p>{{ apiResponse() }}</p>
        </div>
      }
      
      @if (error()) {
        <div class="status error">
          <strong>✗ Error</strong>
          <p>{{ error() }}</p>
          <button (click)="testApi()">Retry</button>
        </div>
      }
      
      @if (healthData()) {
        <div class="health-info">
          <h3>API Health</h3>
          <pre>{{ healthData() | json }}</pre>
        </div>
      }
    </div>

    <router-outlet />
  </main>
</div>
"@
        Set-Content -Path "src/app/app.component.html" -Value $appHtml
        
        # Update app.component.scss
        $appScss = @"
.app-container {
  max-width: 1200px;
  margin: 0 auto;
  padding: 2rem;
  font-family: system-ui, -apple-system, sans-serif;
}

header {
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  padding: 2rem;
  border-radius: 8px;
  margin-bottom: 2rem;
  text-align: center;
}

h1 {
  margin: 0;
  font-size: 2.5rem;
}

.test-section {
  background: white;
  border-radius: 8px;
  padding: 2rem;
  box-shadow: 0 2px 8px rgba(0,0,0,0.1);
  margin-bottom: 2rem;
}

h2 {
  margin-top: 0;
  color: #333;
}

.status {
  padding: 1rem;
  border-radius: 4px;
  margin: 1rem 0;
}

.status.loading {
  background: #e3f2fd;
  color: #1976d2;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.status.success {
  background: #e8f5e9;
  color: #2e7d32;
  border-left: 4px solid #4caf50;
}

.status.error {
  background: #ffebee;
  color: #c62828;
  border-left: 4px solid #f44336;
}

.status strong {
  display: block;
  margin-bottom: 0.5rem;
  font-size: 1.1rem;
}

.status p {
  margin: 0.5rem 0;
}

.spinner {
  display: inline-block;
  width: 16px;
  height: 16px;
  border: 2px solid #1976d2;
  border-top-color: transparent;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

button {
  margin-top: 1rem;
  padding: 0.5rem 1rem;
  background: #667eea;
  color: white;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 1rem;
}

button:hover {
  background: #5568d3;
}

.health-info {
  margin-top: 1.5rem;
  padding: 1rem;
  background: #f5f5f5;
  border-radius: 4px;
}

.health-info h3 {
  margin-top: 0;
  color: #555;
}

pre {
  background: white;
  padding: 1rem;
  border-radius: 4px;
  overflow-x: auto;
  border: 1px solid #ddd;
}
"@
        Set-Content -Path "src/app/app.component.scss" -Value $appScss
        
        Write-Host "✓ Angular files configured" -ForegroundColor Green
        
        Set-Location "../../.."
    } else {
        Write-Host "✗ Angular app creation failed" -ForegroundColor Red
    }
    Write-Host ""
}

# ============================================
# CREATE README
# ============================================

Write-Host "Creating README..." -ForegroundColor Yellow

$readme = @"
# TSIC-Net-Angular

Next generation version of TSIC-Unisify built with .NET 9 and Angular 18.

## Project Structure

\`\`\`
TSIC-Net-Angular/
├── src/
│   ├── backend/
│   │   ├── TSIC.API/              # .NET 9 Web API
│   │   ├── TSIC.Application/      # Business logic
│   │   ├── TSIC.Domain/           # Domain entities & interfaces
│   │   ├── TSIC.Infrastructure/   # EF Core, repositories
│   │   └── TSIC.Tests/            # Unit & integration tests
│   ├── services/
│   │   ├── TSIC.ApiClient/        # Typed HTTP client library
│   │   └── TSIC.ApiClient.Tests/  # Service layer tests
│   └── frontend/
│       └── tsic-app/              # Angular 18 application
└── TSIC-Net-Angular.sln
\`\`\`

## Getting Started

### Prerequisites

- .NET 9 SDK
- Node.js 18+ and npm
- SQL Server (using existing TSICV5 database)
- Angular CLI (\``npm install -g @angular/cli\``)

### Running the Backend

\`\`\`bash
cd src/backend/TSIC.API
dotnet run
\`\`\`

The API will be available at \``https://localhost:7001\`` (or check console output for the exact port).

### Running the Frontend

\`\`\`bash
cd src/frontend/tsic-app
ng serve
\`\`\`

The app will be available at \``http://localhost:4200\``.

### Testing the Connection

1. Start the backend API
2. Start the Angular frontend
3. Navigate to \``http://localhost:4200\``
4. You should see a success message: "api returning successfully from test"

## Database

This project uses the existing TSICV5 database in **READ-ONLY** mode. No migrations will be applied.

### Scaffolding Database Entities

To generate entity models from the existing database:

\`\`\`bash
cd src/backend/TSIC.Infrastructure
dotnet ef dbcontext scaffold "Server=.\\SS2016;Database=TSICV5;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True" Microsoft.EntityFrameworkCore.SqlServer --output-dir Data/SqlDbContext/Entities --context SqlDbContext --context-dir Data/SqlDbContext/Context --context-namespace TSIC.Infrastructure.Data.SqlDbContext --namespace TSIC.Infrastructure.Data.SqlDbContext --no-pluralize --force --no-build
\`\`\`

## Architecture

This project follows **Clean Architecture** principles:

- **Domain**: Core business entities and interfaces
- **Application**: Business logic, DTOs, services
- **Infrastructure**: Data access, external services
- **API**: REST endpoints, controllers

## Technology Stack

- **Backend**: .NET 9, EF Core 9, AutoMapper, FluentValidation
- **Frontend**: Angular 18, TypeScript, SCSS, RxJS, Signals
- **Database**: SQL Server 2016+
- **Testing**: xUnit, Moq, FluentAssertions

## Next Steps

1. Scaffold database entities
2. Create repositories and services
3. Build Angular components and features
4. Add authentication/authorization

## License

Copyright © TSIC
"@

Set-Content -Path "README.md" -Value $readme
Write-Host "✓ README created" -ForegroundColor Green
Write-Host ""

# ============================================
# CREATE .GITIGNORE
# ============================================

Write-Host "Creating .gitignore..." -ForegroundColor Yellow

$gitignore = @"
## Ignore Visual Studio temporary files, build results, and
## files generated by popular Visual Studio add-ons.

# User-specific files
*.suo
*.user
*.userosscache
*.sln.docstates

# Build results
[Dd]ebug/
[Dd]ebugPublic/
[Rr]elease/
[Rr]eleases/
x64/
x86/
[Aa][Rr][Mm]/
[Aa][Rr][Mm]64/
bld/
[Bb]in/
[Oo]bj/
[Ll]og/

# Visual Studio cache/options directory
.vs/

# JetBrains Rider
.idea/
*.sln.iml

# Visual Studio Code
.vscode/

# Angular
src/frontend/tsic-app/node_modules/
src/frontend/tsic-app/dist/
src/frontend/tsic-app/.angular/
src/frontend/tsic-app/npm-debug.log*
src/frontend/tsic-app/yarn-error.log*

# Environment files (keep templates)
**/appsettings.*.json
!**/appsettings.json
!**/appsettings.Development.json

# OS files
.DS_Store
Thumbs.db
"@

Set-Content -Path ".gitignore" -Value $gitignore
Write-Host "✓ .gitignore created" -ForegroundColor Green
Write-Host ""

# ============================================
# SUMMARY
# ============================================

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Setup Complete!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Project created at: $projectRoot" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Start the API:" -ForegroundColor White
Write-Host "     cd src/backend/TSIC.API" -ForegroundColor Gray
Write-Host "     dotnet run" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. Start Angular (in a new terminal):" -ForegroundColor White
Write-Host "     cd src/frontend/tsic-app" -ForegroundColor Gray
Write-Host "     ng serve" -ForegroundColor Gray
Write-Host ""
Write-Host "  3. Open browser:" -ForegroundColor White
Write-Host "     http://localhost:4200" -ForegroundColor Gray
Write-Host ""
Write-Host "  4. Scaffold database entities:" -ForegroundColor White
Write-Host "     cd src/backend/TSIC.Infrastructure" -ForegroundColor Gray
Write-Host "     dotnet ef dbcontext scaffold ..." -ForegroundColor Gray
Write-Host ""
Write-Host "Happy coding! 🚀" -ForegroundColor Cyan
Write-Host ""