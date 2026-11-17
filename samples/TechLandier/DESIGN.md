---
name: TechLandier
description: A clean, confident electronics storefront with buy-now-pay-later built in; product art first, one blue accent, money always stated plainly.
colors:
  primary: "oklch(0.5 0.2 262)"
  primary-foreground: "oklch(0.99 0.003 250)"
  accent-tint: "oklch(0.94 0.02 262)"
  accent-tint-foreground: "oklch(0.32 0.14 262)"
  page: "oklch(0.972 0.003 250)"
  card: "oklch(1 0 0)"
  ink: "oklch(0.2 0.012 255)"
  muted: "oklch(0.945 0.004 250)"
  muted-foreground: "oklch(0.48 0.012 255)"
  border: "oklch(0.915 0.004 250)"
  input: "oklch(0.86 0.005 250)"
  success: "oklch(0.45 0.115 155)"
  warning: "oklch(0.5 0.12 65)"
  destructive: "oklch(0.52 0.2 27)"
  primary-dark: "oklch(0.7 0.16 262)"
  primary-foreground-dark: "oklch(0.15 0.02 262)"
  accent-tint-dark: "oklch(0.29 0.04 262)"
  page-dark: "oklch(0.155 0.006 260)"
  card-dark: "oklch(0.2 0.007 260)"
  popover-dark: "oklch(0.23 0.008 260)"
  ink-dark: "oklch(0.95 0.004 250)"
  muted-dark: "oklch(0.25 0.008 260)"
  muted-foreground-dark: "oklch(0.72 0.01 255)"
  success-dark: "oklch(0.74 0.14 155)"
  warning-dark: "oklch(0.8 0.13 75)"
  destructive-dark: "oklch(0.7 0.18 22)"
  hero-night: "oklch(0.22 0.06 262)"
typography:
  display:
    fontFamily: "Geist, Geist Fallback, Inter, ui-sans-serif, system-ui, sans-serif"
    fontSize: "3.75rem"
    fontWeight: 600
    lineHeight: 1.06
    letterSpacing: "-0.025em"
  headline:
    fontFamily: "Geist, Geist Fallback, Inter, ui-sans-serif, system-ui, sans-serif"
    fontSize: "1.75rem"
    fontWeight: 600
    lineHeight: 1.25
    letterSpacing: "-0.025em"
  title:
    fontFamily: "Geist, Geist Fallback, Inter, Inter Fallback, ui-sans-serif, system-ui, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 500
    lineHeight: 1.375
    letterSpacing: "0em"
  body:
    fontFamily: "Geist, Geist Fallback, Inter, Inter Fallback, ui-sans-serif, system-ui, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.43
  label:
    fontFamily: "Geist, Geist Fallback, Inter, Inter Fallback, ui-sans-serif, system-ui, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 500
    lineHeight: 1
  price:
    fontFamily: "Geist, Geist Fallback, Inter, Inter Fallback, ui-sans-serif, system-ui, sans-serif"
    fontSize: "1.25rem"
    fontWeight: 600
    letterSpacing: "-0.025em"
    fontFeature: "\"tnum\""
rounded:
  sm: "4px"
  md: "8px"
  lg: "12px"
  xl: "18px"
  hero: "24px"
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
    rounded: "{rounded.full}"
    padding: "0 20px"
    height: "40px"
  button-primary-touch:
    height: "44px"
  button-primary-lg:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.primary-foreground}"
    rounded: "{rounded.full}"
    padding: "0 28px"
    height: "48px"
  button-outline:
    backgroundColor: "{colors.card}"
    textColor: "{colors.ink}"
    rounded: "{rounded.full}"
    padding: "0 20px"
    height: "40px"
  button-secondary:
    backgroundColor: "{colors.muted}"
    textColor: "{colors.ink}"
    rounded: "{rounded.full}"
    padding: "0 20px"
    height: "40px"
  button-ghost:
    textColor: "{colors.ink}"
    rounded: "{rounded.full}"
    padding: "0 16px"
    height: "36px"
  input:
    backgroundColor: "{colors.card}"
    textColor: "{colors.ink}"
    rounded: "{rounded.lg}"
    padding: "4px 12px"
    height: "40px"
  input-search:
    backgroundColor: "{colors.muted}"
    textColor: "{colors.ink}"
    rounded: "{rounded.full}"
    padding: "4px 12px 4px 40px"
    height: "40px"
  surface-card:
    backgroundColor: "{colors.card}"
    rounded: "{rounded.xl}"
    padding: "16px"
  status-badge-neutral:
    backgroundColor: "{colors.muted}"
    textColor: "{colors.muted-foreground}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    padding: "4px 10px"
  status-badge-info:
    backgroundColor: "{colors.accent-tint}"
    textColor: "{colors.accent-tint-foreground}"
    typography: "{typography.label}"
    rounded: "{rounded.full}"
    padding: "4px 10px"
  nav-item-active:
    backgroundColor: "{colors.accent-tint}"
    textColor: "{colors.accent-tint-foreground}"
    rounded: "{rounded.lg}"
    padding: "0 12px"
    height: "40px"
