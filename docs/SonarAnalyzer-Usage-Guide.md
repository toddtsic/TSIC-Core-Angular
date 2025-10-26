# SonarAnalyzer.CSharp Usage Guide

## Overview

SonarAnalyzer.CSharp is a static code analysis tool that helps maintain code quality, catch bugs, and identify security vulnerabilities in your .NET projects. This guide covers how to effectively use SonarAnalyzer in your clean architecture project.

## Current Configuration

SonarAnalyzer.CSharp v9.32.0.97167 is configured in your project with the following setup:

- **Package Management**: Added to `Directory.Packages.props` for centralized version control
- **Project Integration**: Configured in all backend projects (API, Application, Domain, Infrastructure)
- **CI/CD Integration**: GitHub Actions workflow allows warnings without failing builds
- **IDE Integration**: Works with VS Code SonarLint extension for real-time analysis

## Understanding SonarAnalyzer Warnings

### Common Warning Types

From your project analysis, you may encounter these types of warnings:

#### Code Quality Issues
- **S2094**: Empty classes that should be removed or converted to interfaces
- **S101**: Incorrect PascalCase naming conventions
- **S3251**: Missing implementations for partial methods

#### Security Issues
- **S6781**: JWT secret keys should not be disclosed (HIGH PRIORITY)
- **S1075**: Hardcoded absolute paths or URIs

#### Testing Issues
- **S2699**: Missing assertions in test cases

### Warning Format
```
File.cs(line,column): warning SXXXX: Description (https://rules.sonarsource.com/csharp/RSPEC-XXXX)
```

## Viewing Warnings

### Method 1: Build Output
```bash
dotnet build --verbosity normal
```
Shows all warnings in the terminal output.

### Method 2: VS Code Problems Panel
- Press `Ctrl+Shift+M` to open Problems panel
- Filter by "Sonar" to see only SonarAnalyzer issues
- Click on warnings to navigate to the problematic code

### Method 3: SonarLint Extension
- Real-time analysis as you type
- Inline warnings in the editor
- Quick fixes for some issues

## Fixing Common Issues

### Empty Classes (S2094)
```csharp
// ❌ Bad - Empty class
public class EmptyClass { }

// ✅ Good - Remove if unused
// Or convert to interface if needed
public interface IService
{
    // Define methods here
}
```

### Hardcoded Paths (S1075)
```csharp
// ❌ Bad - Hardcoded path
private const string DatabasePath = @"C:\hardcoded\path\to\db";

// ✅ Good - Use configuration
private readonly string _databasePath;

public MyClass(IConfiguration configuration)
{
    _databasePath = configuration["Database:Path"];
}
```

### Naming Conventions (S101)
```csharp
// ❌ Bad - Incorrect casing
public class ynschedule { }
public class APIController { }

// ✅ Good - PascalCase
public class YnSchedule { }
public class ApiController { }
```

### JWT Security (S6781)
```csharp
// ❌ Bad - Exposed secret
private const string JwtSecret = "my-secret-key";

// ✅ Good - Use environment variables or secure config
private readonly string _jwtSecret;

public AuthService(IConfiguration config)
{
    _jwtSecret = config["JWT:Secret"];
}
```

## Configuration Options

### Disable Specific Rules

Add to your `.csproj` files:
```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);S2094;S101;S1075</NoWarn>
</PropertyGroup>
```

### EditorConfig Configuration

Create `.editorconfig` in your repository root:
```ini
[*.cs]
dotnet_diagnostic.S2094.severity = none  # Empty classes
dotnet_diagnostic.S1075.severity = none  # Hardcoded paths
dotnet_diagnostic.S101.severity = none   # Naming conventions
```

### Project-Specific Rules

For domain-specific exceptions, add to specific project files:
```xml
<!-- In TSIC.Domain.csproj - Allow some flexibility -->
<PropertyGroup>
  <NoWarn>$(NoWarn);S2094</NoWarn>
</PropertyGroup>

<!-- In TSIC.API.csproj - Strict security rules -->
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);S6781</WarningsAsErrors>
</PropertyGroup>
```

## Best Practices

### Priority Order for Fixes
1. **Security Issues** - Fix immediately (S6781, authentication vulnerabilities)
2. **Bugs** - Logic errors, potential runtime issues
3. **Maintainability** - Code complexity, naming, structure
4. **Code Smells** - Duplication, unused code, formatting

### Development Workflow
1. **Regular Builds**: Run `dotnet build` frequently during development
2. **Review Warnings**: Check Problems panel regularly
3. **Incremental Fixes**: Address critical issues first
4. **Team Standards**: Establish which rules to enforce vs. suppress

### CI/CD Integration
Your GitHub Actions workflow:
- ✅ Runs analysis on every push and pull request
- ✅ Allows warnings without failing builds (`--warnaserror:false`)
- ✅ Provides feedback without blocking development
- ✅ Maintains code quality standards

## Advanced Usage

### Targeted Analysis
```bash
# Analyze specific project
dotnet build TSIC.API/TSIC.API.csproj --verbosity normal

# Exclude test files from analysis
dotnet build /p:SonarQubeExclude="**/Test*.cs;**/Tests/**/*.cs"
```

### SonarQube Server Integration
For advanced reporting and dashboards:
```bash
# Install SonarScanner
dotnet tool install --global dotnet-sonarscanner

# Run analysis with SonarQube server
dotnet sonarscanner begin /k:"your-project-key" /d:sonar.host.url="https://your-sonarqube-server"
dotnet build
dotnet sonarscanner end
```

### Custom Rules
Create custom analyzers for project-specific rules:
```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CustomAnalyzer : DiagnosticAnalyzer
{
    // Implementation here
}
```

## Troubleshooting

### Warnings Not Showing
- Ensure SonarAnalyzer.CSharp package is installed
- Check build verbosity: `dotnet build --verbosity normal`
- Verify VS Code SonarLint extension is enabled

### Too Many Warnings
- Use `.editorconfig` to suppress non-critical rules
- Focus on high-impact issues first
- Consider phased approach to code quality improvement

### CI/CD Issues
- Your workflow uses `--warnaserror:false` to prevent failures
- Monitor build logs for new critical issues
- Adjust rules based on team preferences

## Integration with Clean Architecture

### Layer-Specific Considerations

**Domain Layer**:
- Strict naming conventions
- Minimal external dependencies
- Focus on business rules validation

**Application Layer**:
- Service implementation quality
- Validation logic correctness
- CQRS pattern adherence

**Infrastructure Layer**:
- Data access patterns
- External service integrations
- Configuration management

**API Layer**:
- Security implementations
- Input validation
- Error handling

## Next Steps

1. **Review Current Warnings**: Check VS Code Problems panel
2. **Prioritize Security**: Fix JWT and authentication issues first
3. **Establish Standards**: Decide which rules to enforce strictly
4. **Team Training**: Share this guide with your development team
5. **Continuous Improvement**: Gradually reduce warning count over time

## Resources

- [SonarQube Rules Documentation](https://rules.sonarsource.com/csharp)
- [SonarAnalyzer.CSharp GitHub](https://github.com/SonarSource/sonar-dotnet)
- [Clean Architecture Guidelines](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
- [Microsoft Code Analysis](https://docs.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)

---

*This guide was created for the TSIC-Core-Angular project to help maintain high code quality standards using SonarAnalyzer.CSharp in a clean architecture context.*