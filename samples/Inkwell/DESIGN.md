---
name: Inkwell
description: A calm, literary room for reading and writing essays; paper and ink with one warm accent.
colors:
  laid-paper: "oklch(0.985 0.006 85)"
  bright-sheet: "oklch(0.992 0.004 85)"
  iron-gall-ink: "oklch(0.22 0.012 65)"
  pressed-ink: "oklch(0.24 0.012 65)"
  kiln-rust: "oklch(0.52 0.15 38)"
  kiln-rust-on: "oklch(0.99 0.004 60)"
  margin-wash: "oklch(0.955 0.008 85)"
  thumbed-page: "oklch(0.945 0.01 85)"
  pencil-grey: "oklch(0.5 0.014 65)"
  rule-line: "oklch(0.905 0.01 85)"
  field-edge: "oklch(0.87 0.01 85)"
  correction-red: "oklch(0.52 0.2 27)"
  lamplit-desk: "oklch(0.175 0.008 65)"
  lamplit-sheet: "oklch(0.2 0.008 65)"
  chalk-ink: "oklch(0.93 0.008 85)"
  ember-glow: "oklch(0.74 0.12 45)"
typography:
  display:
    fontFamily: "Newsreader Variable, Newsreader Fallback, ui-serif, Georgia, Times New Roman, serif"
    fontSize: "clamp(2.2rem, 6vw, 3.75rem)"
    fontWeight: 600
    lineHeight: 1.05
    letterSpacing: "-0.03em"
  headline:
    fontFamily: "Newsreader Variable, Newsreader Fallback, ui-serif, Georgia, Times New Roman, serif"
    fontSize: "2rem"
    fontWeight: 600
    lineHeight: 1.25
    letterSpacing: "-0.02em"
  title:
    fontFamily: "Newsreader Variable, Newsreader Fallback, ui-serif, Georgia, Times New Roman, serif"
    fontSize: "1.45rem"
    fontWeight: 600
    lineHeight: 1.25
    letterSpacing: "-0.015em"
  reading:
    fontFamily: "Newsreader Variable, Newsreader Fallback, ui-serif, Georgia, Times New Roman, serif"
    fontSize: "1.25rem"
    fontWeight: 400
    lineHeight: 1.7
  body:
    fontFamily: "Geist Variable, Geist Fallback, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "Geist Variable, Geist Fallback, ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif"
    fontSize: "13px"
    fontWeight: 500
    lineHeight: 1.4
rounded:
  sm: "4px"
  md: "6px"
  lg: "8px"
  xl: "12px"
  container: "16px"
  hero: "24px"
  full: "9999px"
spacing:
  gutter: "16px"
  gutter-wide: "24px"
  row: "28px"
  page-top: "32px"
  page-top-wide: "56px"
  reading-column: "44rem"
  wide-column: "64rem"
components:
  button-primary:
    backgroundColor: "{colors.pressed-ink}"
    textColor: "{colors.laid-paper}"
    rounded: "{rounded.full}"
    padding: "0 16px"
    height: "36px"
  button-form:
    backgroundColor: "{colors.pressed-ink}"
    textColor: "{colors.laid-paper}"
    rounded: "{rounded.md}"
    padding: "8px 16px"
    height: "36px"
  button-outline:
    backgroundColor: "{colors.laid-paper}"
    textColor: "{colors.iron-gall-ink}"
    rounded: "{rounded.md}"
    padding: "8px 16px"
    height: "36px"
  button-ghost-hover:
    backgroundColor: "{colors.thumbed-page}"
    textColor: "{colors.pressed-ink}"
    rounded: "{rounded.md}"
  action-social:
    textColor: "{colors.pencil-grey}"
    rounded: "{rounded.full}"
    padding: "0 10px"
    height: "40px"
  action-social-liked:
    textColor: "{colors.kiln-rust}"
  input:
    backgroundColor: "transparent"
    textColor: "{colors.iron-gall-ink}"
    rounded: "{rounded.md}"
    padding: "4px 12px"
    height: "36px"
  input-search:
    backgroundColor: "{colors.margin-wash}"
    textColor: "{colors.iron-gall-ink}"
    rounded: "{rounded.full}"
    height: "36px"
  chip-topic:
    backgroundColor: "{colors.laid-paper}"
    textColor: "{colors.pencil-grey}"
    rounded: "{rounded.full}"
    padding: "0 14px"
    height: "36px"
  chip-topic-active:
    backgroundColor: "{colors.iron-gall-ink}"
    textColor: "{colors.laid-paper}"
  card:
    backgroundColor: "{colors.bright-sheet}"
    textColor: "{colors.iron-gall-ink}"
    rounded: "{rounded.container}"
    padding: "20px"
  post-row:
    typography: "{typography.title}"
    rounded: "{rounded.container}"
    padding: "28px 12px"
