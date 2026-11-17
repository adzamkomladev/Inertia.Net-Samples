---
name: CarParker
description: A calm car-park operator on a phone; petrol brand on cool paper, a live numbered-bay floor plan, plates set like real plates, money stated before every commitment.
colors:
  primary: "oklch(0.43 0.09 208)"
  primary-foreground: "oklch(0.985 0.005 195)"
  cta: "oklch(0.8 0.14 82)"
  cta-foreground: "oklch(0.36 0.08 70)"
  spot-free: "oklch(0.82 0.13 152)"
  spot-free-foreground: "oklch(0.32 0.08 155)"
  spot-booked: "oklch(0.56 0.14 256)"
  spot-booked-foreground: "oklch(0.985 0.005 195)"
  spot-booked-ink: "oklch(0.47 0.15 258)"
  spot-occupied: "oklch(0.3 0.03 235)"
  spot-occupied-foreground: "oklch(0.96 0.008 195)"
  spot-blocked: "oklch(0.86 0.008 200)"
  spot-blocked-foreground: "oklch(0.45 0.015 215)"
  background: "oklch(0.975 0.004 250)"
  card: "oklch(0.993 0.003 195)"
  foreground: "oklch(0.22 0.03 230)"
  secondary: "oklch(0.94 0.01 195)"
  muted: "oklch(0.945 0.008 195)"
  muted-foreground: "oklch(0.47 0.025 220)"
  accent: "oklch(0.93 0.014 195)"
  border: "oklch(0.91 0.008 195)"
  input: "oklch(0.85 0.012 200)"
  ring: "oklch(0.55 0.1 205)"
  destructive: "oklch(0.54 0.2 27)"
  plate-yellow: "oklch(0.93 0.13 95)"
  plate-ink: "oklch(0.2 0.02 90)"
  gate-night: "oklch(0.17 0.03 255)"
  primary-dark: "oklch(0.78 0.1 195)"
  primary-foreground-dark: "oklch(0.2 0.04 215)"
  cta-dark: "oklch(0.82 0.13 82)"
  cta-foreground-dark: "oklch(0.25 0.05 70)"
  spot-free-dark: "oklch(0.7 0.14 152)"
  spot-booked-dark: "oklch(0.66 0.13 256)"
  spot-booked-ink-dark: "oklch(0.78 0.1 256)"
  spot-occupied-dark: "oklch(0.34 0.03 232)"
  spot-blocked-dark: "oklch(0.3 0.01 215)"
  background-dark: "oklch(0.17 0.015 255)"
  card-dark: "oklch(0.215 0.018 255)"
  popover-dark: "oklch(0.25 0.022 255)"
  foreground-dark: "oklch(0.95 0.008 195)"
  muted-dark: "oklch(0.26 0.02 228)"
  muted-foreground-dark: "oklch(0.72 0.02 215)"
  accent-dark: "oklch(0.3 0.026 228)"
  border-dark: "oklch(1 0 0 / 13%)"
  ring-dark: "oklch(0.72 0.1 195)"
  destructive-dark: "oklch(0.7 0.18 22)"
typography:
  display:
    fontFamily: "Archivo, Archivo Fallback, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "4.5rem"
    fontWeight: 600
    lineHeight: 1.04
    letterSpacing: "-0.055em"
  headline:
    fontFamily: "Archivo, Archivo Fallback, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "1.875rem"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "-0.025em"
  title:
    fontFamily: "Archivo, Archivo Fallback, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "1.25rem"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "-0.005em"
  body:
    fontFamily: "Archivo, Archivo Fallback, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.43
  label:
    fontFamily: "Archivo, Archivo Fallback, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 600
    lineHeight: 1.33
  figure:
    fontFamily: "Archivo, Archivo Fallback, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "1.875rem"
    fontWeight: 700
    lineHeight: 1
    fontFeature: "\"tnum\""
  plate:
    fontFamily: "Barlow Semi Condensed, Barlow Fallback, Archivo, sans-serif"
    fontWeight: 700
    letterSpacing: "0.08em"
    fontFeature: "\"tnum\""
