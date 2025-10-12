# TSIC-Net-Angular

Next generation version of TSIC-Unisify built with .NET 9 and Angular 18.

## Project Structure

\\\
TSIC-Net-Angular/
+-- src/
¦   +-- backend/
¦   ¦   +-- TSIC.API/              # .NET 9 Web API
¦   ¦   +-- TSIC.Application/      # Business logic
¦   ¦   +-- TSIC.Domain/           # Domain entities & interfaces
¦   ¦   +-- TSIC.Infrastructure/   # EF Core, repositories
¦   ¦   +-- TSIC.Tests/            # Unit & integration tests
¦   +-- services/
¦   ¦   +-- TSIC.ApiClient/        # Typed HTTP client library
¦   ¦   +-- TSIC.ApiClient.Tests/  # Service layer tests
¦   +-- frontend/
¦       +-- tsic-app/              # Angular 18 application
+-- TSIC-Net-Angular.sln
\\\

## Getting Started

### Prerequisites

- .NET 9 SDK
- Node.js 18+ and npm
- SQL Server (using existing TSICV5 database)
- Angular CLI (\`npm install -g @angular/cli\`)

### Running the Backend

\\\ash
cd src/backend/TSIC.API
dotnet run
\\\

The API will be available at \`https://localhost:7001\` (or check console output for the exact port).

### Running the Frontend

\\\ash
cd src/frontend/tsic-app
ng serve
\\\

The app will be available at \`http://localhost:4200\`.

### Testing the Connection

1. Start the backend API
2. Start the Angular frontend
3. Navigate to \`http://localhost:4200\`
4. You should see a success message: "api returning successfully from test"

## Database

This project uses the existing TSICV5 database in **READ-ONLY** mode. No migrations will be applied.

### Scaffolding Database Entities

To generate entity models from the existing database:

\\\ash
cd src/backend/TSIC.Infrastructure
dotnet ef dbcontext scaffold "Server=.\\SS2016;Database=TSICV5;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True" Microsoft.EntityFrameworkCore.SqlServer --output-dir Data/SqlDbContext/Entities --context SqlDbContext --context-dir Data/SqlDbContext/Context --context-namespace TSIC.Infrastructure.Data.SqlDbContext --namespace TSIC.Infrastructure.Data.SqlDbContext --no-pluralize --force --no-build
\\\

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
