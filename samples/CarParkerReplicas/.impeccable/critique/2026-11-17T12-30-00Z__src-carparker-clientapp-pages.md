---
target: CarParker ClientApp pages (round 1)
total_score: 26
max_score: 40
na_heuristics: 
p0_count: 2
p1_count: 12
target_identity: "file:C:\\Users\\adzam\\Documents\\Work\\dotnet-inertia-samples\\samples\\CarParker\\src\\CarParker\\ClientApp\\pages"
timestamp: 2026-11-17T12-30-00Z
slug: src-carparker-clientapp-pages
---
# CarParker critique, round 1

Customer journey first, then guard and admin, at phone width (iPhone 15) with desktop checks. Assessment A (design
review) and Assessment B (detector + browser evidence) merged. Nothing was submitted.

Score: 26/40 (A). Visibility 3, real-world match 2, control 3, consistency 2, error prevention 2, recognition 3,
efficiency 3, minimalism 3, recovery 3, help 2. It feels specific to the product (bay plan, plate inputs, signage
numerals); it slips on copy and data plumbing.

## Detector
Source scan: 0 findings. Live scan of public pages: step numerals on Home at 1.5-1.7:1 (real); kicker-above-heading x3
and an eyebrow chip on Home (design call); repeating stripes texture (deliberate); no font false positives.

## Browser evidence
25 routes at 393 px: one h1 each, no NaN/undefined text, no console errors or CSP violations. Skeleton on the tap in
9-30 ms; pages arrive in 110-660 ms. CLS 0.40 on /lots and the lot page in dev (unstyled SSR HTML before Vite CSS
loads; re-measure on a production build). /account is client-rendered: LCP about 1.4-1.8 s behind the boot shell.

## P0
1. /admin/team overflows to 452 px on phones: the member grid has no single-column template (`admin/Team.svelte:64`).
2. Every DataTable row link is a plain `<a>`: "View details" does a full page reload, losing app state and the
   skeleton (`components/DataTable.svelte:84,103`).

## P1
1. Bay map shows bays free now, not free for the chosen window; a clash surfaces only as a 409 (`BookingFlow.svelte`).
2. Drivers see only UTC times ("Arrive (UTC)", "Open 24 hours · UTC"); lots sit in New York but are seeded as UTC.
3. Refund rule cut to "24 h or more" before paying; half and no-refund tiers missing; nothing in the phone sticky bar.
4. Spot Cover on by default and the step shows done before it was seen.
5. Cancel dialog talks about "the matrix" instead of the amount coming back.
6. Guards and admins land on the customer lots page after login.
7. Booking detail: no hours for singles, "Span: —" and no weekdays for weekly bookings.
8. Status badges ("Confirmed", "Guard") 3.95:1 at 12 px; avatar initials 3.8:1; "02" chip 3.6:1.
9. Insurance decision note input has no label (`admin/Insurance.svelte:78`).
10. Notification badge 2.9:1 in dark mode, 10 px.
11. Bottom-tab labels 11 px on every account and admin page.
12. Home step numerals 1.5:1.

## P2
"Materialized windows" jargon; parked card stays green while overstay alerts fire, no way to call the gate; refund
receipt offered while the refund is only requested and payments still say "Paid"; Reserve shown twice on the phone
booking page; bookings list mixes past and upcoming, sorted by creation, about 1.3 rows per screen; "View details ↗"
reads as external; header icons 36 px, footer links 20 px, many 24-32 px secondary actions (alerts Open, account Read,
vehicle remove/default, guard Resolve, admin lot links 15 px high); guard plate placeholder looks like a real plate;
"deck" and "lot" mixed in admin.

## Minor
Lot page 14-day chart clipped and low information; "When" step checked before any choice; analytics skeletons give no
caption; preferences save model unclear; booking detail back link only at the bottom; double space in "starts  in".