rounded:
  xs: "4px"
  sm: "6px"
  md: "8px"
  lg: "10px"
  xl: "14px"
  2xl: "16px"
  hero: "32px"
  full: "9999px"
spacing:
  "1": "4px"
  "1.5": "6px"
  "2": "8px"
  "3": "12px"
  "4": "16px"
  "5": "20px"
  "6": "24px"
  "8": "32px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.primary-foreground}"
    typography: "{typography.body}"
    rounded: "{rounded.lg}"
    padding: "0 16px"
    height: "40px"
  button-primary-phone:
    height: "44px"
  button-primary-lg:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.primary-foreground}"
    rounded: "{rounded.xl}"
    padding: "0 24px"
    height: "48px"
  button-ink:
    backgroundColor: "{colors.foreground}"
    textColor: "{colors.background}"
    rounded: "{rounded.lg}"
    padding: "0 16px"
    height: "40px"
  button-outline:
    backgroundColor: "{colors.card}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.lg}"
    padding: "0 16px"
    height: "40px"
  button-secondary:
    backgroundColor: "{colors.secondary}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.lg}"
    padding: "0 16px"
    height: "40px"
  button-ghost:
    textColor: "{colors.foreground}"
    rounded: "{rounded.lg}"
    padding: "0 14px"
    height: "36px"
  button-destructive:
    backgroundColor: "{colors.destructive}"
    textColor: "{colors.primary-foreground}"
    rounded: "{rounded.lg}"
    padding: "0 16px"
    height: "40px"
  input:
    backgroundColor: "{colors.card}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.lg}"
    padding: "4px 12px"
    height: "40px"
  input-phone:
    height: "44px"
  input-search:
    backgroundColor: "{colors.muted}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.lg}"
    padding: "0 12px 0 36px"
    height: "40px"
  surface-card:
    backgroundColor: "{colors.card}"
    rounded: "{rounded.xl}"
    padding: "16px"
  status-badge-free:
    textColor: "{colors.spot-free-foreground}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    padding: "2px 8px"
  status-badge-booked:
    textColor: "{colors.spot-booked-ink}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    padding: "2px 8px"
  status-badge-attention:
    textColor: "{colors.cta-foreground}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    padding: "2px 8px"
  status-badge-neutral:
    backgroundColor: "{colors.muted}"
    textColor: "{colors.muted-foreground}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    padding: "2px 8px"
  bay-occupied:
    backgroundColor: "{colors.spot-occupied}"
    textColor: "{colors.spot-occupied-foreground}"
    rounded: "{rounded.md}"
    height: "64px"
  bay-blocked:
    backgroundColor: "{colors.spot-blocked}"
    textColor: "{colors.spot-blocked-foreground}"
    rounded: "{rounded.md}"
    height: "64px"
  bay-selected:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.primary-foreground}"
    rounded: "{rounded.md}"
    height: "64px"
  plate:
    backgroundColor: "{colors.plate-yellow}"
    textColor: "{colors.plate-ink}"
    typography: "{typography.plate}"
    padding: "0.12em 0.55em"
  nav-item-active:
    textColor: "{colors.primary}"
    rounded: "{rounded.lg}"
    padding: "0 12px"
    height: "40px"
---

# Design System: CarParker

## Overview

**Creative North Star: "The Lit Floor Plan"**

CarParker reads like a well-run car park seen from the attendant's booth: cool paper surfaces, a deep petrol brand, and the floor plan itself as the hero. Bays are drawn as numbered, bordered cells in two facing rows with a dashed aisle between them, and their state colours (green free, blue booked, ink occupied, hatched grey blocked) are the one place the interface allows itself real colour. Everything around the map stays quiet so the bay, the price and the plate can be read at a glance.

Depth comes from a tonal ladder rather than hairlines: paper page, near-white cards with an almost invisible shadow, grey wells for tab strips, search fields and switches. Money and counts are set large and bold in Archivo with tabular figures; licence plates are set in Barlow Semi Condensed on a yellow plate with a dark inset rim, everywhere a plate appears. Ochre is the attention colour (pending, unverified, counts on nav items) and never a call to action.

