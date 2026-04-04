# Ann's Punchlist Workflows

When Ann uses any of the trigger phrases below, execute the corresponding workflow.

## Punchlist Locations

Each functional area has its own punchlist under `scripts/Ann-Backend-Testing/`:

```
scripts/Ann-Backend-Testing/
  Accounting/Punchlist/punchlist.md
  PlayerRegistration/Punchlist/punchlist.md
  SearchRegistrations/Punchlist/punchlist.md
  SearchTeams/Punchlist/punchlist.md
  TeamRegistration/Punchlist/punchlist.md
  TSIC-Events/Punchlist/punchlist.md
  TSIC-Teams/Punchlist/punchlist.md
```

**Routing:** Determine which punchlist to use from context -- what Ann is currently testing. If unclear, ask: "Which area are you testing right now?" Use short names she'll recognize: Player Registration, Team Registration, Accounting, Search, Events, Teams.

---

## Trigger: "Switch to [area]" or "I'm testing [area] now"

1. Set the active functional area for the rest of the conversation
2. Map casual names to folder names:
   - "Player Registration" / "Player Reg" → PlayerRegistration
   - "Team Registration" / "Team Reg" → TeamRegistration
   - "Accounting" → Accounting
   - "Search Registrations" / "Search Regs" → SearchRegistrations
   - "Search Teams" → SearchTeams
   - "Events" → TSIC-Events
   - "Teams" → TSIC-Teams
3. Confirm: "Got it -- you're on [area]. Your punchlist items will go there."
4. All subsequent punchlist commands use this area until she switches again

---

## Trigger: "Add to my punchlist: ..." or "Punchlist: ..."

1. Determine the correct punchlist file from context (ask if unclear)
2. Read that punchlist.md
3. Determine the next PL number (PL-001, PL-002, etc.) -- numbers are per-punchlist
4. Ask Ann (only if not clear from her description):
   - What did she expect vs what happened?
5. Append the new item under `## Punch List Items` using this format:

```markdown
### PL-XXX: [Short title from her description]
- **Area**: [sub-area within this functional area]
- **What I did**: [her steps, in her words]
- **What I expected**: [expected behavior]
- **What happened**: [actual behavior]
- **Severity**: Bug / UX / Question
- **Status**: Open
```

6. Confirm to Ann: "Got it -- logged as PL-XXX in [functional area]."

---

## Trigger: "Show me my punchlist" or "What's on my punchlist?"

1. Determine which punchlist from context (ask if unclear, or show all if she says "everything")
2. Read that punchlist.md
3. Summarize the open items in a simple list:
   - PL-XXX: [title] (Severity) -- Status
4. Show counts: X open, X fixed, X won't fix

---

## Trigger: "Mark PL-XXX as fixed" or "PL-XXX is fixed"

1. Find the punchlist containing that PL number (search if needed)
2. Change `**Status**: Open` to `**Status**: Fixed` on that item
3. Confirm: "Marked PL-XXX as fixed."

---

## Trigger: "Mark PL-XXX as won't fix" or "PL-XXX won't fix"

1. Find the punchlist containing that PL number (search if needed)
2. Change `**Status**: Open` to `**Status**: Won't Fix` on that item
3. Confirm: "Marked PL-XXX as won't fix."

---

## Notes

- Keep language simple and friendly -- Ann is not technical
- Never ask Ann to edit the file directly
- If Ann describes something vague, ask ONE clarifying question, not multiple
- Default severity to "Bug" unless she says "it's confusing" (UX) or "is this right?" (Question)
- PL numbers are scoped per punchlist (each area starts at PL-001)
- When creating a new punchlist for an area that doesn't have one yet, use the PlayerRegistration punchlist.md as a template and adapt the test areas