---

# Design System: TechLandier

## Overview

**Creative North Star: "The Well-Lit Counter"**

TechLandier looks like a good local electronics shop with the lights on: cool neutral surfaces, product art in the middle of the frame, and one blue accent that marks what you can act on. The store is calm so the devices and the prices can be loud. Nothing is decorative for its own sake; tint, weight and the occasional raised shadow carry hierarchy, and state colours appear only when something actually has a state.

Money is the subject the system treats most carefully. Prices use tabular figures in the body face, installment amounts sit next to the full price with the fee named, and every order that is being paid over time says so in one shared phrase. Density is moderate on the storefront (photo-led grids, generous gaps) and compact in the account and admin shells (tables, stat tiles, badges), but both share the same tokens, radii and type.

The system is phone first. Touch layouts get 44px targets through a `pointer: coarse` variant rather than a separate mobile skin, purchase and save actions stay pinned within thumb reach, and every navigation answers on the tap with the skeleton of the page it is heading to.

**Key Characteristics:**
- Cool neutral tonal layering (page, card, popover) with a single blue accent (hue 262).
- Geist for everything, Inter as the named second face in the stack; no serif, no monospace for numbers.
- Pill-shaped controls, softly rounded cards (18px), surfaces separated by tone and a hairline ring, not heavy borders.
- Status always as tinted pill plus dot plus words; colour is never the only signal.
- Light and dark themes are both first-class, switched by a `.dark` class applied before first paint.

## Colors

A cool, nearly achromatic neutral ramp (hue 250 to 260, chroma under 0.013) carrying one saturated blue; green, amber and red exist only to report state. The OKLCH values in `app.css` are canonical; emails and PDFs carry hex approximations of the same roles.

### Primary
- **Counter Blue** (`primary`; dark theme `primary-dark`): the one accent. Primary buttons, links in tables, the active bottom-bar destination, the wordmark's full stop, focus rings (`--ring` is the same value) and the first chart series. Also aliased as `--brand` for selection tint and email/PDF parity.
- **Blue Wash** (`accent-tint` with `accent-tint-foreground`): the quiet form of the accent. Active sidebar items and info/primary status pills. Where blue needs to mark place rather than demand a click, use the wash.

### Tertiary (state only)
- **Paid Green** (`success`): paid, delivered, in stock, coverage remaining, discount percentages.
- **Amber Notice** (`warning`): low stock ("Only 3 left"), coverage running out, overdue-soon tones.
- **Signal Red** (`destructive`): overdue, cancelled, failed, destructive buttons, invalid fields.
State tints are applied at low alpha on pills (success 12%, warning 14%, destructive 10%) with the full colour on text and dot.

### Neutral
- **Daylight Page** (`page`): app background; tone sits just below the white card.
- **Card White** (`card`): cards, inputs, popovers, outline buttons, the store footer.
- **Graphite Ink** (`ink`): body text and headings.
- **Shelf Grey** (`muted`): skeletons, secondary buttons, search field fill, neutral pills, row hover at 40%.
- **Caption Grey** (`muted-foreground`): captions, table headers, hints, inactive nav.
- **Hairline** (`border`) and **Field Edge** (`input`): dividers and input strokes. In dark mode both become translucent white (9% and 16%).
- **Night Hero** (`hero-night`): the home hero's deep blue panel, which opts into the dark token set so its button and accent text use the dark-theme blue.

The dark theme (`page-dark` through `destructive-dark`) lifts the accent to lightness 0.7 with dark text on it, and steps surfaces up by tone: page 0.155, card 0.2, popover 0.23.