The system is phone first for drivers and guards. Controls are 44px tall below md and step down to 40px from md; the booking total and Reserve button are pinned to the bottom edge on phones, the staff shells swap their sidebar for a bottom bar of pinned destinations plus More, and every navigation answers on the tap with a skeleton of the destination page. The gate console is the one deliberately dark surface: a night slab with a huge plate input, built for bright sun and poor light alike.

**Key Characteristics:**
- Petrol primary (hue 208) on cool paper; ochre for attention only; a four-state spot code that doubles as the status tone palette.
- Archivo for all interface text and figures; Barlow Semi Condensed reserved for number plates and condensed numerals.
- Tonal surface ladder with faint shadows; cards are 14px rounded, lot cards and sheets 16px, the landing preview 32px.
- State is never colour alone: bays carry glyphs and aria-labels, badges carry a dot and words, availability carries a bar and a phrase.
- Light and dark themes are both first-class, switched by a `.dark` class; the dark theme flips petrol to a light teal with dark text on it.

## Colors

Cool, low-chroma neutrals (hue 195 to 250) carrying one petrol brand colour, one ochre attention colour and a four-state spot code. The OKLCH values in `app.css` are canonical.

### Primary
- **Petrol** (`primary`; also aliased as `brand`; dark theme `primary-dark`): primary buttons, active navigation text and marker, the "P" brand tile, step numbers in the booking flow, selected bays, selected chips and day toggles, links, the first chart series. Focus rings use the close **Ring Teal** (`ring`, dark `ring-dark`).

### Secondary
- **Ochre** (`cta` with `cta-foreground`; dark `cta-dark`): attention, not action. Pending and requested badges, the unverified-email banner (20% tint), count badges on nav items, the "filling up" availability bar, text selection (30% mix). Despite its token name it is never used for a button.

### Tertiary (spot-state code)
- **Free Green** (`spot-free`, `spot-free-foreground`): free bays (25% fill, full border), paid/active/completed badges, done step numbers, the gate's success panel, positive trends.
- **Booked Blue** (`spot-booked`, `spot-booked-ink` for text on its tint): booked bays (12% fill, 70% border), confirmed, recurring, approved and refunded badges.
- **Occupied Ink** (`spot-occupied`, `spot-occupied-foreground`): occupied bays as solid dark cells with a car glyph.
- **Blocked Grey** (`spot-blocked`, `spot-blocked-foreground`): blocked bays, always with the diagonal hatch and a ban glyph.
- **Signal Red** (`destructive`, dark `destructive-dark`): failed, rejected, open incidents, theft, full lots, field errors, destructive buttons.

### Neutral
- **Cool Paper** (`background`, dark `background-dark`): the page.
- **Card White** (`card`, dark `card-dark`; popovers `popover-dark` one step lighter in dark): cards, headers, sidebar, sheets, inputs, outline buttons.
- **Deep Ink** (`foreground`, dark `foreground-dark`): text, the ink button, section tiles on the floor plan.
- **Well Grey** (`muted` and `secondary`; dark `muted-dark`): tab strips, search fields, floor-plan background (70%), Spot Cover toggle row, user block in the sidebar, neutral badges, empty-state icon wells.
- **Hover Grey** (`accent`, dark `accent-dark`): hover fills on rows (40%), ghost and outline buttons, the active storefront nav item.
- **Caption Grey** (`muted-foreground`, dark `muted-foreground-dark`): captions, table headers, inactive nav, hints.
- **Hairline** (`border`, dark `border-dark` as 13% white) and **Field Edge** (`input`): dividers and the inset ring on inputs and outline controls.
- **Plate Yellow** (`plate-yellow` with `plate-ink`): the number-plate face, fixed in both themes because real plates do not change with the theme.
- **Gate Night** (`--gate`, `--gate-foreground`, `--gate-accent`; classes `bg-gate`, `text-gate-foreground`, `bg-gate-accent`): the near-black surface of the guard header and bottom bar, the gate check-in panel and the landing page's illustrative floor plan, the same in both themes. Ink on it uses `text-gate-foreground` at 65 to 100% opacity; the reserved-bay highlight uses the gate accent.
- **Ochre Ink / Free Ink** (`--cta-ink`, `--spot-free-ink`; `text-cta-ink`, `text-spot-free-ink`): ochre and free-green as text or icons on their own tints (pills, callouts, alert icons, trend arrows). Dark in light mode, light in dark mode, so tinted callouts keep 4.5:1 in both themes; the `-foreground` tokens are only for text on the solid fill.