---

# Design System: Inkwell

> The North Star, colour names and component philosophy below were inferred from the shipped code and PRODUCT.md, not confirmed with the owner.

## Overview

**Creative North Star: "The Reading Room"**

Inkwell is a quiet room with good paper on the desk. Every surface is a warm off-white sheet, every word is near-black ink, and a single kiln-rust accent appears only where something is yours, live, or worth a second look. The essay is the furniture: serif headlines and a 1.25rem serif reading column carry the identity, while a neutral grotesque handles the controls around them so the chrome never competes with the prose.

Density is generous and editorial rather than app-like. The feed reads like a table of contents (byline, serif title, two-line dek, quiet meta row) separated by hairline rules instead of boxed cards. Containers, when they appear, are flat bordered sheets with soft 16px corners. Depth comes from tone (paper, slightly brighter sheet, translucent sticky glass) rather than shadow. Dark mode is the same room after dusk: a warm lamplit brown-black, chalk-coloured ink and the accent lifted to a glowing ember.

Motion is small and causal: a heart pops when you like, a count ticks, a bell rings, pages rise 6px into place. Everything collapses to instant under reduced motion.

**Key Characteristics:**
- Warm paper neutrals (hue 65–85) with one terracotta accent; no secondary brand colour.
- Newsreader serif for every headline and the reading body; Geist sans for UI.
- Primary actions are pressed ink, not brand colour.
- Flat, bordered, tonal surfaces; shadows only on floating layers.
- Pills for navigation, chips and calls to action; gentle 6px corners for form controls.
- 44px touch targets and a fixed phone tab bar below 1024px.

## Colors

A paper-and-ink neutral range of very low chroma, warmed toward yellow-brown, with one terracotta accent used sparingly.

### Primary
- **Kiln Rust** (`kiln-rust`): the single brand accent. Focus rings, the reading-progress bar, the page-load progress bar, a liked heart, unread dots and badges, inline text links in prose (as underline colour), blockquote rules, search-hit highlights (at 20%), selection (at 22%), the `paper` hero wash (6–14%), and positive trends in analytics. Text on it uses **Kiln Rust On** (`kiln-rust-on`).
- **Ember Glow** (`ember-glow`): Kiln Rust in dark mode; lighter and less saturated so it reads on the lamplit ground.