### Named Rules
**The One Blue Rule.** Blue is the only hue that means "act here" or "you are here". Green, amber and red are reserved for state and never used as decoration or as a second brand colour.

**The Tinted Pill Rule.** A state colour at full strength appears on text, dots and thin meters; its area fills are always a 10 to 14% tint.

## Typography

**Display Font:** Geist (with a size-adjusted Arial fallback, then Inter)
**Body Font:** Geist (with Geist Fallback, Inter, Inter Fallback, system UI)
**Label/Mono Font:** none distinct; numbers use Geist with tabular figures.

**Character:** One neo-grotesk doing every job, tightened for headings (-0.022em on h1 to h3, -0.025em where `tracking-tight` is applied) and left neutral for body. Geist and Inter are a recorded brand commitment from PRODUCT.md, not a default; both load from Google Fonts without blocking first paint, swapping in over metric-matched Arial fallbacks.

### Hierarchy
- **Display** (600, 2.25rem rising to 3.75rem at lg, line-height 1.06): the home hero headline only, balanced text.
- **Headline** (600, 1.5rem rising to 1.75rem at sm): the page title in every app page header. Content pages (About, Policy) use 1.875rem to 2.25rem.
- **Title** (500, 15px, line-height 1.375): product names on tiles, clamped to two lines; card and section titles use 1rem to 1.125rem at 600.
- **Body** (400, 14px, line-height 1.43): the working size across account and admin; long descriptions at 1rem with line-height 1.75. Inputs are 16px on phones (no iOS zoom) and 14px from md.
- **Label** (500, 12px): badges, table headers, captions, grouped-nav labels. Sentence case, no tracking.
- **Price** (600, 16px / 20px / 30px by size, tabular figures): every money value and every count, via the `price` utility.

### Named Rules
**The Tabular Money Rule.** Every amount, count and installment figure uses `font-feature-settings: "tnum"` in the body face. Never a monospace font for money.

**The Sentence Case Rule.** In the app, labels and captions are sentence case at 12px/500. Uppercase tracked lettering is not part of the app's type ramp.

## Layout

The storefront is a centred column (max 80rem; 16px, 24px and 32px side padding at base, sm and lg) under a sticky 64px glass header. Product grids run two columns on phones and widen with the viewport; the product page splits into art plus a 25rem buy box, checkout into form plus a 23rem summary. On large screens those side columns are sticky at `top: 6rem`.

Account and admin pages use the sidebar shell: a 256px fixed card-coloured sidebar from lg, and on smaller screens a 56px glass top bar plus a fixed bottom bar with four pinned destinations and More. Main content caps at 72rem by default, with bottom padding that clears the bottom bar and the safe-area inset.

Spacing follows Tailwind's 4px scale; the reused steps are 4, 6, 8, 12, 16, 20 and 24px (gap-2 and gap-3 dominate), with 32px between page sections. Cards pad 16px on phones and 20px from sm. Every fixed bar respects `env(safe-area-inset-*)`; the viewport uses `viewport-fit=cover`. Every task must work at 360px.

**The Thumb Reach Rule.** On phones the primary action for the screen (Buy, Pay, Save) is pinned to the bottom edge, and toasts move to the top so they never cover it.

## Elevation & Depth

Depth is tonal first. Cards sit on the page by being white on a slightly darker page, ringed by a 1px hairline at 70% border strength (the `surface` utility), not by an outline or a shadow. Shadows are reserved for things that float: menus, popovers, dialogs and sheets, plus a single raised shadow on hover for interactive care cards and on the floating save bar. Headers and bottom bars use frosted glass (82% page colour, 16px blur, 1.5 saturation).

### Shadow Vocabulary
- **Raised** (`box-shadow: 0 1px 2px oklch(0.2 0.02 255 / 0.05), 0 8px 24px -8px oklch(0.2 0.02 255 / 0.12)`; dark: `0 1px 2px oklch(0 0 0 / 0.3), 0 8px 24px -8px oklch(0 0 0 / 0.5)`): hover lift on interactive surface cards and the floating unsaved-changes bar.
- **Overlay** (Tailwind `shadow-lg`/`shadow-md`): sheets, dialogs, dropdowns and popovers.

### Named Rules
**The Flat Until Touched Rule.** Surfaces are flat at rest. A shadow appears only when an element floats above the page or responds to hover.

