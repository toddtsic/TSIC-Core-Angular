# Contributing

Thanks for helping improve this project. A few guidelines to make contributions smooth:

- Fork the repository and open a pull request against `master`.
- Ensure `dotnet build` and the frontend build succeed locally.
- Run `dotnet format` and `npm run lint` (if applicable) before committing.
- Keep changes small and focused; write tests for new logic where possible.

Code of conduct: please be respectful and constructive.

Hooks
- We include an example git hook in `.githooks/pre-commit`.

Install hooks (recommended)
- Windows (PowerShell):

```powershell
.
\scripts\install-hooks.ps1
```

- macOS / Linux / Git Bash:

```bash
./scripts/install-hooks.sh
```

Alternatively you can enable hooks manually with:

```powershell
git config core.hooksPath .githooks
```

The pre-commit hook will run `dotnet format --verify-no-changes` and, if a frontend lint script exists, `npm run lint` for the Angular app. CI also enforces formatting and tests.

Releases
- To create a release that triggers CI checks and a GitHub Release, tag the commit with a `v`-prefixed semver and push the tag. Example:

```powershell
git tag -a v1.2.3 -m "Release v1.2.3"
git push origin v1.2.3
```

The release workflow will run build and tests on the pushed tag and create a GitHub Release. (This repository does not require Docker or Aspire for local development.)

Docker & Aspire policy
- This project supports containerized workflows (Docker/Aspire) for CI/integration, but local development does NOT require Docker. If you prefer the host-based workflow (IIS + local SQL), continue using that for speed. To remove local containers and images if you experimented with Docker:

```powershell
# stop and remove compose-created containers and volumes
docker compose down -v
# remove the SQL Server image if pulled
docker image rm -f mcr.microsoft.com/mssql/server:2019-latest
```

If you later want to restore the Docker files or Aspire scaffolding, they can be recovered from git history or the `pre-rollback-backup` branch.
