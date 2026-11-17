---
target: TechLandier ClientApp pages (round 2)
total_score: 25
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 3
target_identity: "file:C:\\Users\\adzam\\Documents\\Work\\dotnet-inertia-samples\\samples\\TechLandier\\src\\TechLandier\\ClientApp\\pages"
timestamp: 2026-11-17T03-27-52Z
slug: src-techlandier-clientapp-pages
---
# TechLandier critique, round 2 (final)

Whole app at phone width, plus desktop checks. Assessment A (design review) and Assessment B (detector + browser evidence) merged.

Score: 25/40. No P0s. 3 P1s.

## Detector
`impeccable detect` returned 0 findings. The overlay flagged Geist as an overused font (false positive: brand font, already in the config ignores), a 10px notification count badge, and 11px secondary lines on admin orders.

## Browser evidence
- No horizontal overflow, one h1 per page, no unlabelled controls, no NaN or undefined text, no console errors, at 393px and 1440px.
- Contrast passes everywhere. "Place order" measured 4.48:1 once on desktop, probably mid-transition.
- Skeleton appears 5-15ms after the click on every navigation measured.
- Sticky totals, product plan bar, installment pay confirm, insurance list and all three drawers (keyboard) pass.
- Phone CLS 0.216 on /account. Desktop CLS on store pages is a dev-mode stylesheet artifact.
- Small targets: brand link (116x28), "Back to the store" (93x24), product breadcrumbs (20px high), order phone link (18px), admin credit "Any" filter (41px wide).

## P1
1. Product page total leaves out insurance, and does not say delivery comes later (`BuyBox.vue`).
2. Staff see BNPL orders as "Paid"; three different plan labels in use (`admin/Order.vue`, `admin/Orders.vue`).
3. Plan cards on /account/credit don't name the order or product; waived installments count as paid, so cancelled plans read 100%; past plans aren't collapsed (`Credit.vue`, `InstallmentPlanCard.vue`).

## P2
- Home Support card links to `/support` (POST only, 405); both footer help links go to `/support/policy`; drawer has no Support entry.
- Admin wording: rider vs driver, raw "delivered" enum in a select, "Instalments", catalog shows the list price instead of the selling price, "Arrears" counts not-yet-due installments, customer column mixes recipient name with account email, "Delivered · Delivered", inconsistent filters.

## Minor
Sticky bar covers the h1 on first load; 44x24 preference switches; future seeded dates; description copy contradicts specs; cancelled orders show "Unpaid"; duplicate spec block; quantity stepper live on review step; lowercase "reviewed"; admin discount without a minus sign; 8 large delivery-zone cards; lower tab rows clipped on phones; 10px count badge; 11px admin text.