## Shapes

Controls are pills: buttons, search fields, badges and the floating save bar are fully rounded. Containers are soft rectangles: cards and product art 18px, inputs, sidebar items and skeleton blocks 12px, menu items 8px. The home hero panel is the one 24px shape. Avatars, status dots and empty-state icon wells are circles. Product art fallback is a studio backdrop: a radial highlight over a desaturated tint derived from a seeded hue, so placeholder art never looks broken.

## Components

### Buttons
Confident pills that never shrink below a thumb.
- **Shape:** fully rounded (9999px).
- **Primary:** Counter Blue on near-white, 14px/500, 40px tall with 20px side padding at md and up; 44px on phones and on any `pointer: coarse` device. Large (48px, 28px padding, 16px text) for purchase CTAs.
- **Hover / Focus / Active:** primary dims to 90%; outline and ghost fill with Shelf Grey; focus shows a 3px ring at 50% of the ring colour; press scales to 0.98. Disabled is 50% opacity.
- **Outline / Secondary / Ghost / Link / Destructive:** outline is card-coloured with a Field Edge stroke; secondary is Shelf Grey; ghost is bare until hover; link is blue text with underline on hover; destructive is Signal Red.
- **Icon buttons:** 40px at md, 44px on coarse pointers; every compact size (xs, sm, icon-xs, icon-sm) also resolves to 44px under `pointer: coarse`.

### Status Badges
- **Style:** tinted pill, 12px/500, 4px by 10px padding, with a 6px dot before the label. Tones map from one table: primary/info use Blue Wash, success, warning and destructive use their tints, neutral uses Shelf Grey.
- **Order badges:** an order shows its status badge plus a payment badge only when the payment adds information. A plan order with installments outstanding always reads **"On plan · 2 of 6 paid"** (never a bare "Paid"), a completed plan reads "Plan paid in full", and a cancelled order that never took money shows no payment badge instead of "Unpaid". Customer and staff views use the same label function.

### Cards / Containers
- **Corner Style:** 18px.
- **Background:** Card White on Daylight Page.
- **Shadow Strategy:** hairline ring at rest; Raised on hover only where the card is interactive.
- **Internal Padding:** 16px, 20px from sm. Stat tiles carry a 14px caption, one 24px tabular number and an optional 12px hint.

### Inputs / Fields
- **Style:** card background, Field Edge stroke, 12px radius, 44px tall on phones and 40px from md, 16px text on phones.
- **Search:** a pill filled with Shelf Grey, no stroke, magnifier inset at 14px.
- **Focus:** border turns ring blue plus a 3px ring at 50%.
- **Error / Disabled:** `aria-invalid` turns the border red with a faint red ring; field errors slide in 4px over 200ms. Disabled is 50% opacity. Native date and select inputs are not used; custom select and date components replace them.

### Navigation
- **Storefront:** sticky glass header (64px) with wordmark, ghost-pill nav, a centred pill search, and on phones a second row holding the search so it is always one tap away. The phone menu is a left Sheet at 85vw (max 24rem) with 44px rows.
- **App shells:** sidebar items are 40px, 12px radius, 18px Lucide icons whose stroke thickens (1.75 to 2.1) when active, active item on Blue Wash. On phones a glass bottom bar of four destinations plus More, 56px tall, 11px labels, active in blue with a heavier icon stroke.
- **Wordmark:** "TechLandier" in Geist 600 with a blue full stop; type does the work.

### Data Tables
One component, two layouts from the same slots. From md it is a table: 12px Caption Grey headers, 14px body, 16px by 12px cells, hairline row dividers, rows tinted with Shelf Grey at 40% on hover, the primary column rendered as a blue prefetching link. Below md each row becomes a stacked card: the primary column as a 44px-tall link title, the rest as a two-column caption/value grid, with `full` columns spanning both. Pages may supply their own compact card slot. Empty tables render the shared empty state.

### Sheets
Drawers built on the dialog primitive: right by default, left for the storefront menu, top and bottom available. Page-coloured background, a hairline edge, overlay shadow, slide in over 500ms and out over 300ms. The close control is a 36px circle that grows to 44px on coarse pointers. Admin create and edit forms (products, riders, zones) open in sheets rather than new pages.

