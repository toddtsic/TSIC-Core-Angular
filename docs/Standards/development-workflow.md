# Development Workflow & Maintenance Guide

## Routine Commits

For basic code changes, use simple commits without pre-commit checks:

```bash
git add .
git commit --no-verify -m "Your commit message"
git push
```

## What Happens During Commits

### Local Pre-commit Hooks (`.githooks/pre-commit`)
- **dotnet format --verify-no-changes**: Checks code formatting and fails if changes are needed
- **Frontend linting**: Runs `npm run lint` if Angular app exists (currently skipped)

### Remote CI Workflow (`.github/workflows/ci.yml`)
Triggers on push to master/main:
1. **Setup .NET 9.0.x**
2. **Restore dependencies**: `dotnet restore`
3. **Build**: `dotnet build --no-restore --warnaserror:false`
4. **Test**: `dotnet test --no-build --verbosity normal`

## Periodic Maintenance Tasks

### Weekly/Monthly Tasks
- **Format code**: `dotnet format TSIC-Core-Angular.sln`
- **Run tests locally**: `dotnet test`
- **Update dependencies**: Check for NuGet package updates
- **Clean up**: `dotnet clean` and remove bin/obj folders

### Before Major Commits
- **Full build**: `dotnet build`
- **Run all tests**: `dotnet test --verbosity normal`
- **Check formatting**: `dotnet format --verify-no-changes TSIC-Core-Angular.sln`

## Troubleshooting CI Failures

### Common Issues
1. **Test failures**: Run `dotnet test` locally to debug
2. **Build failures**: Check `dotnet build` output
3. **Dependency issues**: Run `dotnet restore`

### Disabling Checks Temporarily
- **Skip pre-commit**: `git commit --no-verify`
- **Skip CI**: Add `[skip ci]` to commit message

## Hook Management

### Install Hooks
```powershell
.\scripts\install-hooks.ps1
```

### Bypass Hooks
```bash
git commit --no-verify -m "message"
```

### Remove Hooks
```bash
git config --unset core.hooksPath
```

## CI Workflow Status

The CI currently runs build and tests on every push. If tests are failing, check:
- Test project configuration
- Database dependencies
- Missing test data/setup

## Quick Reference

| Task | Command | When to Use |
|------|---------|-------------|
| Quick commit | `git commit --no-verify` | Routine changes |
| Format code | `dotnet format TSIC-Core-Angular.sln` | Before commits |
| Full check | `dotnet build && dotnet test` | Before pushing |
| Skip CI | `[skip ci]` in message | Documentation changes |