# Go-Live Investigations

Risk-finding and hardening work in the final stretch to the **August 2026 go-live**.

Each numbered file is one investigation into a specific shape of catastrophic-class risk. The bar for inclusion is: **a failure here could be business-ending** (money moves wrong, wrong person sees wrong data, silent data corruption, auth bypass, mass communication failure).

## Investigations

- [001 — Environment / config drift](001-env-config-drift.md) — prod referenced dev endpoints once; could the reverse happen and hit real money / real customers from a dev or staging deploy? **(closed 2026-05-19)**
- [002 — Money moves wrong](002-money-moves-wrong.md) — does the system ever charge, refund, recurring-bill, or record the wrong amount, twice, or to the wrong merchant? **(open)**
