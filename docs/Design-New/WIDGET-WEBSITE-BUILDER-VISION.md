# Widget-Driven Website Builder — Product Vision

## The Opportunity

Clients have repeatedly asked TSIC to produce websites for their clubs and tournaments. Rather than building bespoke sites, the widget dashboard system can evolve into a **self-service website builder** where clients assemble their own public-facing pages from a catalog of widgets.

## How It Works

The widget system already has the right data model:

| Table | Role in Website Builder |
|-------|------------------------|
| **Widget** | Catalog of available building blocks (banner, bulletins, schedule, registration CTA, etc.) |
| **WidgetCategory** | Layout sections — content up top, tools in the middle, insights at bottom |
| **WidgetDefault** | "Out of the box" website templates per JobType + role (e.g. tournaments get schedule + registration, clubs get roster + news) |
| **JobWidget** | Per-client customization — Club X hides the banner, reorders widgets, enables sponsors |
| **Config (JSON)** | Per-widget settings — labels, colors, icons, custom text, linked routes |

Every view in the app is an instance of this model:
- **Public website** = widget dashboard for `role = Anonymous`
- **Player home** = widget dashboard for `role = Player`
- **Director admin** = widget dashboard for `role = Director`
- **TSIC index** = special case (future: could also be widget-driven)

## Widget Catalog (Current + Future)

### Content Widgets (render inline, full-width)
| Widget | ComponentKey | Description | Status |
|--------|-------------|-------------|--------|
| Client Banner | `client-banner` | Job logo, background image, text overlay | Building now |
| Bulletins | `bulletins` | Active announcements with date filtering | Building now |
| Custom HTML | `custom-html` | Freeform content block (sanitized) | Future |
| Photo Gallery | `photo-gallery` | Image grid from job media library | Future |
| Sponsor Logos | `sponsors` | Logo grid with optional links | Future |
| Contact Info | `contact-info` | Address, phone, email, social links | Future |
| About Section | `about` | Rich text about the organization | Future |

### Functional Widgets (render as cards or embedded views)
| Widget | ComponentKey | Description | Status |
|--------|-------------|-------------|--------|
| Schedule Preview | `schedule-preview` | Embedded public schedule view | Future |
| Registration CTA | `registration-cta` | "Sign up now" with status awareness | Future |
| Standings | `standings` | Live standings table | Future |
| Team Roster | `team-roster` | Public roster display | Future |
| Recent Results | `recent-results` | Latest game scores | Future |
| Countdown Timer | `countdown` | Days until event start | Future |

### Navigation Widgets (render as clickable cards — existing)
| Widget | ComponentKey | Description | Status |
|--------|-------------|-------------|--------|
| Admin tools | various | Route-linked cards for admin features | Done |

## Admin UI Vision (Future)

The "build your own website" admin screen for Directors/Club Reps:

1. **Left panel**: Available widget catalog (drag source)
2. **Center panel**: Live preview of the public page
3. **Right panel**: Widget settings (config JSON editor, simplified)
4. **Actions**: Enable/disable, reorder (drag), configure per widget

This is essentially a CRUD interface over the `JobWidget` table with a live preview. The widget dashboard component in "preview mode" renders the result in real-time.

## Architecture Foundation (Being Built Now)

The current implementation establishes the critical patterns:

1. **Universal renderer** — widget-dashboard component renders both content and navigation widgets via `componentKey` → `@switch`
2. **Public endpoint** — `GET /api/widget-dashboard/public/{jobPath}` serves Anonymous role widgets without authentication
3. **Content section** — new section type ("content") renders full-width, no card grid
4. **Seed data pattern** — WidgetDefault entries define "out of the box" templates per JobType

Every future widget follows the same pattern:
1. Add a `Widget` row with a `componentKey`
2. Create the Angular component
3. Register it in the `@switch` block
4. Seed `WidgetDefault` entries for relevant roles/JobTypes
5. Clients customize via `JobWidget` overrides

## Revenue Implications

- Clients get a "free" basic website with their TSIC subscription (default widgets)
- Premium widgets (photo gallery, custom HTML, sponsors) could be tiered
- Custom widget development for enterprise clients
- Reduces "build me a website" support requests to "configure your widgets"

---

**Status**: Vision document — foundational implementation in progress
**Created**: 2026-02-15