### Named Rules
**The Spot Code Rule.** Green, blue, ink and hatched grey mean free, booked, occupied and blocked, on the map and in every status badge. Do not use them for decoration or for unrelated meanings.

**The Ochre Is Not A Button Rule.** Ochre marks something that needs attention. Every call to action is Petrol (or Ink for the secondary solid button).

**The Tinted Badge Rule.** State colours fill badges at 8 to 18% with a 25 to 40% inset ring; full strength is kept for the dot, the bay border and solid occupied cells.

## Typography

**Display Font:** Archivo (with a size-adjusted Arial fallback, then system UI)
**Body Font:** Archivo (same stack; `font-display` is an alias of `font-sans`)
**Label/Mono Font:** Barlow Semi Condensed (with a size-adjusted Arial Narrow fallback), mapped to `font-mono`

**Character:** A sturdy grotesque that holds up at gate-console sizes, paired with a condensed signage face used only where the real world uses one: plates. Archivo loads at 400 to 700 and Barlow at 500 to 700 from Google Fonts without blocking first paint.

### Hierarchy
- **Display** (600, 3rem rising to 4.5rem at sm, line-height 1.04, -0.055em): the landing hero headline only.
- **Headline** (700, 1.875rem, tight tracking): the page title in every page header, balanced. Lot pages use 2.25rem; sheet titles 1.5rem at 600.
- **Title** (700, 1.25rem): booking step titles, lot card names, section headings inside cards; 1.125rem for card headings such as the reservation summary.
- **Body** (400, 14px, line-height 1.43): the working size across account, admin and guard. Inputs and the storefront nav are 16px on phones (no iOS zoom) and 14px from md; long explanations run at 14px with 24 to 28px line height.
- **Label** (600, 12px): badges, captions, table headers, sidebar group labels.
- **Figure** (700, 1.875rem, tabular): totals, lot hourly prices and stat-tile values; the phone reserve bar uses 1.5rem, price tags 14 to 30px at 600.
- **Plate** (Barlow Semi Condensed 700, 0.08em, uppercase, tabular): every licence plate, at 12px in lists up to 36px in the gate input.
- **Bay number** (Barlow Semi Condensed 700, 18px, tabular): the number on each bay of the floor plan, read like painted signage.

### Named Rules
**The Real Plate Rule.** A licence plate is always rendered with the plate treatment: condensed bold uppercase on Plate Yellow with a 2px dark inset rim and 0.35em corners. Plate inputs set the typed value bold and uppercase.

**The Tabular Figures Rule.** Every money value, count, time and bay number uses tabular figures (`table`, `.tabular-nums` and `.font-mono` all set it), in Archivo for money.

**The Signage Caps Rule.** Uppercase tracked lettering is limited to table column headers, sidebar group labels and floor-plan signage (section labels, the "Aisle" marking). It is not used as a kicker above headings.

## Layout

The storefront is a centred column (max 72rem; 16px side padding, 24px from sm) under a sticky 64px card-coloured header. Lot grids run one column on phones, two from sm, three from lg. The booking page splits into the step column plus a 22rem price summary that becomes sticky at `top: 6rem` from lg; below lg the summary's total and Reserve button move into a fixed bottom bar.

