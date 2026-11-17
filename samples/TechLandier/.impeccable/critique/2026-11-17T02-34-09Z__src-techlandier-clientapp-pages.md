---
target: TechLandier ClientApp pages (round 1)
total_score: 25
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 3
target_identity: "file:C:\\Users\\adzam\\Documents\\Work\\dotnet-inertia-samples\\samples\\TechLandier\\src\\TechLandier\\ClientApp\\pages"
timestamp: 2026-11-17T02-34-09Z
slug: src-techlandier-clientapp-pages
---
Method: dual-agent (A: design review, opus · B: detector + browser evidence, sonnet). Round 1 of 2; target: TechLandier ClientApp (store, account, admin) at 393 px and 1440 px.

## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|-----------|-------|-----------|
| 1 | Visibility of System Status | 2 | BNPL order confirmation reads "Paid · Paid", total without the plan; no schedule |
| 2 | Match System / Real World | 2 | Every future installment badged amber "Due"; "Due and overdue" shows the whole balance; raw enum filter chips |
| 3 | User Control and Freedom | 3 | Steps revisitable; Back on phone replaces the total |
| 4 | Consistency and Standards | 2 | Three different "was" prices for one product; three date formats |
| 5 | Error Prevention | 2 | One-tap "Pay now" on every row with no confirm; tapping #6 pays #1 |
| 6 | Recognition Rather Than Recall | 2 | Phone checkout hides the total on payment and review steps |
| 7 | Flexibility and Efficiency | 3 | Ctrl K, quick-nav, prefetch; no bulk actions on admin orders |
| 8 | Aesthetic and Minimalist Design | 3 | Store-hours card heads checkout; column of solid "Reassign" buttons |
| 9 | Error Recovery | 3 | Step-1 errors render off-screen, focus stays on Continue |
| 10 | Help and Documentation | 3 | No "what if I miss a payment" at commitment |
| **Total** | | **25/40** | **Acceptable** |

## Design Specificity Verdict

LLM: partly specific. Checkout and BNPL mechanics are product-grounded (GhanaPost GPS, zone ETA cards, plan picker with fee in words, delivery board language). The shell is generic: the home hero (dark green + lime, "Better tech. More possibility.") contradicts the blue brand and could sell anything; account/admin are stock sidebar + stat cards. The plan schedule, the most product-specific object, never appears where the decision is made.

Deterministic scan: 3 CLI findings in app.css: overused-font x2 (Geist/Inter fallback @font-face; false positive, brand choice in PRODUCT.md), bounce-easing x1 (`--ease-spring` on the pop animation; real). Overlay (injected via CDP CSP bypass): 10px brand label on ProductArt tiles (undersized + 2.8:1 contrast on pastel tiles), a straggler word, one kicker-above-heading on home, ~93-char lines on product description. Browser: no overflow anywhere, 0 console errors, 1 h1 per page, StatusBadge success text 4.37:1 (fails 4.5), admin orders search input unlabeled, many 40 px controls and inline 20 px links under 44 px on phone, desktop dev CLS 1.2-1.7 on store pages (header wraps before CSS; to confirm on a production build), /account CLS 0.12-0.23 from a late block. Navigation skeletons appear in 4-11 ms on every client navigation; drawers trap focus, close on Escape and return focus.

## Overall Impression

The plumbing is good (skeletons, drawers, no overflow, live boards). The money story is not: the moments that decide trust (confirmation, review step, installment list) misstate or hide what the customer owes.

## What's Working

- Buy-box plan picker: per-payment amounts, fee in words, plain eligibility copy.
- Real-world checkout details: GPS address, zone ETA, pay-on-delivery disabled with the reason.
- Admin operational language: failed-attempt chips, cash to collect, SLA states, Ctrl+Enter to send.

## Priority Issues

- [P0] Confirmation hides the installment plan and calls a BNPL order "Paid" (orders/Track.vue, account/Order.vue, admin orders list). Fix: plan block with schedule and first due date, "On plan · 0/6 paid" badge, tracking-link note only for signed-out token views, plan marker in admin list. /impeccable clarify
- [P1] Place order without total, fee or first due date in view on phone (products/Buy.vue review step + sticky bar). Fix: total on every step, plan total incl. fee and first payment date in review. /impeccable clarify
- [P1] Contradictory prices on product page (PriceTag.vue, BuyBox.vue, insurance premium base). Fix: one reference price for strike, percent and saving; insurance on the price paid. /impeccable harden
- [P1] Phone sticky bar shows the 12-payment "from" line after a 6-payment plan is chosen (BuyBox.vue:119); "from $X" hides the fee everywhere. /impeccable clarify
- [P2] Installment management alarms and lets users pay by mistake (InstallmentPlanCard.vue, account/Credit): upcoming rows as "Due", "Due and overdue" for the full balance, one-tap pay without confirm. /impeccable harden
- [P2] Admin numbers disagree (overview vs analytics "Financed", riders needed, SLA overdue while waiting on customer). /impeccable clarify

## Persona Red Flags

Casey: no Log in in the phone header; store-hours card eats checkout's first screen; step-1 errors off-screen; no total at Place order. Jordan: never sees the first due date; sees "Paid"; six amber "Due" badges; review/warranty claim offered before delivery. Riley: insurance payout above price paid; preselected plan silently dropped when at limit. Alex: no bulk select, raw enum chips, BNPL invisible in orders list, Mark resolved outranks Send reply, Administration above Overview in sidebar. Sam: 18-20 px order links in mobile cards; no focus move to the first invalid field; success badge 4.37:1.

## Minor Observations

Plan cards show only "$X each"; unrounded premium label; StepIndicator future steps as disabled buttons; raw toLocaleString and "item(s)" on admin order; ISO warranty dates; recent-orders row wrap on phone; verbose admin mobile order cards; lone "…" row on delivery cards; signed-in drawer lacks Orders/Installments; seeded copy visible; home hero kicker + lime CTA off-brand; ProductArt 10 px label.

## Questions to Consider

- What if the plan schedule were the product, shown as a dated timeline at the moment of choice?
- Should a BNPL order ever display "Paid"?
- Does a one-item phone checkout need three steps?