### Neutral
- **Laid Paper** (`laid-paper`): the page background and the text colour on ink buttons.
- **Bright Sheet** (`bright-sheet`): bordered containers (author card, sidebar call to write, analytics panels); a hair brighter than the page.
- **Iron-Gall Ink** (`iron-gall-ink`): body and heading text; also the active topic chip and the phone Write button.
- **Pressed Ink** (`pressed-ink`): the fill of primary buttons and checked switches.
- **Margin Wash** (`margin-wash`): muted and secondary fills: the search pill, topic tags in feed rows, inline code, the unverified-email strip, skeletons.
- **Thumbed Page** (`thumbed-page`): hover fill for ghost buttons, social actions and feed rows (at 45%).
- **Pencil Grey** (`pencil-grey`): secondary text: bylines, dates, deks, inactive tabs and nav, placeholders.
- **Rule Line** (`rule-line`): every border and the hairline between feed rows.
- **Field Edge** (`field-edge`): input borders and the unchecked switch track.
- **Correction Red** (`correction-red`): destructive buttons and invalid-field borders only.
- **Lamplit Desk / Lamplit Sheet / Chalk Ink** (`lamplit-desk`, `lamplit-sheet`, `chalk-ink`): the dark-mode page, container and text. In dark mode borders and inputs become translucent paper (11% and 16% white-warm) rather than solid greys.

### Named Rules
**The One Accent Rule.** Kiln Rust is the only chromatic colour in the interface. Social actions rest in Pencil Grey and turn Iron-Gall Ink when active; only a like turns rust. Trend chips use rust for up and ink for down. If a second hue seems necessary, use ink weight or a tint of rust instead.

**The Ink Not Rust Rule.** Buttons that commit an action (Write, Sign up, Save, Start writing) are Pressed Ink on Laid Paper. Rust marks state and attention, not the primary verb.

**Email expression.** Transactional email renders the same room in hex for mail clients: paper #f6f2ea, card #fffdf9, ink #2a241f, muted #6e655c, line #e8e1d5, brand #a64a22, with a dark-mode override. Email buttons are rust pills, the one place the call to action takes the accent; keep that divergence inside email.

## Typography

**Display Font:** Newsreader Variable (with a metric-matched Times New Roman fallback, then Georgia)
**Body Font:** Geist Variable (with a metric-matched Arial fallback, then system-ui)
**Label/Mono Font:** ui-monospace / Cascadia Code, only for code in prose

**Character:** A bookish optical-size serif for anything that is read, against a calm modern grotesque for anything that is operated. The serif is always semibold and tightly tracked when it is a heading, regular and open when it is a body.

### Hierarchy
- **Display** (600, 2.2rem rising to 3.75rem at 640px, 1.05, −0.03em): the signed-out landing hero headline only.
- **Wordmark** (600, 1.65rem, 1, −0.03em, serif): the "Inkwell" logotype in the header and footer.
- **Headline** (600, 2rem rising to 2.25rem, 1.25, −0.02em, balanced): page titles via the shared page header; the composer title field grows to 3rem.
- **Title** (600, 1.45rem rising to 1.65rem, 1.25, −0.015em, balanced): essay titles in feed rows; underlined in 60% rust on row hover. Card titles use the serif at 1.25rem.
- **Reading** (400, 1.25rem, 1.7, serif, optical sizing auto): essay bodies in a column capped by the 44rem layout. Prose headings step 1.9 / 1.6 / 1.3 / 1.1rem; blockquotes go italic at 1.12em behind a 3px rust rule; `hr` renders as a centred "· · ·".
- **Body** (400, 0.875rem UI / 1rem descriptions, 15.5px for deks with relaxed leading, max 62ch): controls, descriptions, dek copy.
- **Label** (500, 13px): bylines, dates, repost lines and counts; tab-bar labels drop to 11px. Counts use tabular numerals.

### Named Rules
**The Serif Reads, Sans Operates Rule.** Anything a reader reads as content (essay titles, page titles, essay bodies, author names on cards, the wordmark) is Newsreader. Anything a reader clicks or scans as metadata is Geist.

## Layout

A single centred column. Reading pages sit in a 44rem column; wide pages (feed with sidebar, analytics, profiles) widen to 64rem. Gutters are 16px on phones and 24px from 640px. Pages start 32px below the header on phones and 56px from 1024px. The sticky header is 56px tall, 64px from 1024px.