Account and admin pages use the console shell: a 240px fixed card sidebar from lg; below lg a 56px card header with a menu button and a fixed bottom bar of up to four pinned destinations plus More (56px rows, 12px labels). Main content caps at 64rem by default with 24 to 40px vertical padding and bottom padding of 5.5rem plus the safe-area inset to clear the bar. The guard shell is a full-width (max 80rem) Gate Night header over a two-column console: a 25rem sticky gate panel beside the live board.

Spacing follows Tailwind's 4px scale; gap-2 and gap-3 dominate, with 16px and 24px between blocks and 40 to 80px between landing sections. Cards pad 16px on phones and 20 to 24px from sm. Every fixed bar uses `pb-safe`/`pt-safe` for notches. Every task must work at 360px.

**The Thumb Reach Rule.** On phones the screen's commitment (Reserve, the total) is pinned to the bottom edge, and toasts move to the top centre below 640px so they never cover it; from sm toasts sit bottom right.

**The Every Tap Answers Rule.** A navigation to a known page shape (lots, lot, book, dashboard, guard, list) immediately swaps the content for that page's skeleton, fading in over 100ms.

## Elevation & Depth

Depth is tonal first: the page is Cool Paper, cards are a step lighter and carry only a 1px faint drop (`shadow-card`), wells are a step darker. Shadows grow only for things that lift or float: hovered lot and dashboard cards, the landing search card, the phone reserve bar, menus and sheets. Shadows are tinted with the ink hue, never pure black, except the sheet overlay.

### Shadow Vocabulary
- **Card** (`box-shadow: 0 1px 0 oklch(0.3 0.04 225 / 0.04)`): every resting card, header and segmented-control thumb.
- **Lifted** (`box-shadow: 0 1px 2px oklch(0.3 0.04 225 / 0.06), 0 10px 28px -14px oklch(0.3 0.04 225 / 0.22)`): hover on clickable cards (with a 2px rise), the landing search card.
- **Pop** (`box-shadow: 0 2px 6px oklch(0.3 0.04 225 / 0.08), 0 18px 40px -18px oklch(0.3 0.04 225 / 0.3)`): the fixed phone reserve bar, dropdowns and pickers.
- **Primary glow** (Tailwind `shadow-sm` tinted 20% primary, `shadow-md` 25% on hover): primary buttons only.
- **Sheet** (Tailwind `shadow-2xl` over a 45% black overlay with a small backdrop blur): side panels.

### Named Rules
**The Ladder Not Lines Rule.** Separate surfaces by tone and the faint card shadow; borders are reserved for dividers inside a card, table rows, sheet edges and the phone bottom bar.

## Shapes

Softly squared, not pills. Buttons, inputs, nav items, chips and day toggles are 10px; large buttons, cards, wells and the floor plan are 14px; lot cards, sheets' inner edges and landing panels 16px; the landing floor-plan preview is the one 32px shape. Bays are 8px cells with a 2px border, section tiles 8px ink squares, legend swatches 5px. Badges, avatars, step numbers, empty-state icon wells, switches and the availability bar are fully round. Plates use 0.35em corners. Blocked bays carry a -45deg stripe hatch at 16% of the blocked foreground.

## Components

### Buttons
Solid, slightly squared, and always thumb-sized on phones.
- **Shape:** 10px corners; large buttons 14px.
- **Primary:** Petrol with near-white text, 14px/600, 44px tall below md and 40px from md, 16px side padding, a faint petrol glow. Large is 48px with 24px padding and 16px text (Reserve, Log in / Sign up in the phone menu).
- **Hover / Focus / Active:** primary dims to 90% and its glow deepens; outline and ghost fill with Hover Grey; focus shows a 2px ring with a 2px background offset; press scales to 0.98 (ghost 0.97). Disabled is 50% opacity.
- **Ink / Outline / Secondary / Ghost / Link / Destructive:** ink is foreground-on-background; outline is card with a Field Edge inset ring that tints toward the ring on hover; secondary is Well Grey; ghost is bare until hover; link is petrol text underlined on hover; destructive is Signal Red.
- **Icon buttons:** 44px below md, 40px from md; small 40/36px.

