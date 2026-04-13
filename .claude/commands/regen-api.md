Run `.\scripts\2-Regenerate-API-Models.ps1` to regenerate the frontend TypeScript API models from the current backend OpenAPI spec.

Steps:
1. Run the script via Bash: `powershell.exe -ExecutionPolicy Bypass -File ./scripts/2-Regenerate-API-Models.ps1`
2. If the script fails (API server not running, lock contention, etc.) — STOP and tell the user. Never work around with inline TypeScript types.
3. On success, briefly confirm which DTOs changed (run `git diff --stat src/app/core/api/models/`).
4. Do NOT proceed to write frontend code that uses new DTOs until this completes successfully.