Below 1024px the header collapses to wordmark, search icon, bell and avatar, and a fixed bottom tab bar (56px rows, safe-area padded) carries navigation; bottom padding on `main` (112px) and `scroll-padding-bottom` keep content clear of it. Compose pages hide the tab bar so the sticky save bar owns the bottom edge. From 1024px the inline nav, search field and footer appear, and the feed gains a sticky sidebar.

Feed rows breathe at 28px vertical padding with hairline separators; containers use 16–24px internal padding. Spacing otherwise follows Tailwind's 4px grid, with 6px (`gap-1.5`) common inside clusters.

## Elevation & Depth

Inkwell is flat by default and layers by tone. Page, sheet and wash differ by a few points of lightness, and borders do the separating. Sticky chrome (header, phone tab bar, compose save bar) uses translucent paper (85–92%) with a medium backdrop blur, so content scrolls visibly beneath it. Shadows are reserved for things that float above the page.

### Shadow Vocabulary
- **Hairline lift** (`box-shadow: 0 1px 2px 0 rgb(0 0 0 / 0.05)`): outline buttons, inputs, switches.
- **Raised pill** (`box-shadow: 0 1px 3px 0 rgb(0 0 0 / 0.1), 0 1px 2px -1px rgb(0 0 0 / 0.1)`): the header Write button, the selected segment in segmented toggles, KPI card hover, the essay action bar on phones.
- **Floating layer** (`box-shadow: 0 4px 6px -1px rgb(0 0 0 / 0.1), 0 2px 4px -2px rgb(0 0 0 / 0.1)` for popovers and menus; `0 10px 15px -3px rgb(0 0 0 / 0.1), 0 4px 6px -4px rgb(0 0 0 / 0.1)` for dialogs, sheets and the desktop sticky essay action bar).
- **Search halo** (`box-shadow: 0 0 0 4px color-mix(in oklab, var(--ring) 14%, transparent)`): the focused header search pill.

### Named Rules
**The Flat Sheet Rule.** Containers on the page are bordered sheets with no resting shadow. A shadow means the element floats over content or is responding to the pointer.

## Shapes

Corners scale with the size and role of the surface, from one `--radius` base of 8px: 4px for inline marks, 6px for form controls and form buttons, 8px for images, code blocks and dialogs, 12px for the response textarea and icon tiles, 16px for bordered containers and feed-row hover fields, 24px for the `paper` hero blocks (landing hero, profile header). Navigation links, topic chips, search, social actions, badges, avatars and calls to action are full pills. Borders are 1px Rule Line everywhere; the only thicker rules are the 3px blockquote bar and the 2px active-tab underline.

## Components

### Buttons
Quiet, inky and tactile; they press in rather than glow.
- **Shape:** pill (9999px) for navigational calls to action in the header, hero and sidebar; gently rounded (6px) for buttons inside forms and dialogs.
- **Primary:** Pressed Ink fill, Laid Paper text, 14px medium, 36px tall (44px for hero CTAs), 16–24px horizontal padding.
- **Hover / Focus:** fill drops to 90% on hover; focus shows a 3px ring of Kiln Rust at 50%; pills scale to 0.97–0.98 on press; trailing arrows nudge 2px and the pen icon tilts −12° on hover.
- **Outline:** Laid Paper fill, Rule Line border, hairline lift; hover fills Thumbed Page.
- **Ghost:** no fill until hover (Thumbed Page). Header icon buttons are 44px on touch layouts.
- **Destructive:** Correction Red fill with white text; dialogs only.

### Chips
- **Style:** topic chips are 36px pills with a Rule Line border on Laid Paper and Pencil Grey text, optionally with a tabular count. Topic tags inside feed rows are smaller Margin Wash pills (12px text) that invert to ink on hover.
- **State:** active chip inverts to Iron-Gall Ink fill with paper text and a clear (×) icon; press scales to 0.95.