### Status Badges
- **Style:** a rounded-full pill, 12px/500, 2px by 8px padding, a 1px inset ring, and a 6px dot before the label. Seven tones from one map in `lib/status.ts`: free green, booked blue, occupied ink, ochre attention, red failure, grey muted, petrol primary. Labels come from the API value in sentence case ("No show", "Refund pending").

### Price Tag and Figures
Money renders through the price tag: Archivo 600 with tabular figures at 14, 16, 20 or 30px, optionally Caption Grey. Totals and hero figures step up to 700.

### Cards / Containers
- **Corner Style:** 14px (lot cards 16px).
- **Background:** Card White on Cool Paper.
- **Shadow Strategy:** Card at rest; Lifted plus a 2px rise on hover only where the whole card is a link.
- **Internal Padding:** 16px, 20 to 24px from sm. Stat tiles carry a 14px/500 caption with optional icon, one 30px bold tabular figure and an optional trend in free green or red with an arrow.

### Inputs / Fields
- **Style:** card background, 1px Field Edge inset ring, 10px corners, 44px tall and 16px text below md, 40px and 14px from md.
- **Search:** Well Grey fill, no ring, magnifier inset 12px; 48px with 14px corners in the landing search card.
- **Focus:** a 2px ring in Ring Teal.
- **Error / Disabled:** `aria-invalid` turns the ring 2px red; errors are red 14px text with `role="alert"`. Disabled is 50% opacity. Native date and select inputs are not used; custom date, time and select pickers replace them.
- **Switch:** 44 by 24px track (petrol on, Field Edge off) with a white 20px thumb and an enlarged invisible hit area.

### Navigation
- **Storefront header:** sticky 64px card header with the brand mark, three 16px nav links (active on Hover Grey), a 208px search field from lg, avatar menu or Log in / Sign up, and a 44px menu button below md that opens a left sheet (85vw, max 24rem) with 44px rows.
- **Console shell sidebar:** 240px card sidebar with the brand mark and the shell name, grouped nav with 12px caps group labels, 40px items with 18px Lucide icons whose stroke thickens from 1.75 to 2.25 when active; the active item sits on a 10% petrol wash in petrol text with a 4px petrol marker bar that scales in over 300ms. Ochre count badges sit at the row end. A user block on Well Grey holds notifications, sound, theme and log out.
- **Phone bottom bar:** card at 95% with backdrop blur, top hairline, safe-area padding; up to four destinations plus More, active in petrol with a heavier icon stroke. More and the menu button open the same left sheet with 48px rows, the active row in solid petrol.
- **Brand mark:** a 36px petrol tile with a bold "P" beside the bold wordmark.

### Data Table
One component, three layouts from the same columns. From md it is a real table: a Well Grey header row with 12px caps Caption Grey headers, 14px cells padded 16 by 14px, hairline row dividers, rows tinted with Hover Grey at 40% on hover, and a rounded-full outlined "Details" link per row. Below md each row becomes a card (Card White, 14px corners, 16px padding) of caption/value pairs with a 44px "View details" link stretched over the whole card, or, in compact mode, a single 64px tappable line of title, a dot-separated meta line and trailing cells with a chevron. Each phone row keeps exactly one focusable control; interactive cells are lifted above the stretched link. Empty tables show a centred title and description.

### Spot Grid (signature)
The live floor plan. A Well Grey segmented tablist picks the level (each tab shows "Level N" and its free count; arrow keys move between levels). Each section is a header with an ink section tile, then two facing rows of bays six to a row with a dashed "Aisle" rule between them, on a Well Grey panel. Bays are 64px tall (72px from sm), at least 44px wide, 8px corners and a 2px border, showing the bay number in bold tabular figures, a state glyph (clock booked, car occupied, ban blocked; check when selected) and a small type icon (EV, accessible, covered, compact) in the corner. Selected bays turn solid Petrol. Every bay has an aria-label with state and type. A legend lists each state swatch with its live count, plus the bay types present.