### Sticky CTA Bars
- **Buy bar (product page, below lg):** appears once the in-page CTA scrolls away, slides up over 200ms. Page colour at 95% with backdrop blur, top hairline, safe-area padding; left side states the price or "6 × $X every 2 weeks" with the total and fee, right side a large primary pill.
- **Checkout bar (below lg):** the total and place-order action are fixed to the bottom with the same treatment.
- **Unsaved-changes bar (admin settings):** a floating ink-coloured pill (inverted: foreground background, page-coloured text) with Raised shadow, centred 16px above the bottom edge, offering Discard and Save. It exists only while the form is dirty.

### Loading: Skeletons and Navigation Skeletons
- **Skeleton:** a Shelf Grey block, 12px radius by default, pulsing.
- **Navigation skeleton:** on every GET navigation to a known page type (grid, product, checkout, list, dashboard), the layout immediately swaps its content for a skeleton of the destination's shape, fading in over 100ms. A hover-prefetched visit that lands within 120ms shows no skeleton. Pages with no placeholder kind (home, auth, policy) navigate without one. The server-rendered HTML shell carries a matching critical-CSS skeleton for client-rendered pages.

### Motion
- **Ease Out Soft** (`cubic-bezier(0.22, 1, 0.36, 1)`): the house curve for reveals, meters and list entries; 200 to 300ms for UI, 500 to 700ms for progress meters filling.
- **Pop** (260ms, scale 0.92 to 1 on Ease Out Soft): confirmation glyphs such as "copied" checks.
- **Pulse Glow** (2.8s opacity 1 to 0.55): the current step of a repair progress track only.
- **Reduced motion:** under `prefers-reduced-motion: reduce` all animation and transition durations collapse to 0.01ms, iterations to one, and smooth scrolling is off.

### Email and PDF Brand
- **Email:** 600px column on Daylight Page (#f4f6f8), a white card with a 1px hairline and 16px radius padded 36px by 40px (edge-to-edge on phones), a 30px blue rounded-square "T" mark beside the store name, 27px/700 heading, 15.5px/1.65 body, a 10px-radius blue button (14px by 28px, 15px/600 white text), key-value wells on a grey 12px-radius panel and a blue progress meter for plans. Brand hex #1957d2 with ok #0b7643, warn #975800, bad #c2181d. A full dark palette (#131417 page, #1b1d21 card, #669bff blue with near-black button text) applies via `prefers-color-scheme` and Outlook's `[data-ogsc]`. Font stack: Geist, Inter, then system UI.
- **PDF receipts and statements:** A4, 44pt by 40pt margins, set in Lato (bundled with QuestPDF so output is identical on every host; this is the one sanctioned departure from Geist). 9.5pt/1.35 body, a 28pt blue rounded "T" mark, document title right-aligned in blue, soft grey table head cells, hairline row rules, the total on a pale blue (#e4ecf9) 6pt-radius band with the amount in blue, status pills on tinted backgrounds matching the email tones.

## Do's and Don'ts

### Do:
- **Do** keep the accent singular: Counter Blue (`oklch(0.5 0.2 262)`, dark `oklch(0.7 0.16 262)`) for actions, links, focus and current place.
- **Do** give every touch target 44px under `pointer: coarse`, including compact and icon buttons and table-card links.
- **Do** render every status through the status badge: tinted pill, dot and words.
- **Do** describe an installment order as "On plan · N of M paid" everywhere it appears, and state the per-installment amount, cadence and fee next to any price that offers a plan.
- **Do** use tabular figures for all money and counts.
- **Do** separate cards from the page by tone and a hairline ring; save the Raised shadow for floating and hovered elements.
- **Do** show the destination's skeleton the moment a navigation starts, and keep phone CTAs pinned above the safe-area inset.
- **Do** honour reduced motion globally and keep transitions on Ease Out Soft.
- **Do** keep emails and PDFs on the same neutral-plus-blue palette as the app.

### Don't:
- **Don't** use green, amber or red as decoration or as a secondary brand colour.
- **Don't** convey status by colour alone; the label always renders.
- **Don't** show a plan order as a bare "Paid" while installments are outstanding, or a cancelled unpaid order as "Unpaid".
- **Don't** use native date or select inputs.
- **Don't** set money in a monospace face.
- **Don't** fill a pill or panel with a state colour at full strength.
- **Don't** add uppercase tracked kickers or eyebrows above headings, in the app or in email.
