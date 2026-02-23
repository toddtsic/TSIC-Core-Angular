# Ann's Landing Page Workflows

When Ann uses any of the trigger phrases below, execute the corresponding workflow exactly as specified. No clarification needed.

---

## Trigger 1: "Please commit, tag, and push my landing page work."

1. `git add` all changes in `src/app/views/home/tsic-landing/`
2. `git commit --no-verify -m "feat(landing): [description Ann provided]"`
3. `git tag landing-[next available tag]`
4. `git push`

---

## Trigger 2: "Please create a versioned copy of the landing page for A/B comparison."

1. Check existing `tsic-v*` folders under `src/app/views/home/` to determine the next version number
2. Copy `src/app/views/home/tsic-landing/` to `src/app/views/home/tsic-landing-v[N]/`
3. Rename all component class names and selectors inside the new folder to avoid conflicts (e.g. `TsicLandingComponent` → `TsicLandingV[N]Component`, selector `app-tsic-landing` → `app-tsic-landing-v[N]`)
4. Add a new route for `/tsic-v[N]` pointing to the new component
5. `git add` all changes
6. `git commit --no-verify -m "feat(landing): create A/B version tsic-v[N]"`
7. `git tag landing-v[N]`
8. `git push`
9. Tell Ann: *"Done — version v[N] is pushed. Tell Todd to publish /tsic-v[N] for A/B."*