### Booking Flow (signature)
Four numbered step cards (When, Choose a bay, Spot Cover, Vehicle and payment). The step number is a 36px petrol circle that becomes a free-green check when done, and the chosen value appears as a summary beside the title. One-off versus weekly is a two-option segmented control; durations and weekdays are 44px toggle chips that fill petrol when pressed. Spot Cover is a Well Grey row with a switch and the price stated before the toggle. Saved plates appear as plate chips; picking one pops a check. The price summary card lists parking, cover and a 30px bold total with the refund rule beneath it; on phones the total and a large Reserve button live in a fixed bottom bar with the Pop shadow.

### Availability
A tabular "N of M free" figure, a phrase ("Plenty of space", "Filling up", "Almost full", "Full") and an 8px round bar in free green, ochre or red whose width animates over 500ms.

### Plates
The `plate` treatment is used for plates in tables, chips, the gate board and the gate input (80px tall, 36px text, centred). See the Real Plate Rule.

### Sheets
Side panels on the dialog primitive: left for navigation, right for create and edit flows. Card background, a hairline edge, 16px outer corners from sm, a 1.5rem title and description in a bordered header, a 44px close button that rotates on hover, and an optional pinned footer with safe-area padding. They slide in and out over 300ms on the glide curve over a 45% black blurred overlay.

### Loading, Toasts and Empty States
- **Skeleton:** a block of foreground at 8%, 10px corners by default (14px for card shapes), pulsing.
- **Navigation skeleton:** see the Every Tap Answers Rule; each kind mirrors its page's grid, and the content underneath is hidden until the visit lands.
- **Toasts:** one Sonner toaster for the whole app with a close button, top centre on phones and bottom right from sm. Server flash messages arrive as toasts.
- **Empty state:** a 56px round Well Grey icon well with a 24px thin-stroke icon, an 18px semibold title, a 14px Caption Grey description capped at 24rem, and an optional action.

### Motion
- **Glide** (`cubic-bezier(0.22, 1, 0.36, 1)`): the house curve. Page content rises 8px and fades in over 420ms on each page change; card lift and shadow take 220ms; sheets 300ms.
- **Spring** (`cubic-bezier(0.16, 1, 0.3, 1)`): the 360ms pop (scale 0.6 to 1) for confirmation checks and the gate success panel.
- **Live and ambient:** a 2.4s soft opacity pulse on the reserved bay and a 3.6s looping car drive in the landing preview only.
- **Reduced motion:** under `prefers-reduced-motion: reduce` all animation and transition durations collapse to 0.01ms with one iteration, and view-transition animations are removed.

## Do's and Don'ts

### Do:
- **Do** keep Petrol (`oklch(0.43 0.09 208)`, dark `oklch(0.78 0.1 195)`) as the only call-to-action colour.
- **Do** use the spot code (green free, blue booked, ink occupied, hatched grey blocked) consistently on the map, the legend and status badges.
- **Do** pair every state colour with a glyph, a dot or words, and an aria-label on bays.
- **Do** render every licence plate with the plate treatment.
- **Do** use tabular figures for money, counts, times and bay numbers.
- **Do** make every control at least 44px below md, stepping to 40px from md, and keep phone commitments pinned above the safe-area inset.
- **Do** separate surfaces with the tonal ladder and the faint card shadow; save Lifted and Pop for hover and floating elements.
- **Do** show the destination's skeleton the moment a navigation starts.
- **Do** state price, daily cap, cover cost and refund rule beside the action that commits to them.
- **Do** honour reduced motion globally and keep transitions on the Glide curve.

### Don't:
- **Don't** use Ochre for buttons or links; it marks attention only.
- **Don't** convey bay or booking state by colour alone.
- **Don't** set a plate in Archivo or without the yellow plate face.
- **Don't** use native date, time or select inputs.
- **Don't** fill a badge or panel with a state colour at full strength, except occupied bays and the gate success panel.
- **Don't** add uppercase tracked kickers or eyebrows above headings; caps belong to table headers, nav group labels and floor-plan signage.
- **Don't** separate cards with heavy borders or hard shadows.
