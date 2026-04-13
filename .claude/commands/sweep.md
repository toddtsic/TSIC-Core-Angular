Sweep the entire codebase for every instance of the bug or anti-pattern just identified in this conversation.

Procedure:
1. **Name the pattern.** State in one sentence what you're looking for (e.g., "BehaviorSubject used for component state instead of signal()").
2. **Pick the search.** Choose a grep pattern that finds the pattern with minimum false positives. State it.
3. **Search.** Use Grep across the whole repo (no path filter unless the pattern is layer-specific).
4. **Report every hit.** List file:line for each occurrence — do NOT summarize as "X instances" without naming them.
5. **Categorize each.** Mark each as: TRUE POSITIVE (real instance of the bug) / FALSE POSITIVE (matches pattern but isn't the bug) / AMBIGUOUS (need user judgment).
6. **Ask before fixing.** Show the user the categorized list. Wait for approval before fixing any. Do not assume "sweep" means "fix."

Why: the user has stated repeatedly that fixing one instance of a bug class while leaving others is unacceptable. This command enforces the full sweep.

If the user passed an argument with `/sweep`, treat it as the pattern to search for instead of inferring from conversation context.