### Cards / Containers
- **Corner Style:** 16px.
- **Background:** Bright Sheet.
- **Shadow Strategy:** none at rest (see The Flat Sheet Rule).
- **Border:** 1px Rule Line.
- **Internal Padding:** 16–24px.
- **Paper hero variant:** 24px corners, a border, and the `paper` wash (two radial gradients of Kiln Rust at 14% and 6%) behind the landing hero and profile header.

### Inputs / Fields
- **Style:** 36px, 6px corners, 1px Field Edge border, transparent fill, hairline lift; dark mode fills with 30% input tint. Textareas auto-grow with content.
- **Focus:** border turns Kiln Rust with a 3px rust ring at 50%.
- **Error / Disabled:** invalid fields take a Correction Red border and 20% red ring; disabled drops to 50% opacity.
- **Search:** a borderless Margin Wash pill with a leading icon and a "/" key hint; on focus it turns to paper with a rust-tinted halo.
- **Composer:** the essay editor removes all field chrome: a borderless serif title (2.25–3rem) and a borderless 1.25rem serif body, so writing looks like the published page.

### Navigation
- **Header:** sticky translucent paper bar with the serif wordmark. Desktop links are 14px Pencil Grey pills; hover fills Margin Wash at 70%; active is Margin Wash with medium-weight ink.
- **Link tabs:** 15px text in a row over a Rule Line border, 44px tall; the active tab gets a 2px ink underline that scales in from the centre over 300ms.
- **Phone tab bar:** fixed, translucent, five icon-over-label tabs (11px labels, icons 1.75 stroke rising to 2.25 when active), a 2px ink bar scaling in at the top edge of the active tab, and Write as a 36px solid ink circle in the middle.

### Essay Row (signature)
The feed is an index, not a card grid. Each row: 13px byline (20px avatar, name, middle dot, date), a serif title, a two-line Pencil Grey dek at 15.5px capped at 62ch, an optional 80px (144×96 from 640px) cover thumbnail at 8px corners, then a meta row of up to two topic tags and the social actions. Rows are separated by an inset hairline; on hover the row fills Thumbed Page at 45% inside 16px corners, the hairline fades, the title underlines in rust, and the thumbnail scales to 1.03 over 500ms. The whole row is one link target.

### Social Actions
40px pill buttons with an 18px icon (1.75 stroke) and a 13px tabular count. They rest in Pencil Grey, hover to Thumbed Page and ink, and when active fill their icon: a like turns Kiln Rust, a repost or bookmark turns full ink. The icon pops (scale 1.32, overshoot easing, 380ms) only after a click; counts tick in from above.

### Sticky Save Bar
On compose pages the bottom edge belongs to a sticky bar of translucent paper (90%) with a top border and backdrop blur, safe-area padded, holding the publish-state segmented toggle (selected segment raised on paper) and the save actions.

## Do's and Don'ts

### Do:
- **Do** set every headline, essay title and essay body in Newsreader at weight 600 with negative tracking (−0.015em to −0.03em) for headings.
- **Do** make primary buttons Pressed Ink on Laid Paper, as pills when they navigate.
- **Do** keep Kiln Rust for state and attention: focus rings, unread markers, a liked heart, progress, link underlines, highlights.
- **Do** separate list items with inset 1px Rule Line hairlines and a 45% Thumbed Page hover field, not boxed cards.
- **Do** give bordered containers 16px corners, Bright Sheet fill and no resting shadow.
- **Do** keep touch targets at 44px or more on layouts below 1024px and hand navigation to the bottom tab bar.
- **Do** wrap every animation in the reduced-motion override; keep motion tied to a state change.

### Don't:
- **Don't** introduce a second chromatic hue for social actions, charts or status; a like is rust, everything else is ink.
- **Don't** use the serif for buttons, inputs, chips or metadata.
- **Don't** put resting shadows on cards or rows; shadows belong to floating layers.
- **Don't** use cool greys; every neutral carries the warm 65–85 hue.
- **Don't** fill a primary call to action with Kiln Rust in the app.
