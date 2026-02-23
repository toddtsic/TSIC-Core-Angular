# Landing Page — Ann's Git Workflow

> **Tip:** Hover over any grey prompt box below to reveal a copy icon in the top-right corner.

---

## Scenario 1: Saving Your Work ("The Locker")

Whenever you want to save progress, tell Claude:

```
Please commit, tag, and push my landing page work.
Description: [one sentence].
```

Example:
```
Please commit, tag, and push my landing page work.
Description: Hero section layout complete.
```

Nothing goes to dev. It's just saved and recoverable.

---

## Scenario 2: "I want this visible on dev"

Tell Claude:

```
Please commit, tag, and push my landing page work.
Description: [one sentence].
```

Then tell Todd:
> *"Landing page is pushed — ready to publish to dev.teamsportsinfo.com when you get a chance."*

---

## Scenario 3: "I want to A/B compare"

This creates a standalone frozen copy of the landing page at its own URL so you can open multiple versions side by side in the browser.

Just tell Claude you want an A/B copy. Claude figures out the version number automatically.

```
Please create a versioned copy of the landing page for A/B comparison.
```

Claude will:
1. Check what versions already exist and calculate the next number
2. Copy the `/tsic` folder to `/tsic-v[NUMBER]`
3. Rename all components inside to avoid conflicts
4. Create a new route for `/tsic-v[NUMBER]`
5. Commit the changes
6. Tag the commit as `landing-v[NUMBER]`
7. Push everything to GitHub
8. Tell you the version number it used

Then tell Todd:
> *"Landing page v[NUMBER] is pushed — can you publish /tsic-v[NUMBER] for A/B?"*

(Use the number Claude reported in step 8.)

Todd publishes it to IIS. You can then open `/tsic`, `/tsic-v1`, `/tsic-v2` side by side in separate browser tabs.
