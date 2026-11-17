# Product

<!-- impeccable:product-schema 1 -->

> Written from repository evidence (README, docs/CONTRACT.md, docs/USER_GUIDE.md, the seeder and the pages) during the
> 2026 overhaul; facts marked *(inferred)* were not confirmed in an interview and should be corrected if wrong.

## Platform

web

## Users

- **Drivers** look for a bay near where they are going, almost always on a phone and often on the way there
  *(inferred)*. Their job: see what is free and what it costs, reserve a numbered bay for one window or every week, and
  get through the gate without fuss.
- **Customers** (signed in) manage upcoming and recurring bookings, pay, cancel for a refund by rule, claim on Spot
  Cover, read alerts and download receipts.
- **Guards** stand at the gate with a phone or a small tablet, often in poor light *(inferred)*: check plates in and
  out, see who is late or overstaying, log incidents. Speed and big targets matter more than density.
- **Admins** run lots, bays, pricing and the team; decide refunds and Spot Cover claims; watch occupancy and revenue.
  Desktop at the office, phone when on site *(inferred)*.

## Product Purpose

CarParker rents parking bays: one window or the same time every week, through the gate by plate, with optional Spot
Cover. It is also the reference sample for Inertia.Net with Svelte 5, SSR and MongoDB, so every feature doubles as a
demonstration of an Inertia.Net capability done properly.

Success: a driver on a phone goes from "where am I going" to a reserved bay in under a minute and knows exactly what
it costs and how to cancel; a guard clears a plate in two taps.

## Positioning

A calm, trustworthy operator of a handful of real car parks, not a marketplace: a numbered bay instead of a guess, live
availability, clear hourly prices with a daily cap, and refund rules stated before you pay.

## Operating Context

- Home, lots and lot pages are server-rendered and cached; account, guard and admin pages render on the client.
- Bay states, gate sessions and alerts arrive live (SignalR + live props); email covers verification, booking and
  payment receipts, refunds, no-shows and overstays; booking and refund receipts download as PDF.
- Demo data: 5 lots, about 160 bays, bookings, sessions, claims and incidents (`--seed-demo`); demo logins
  `inertia.demo42.carparker.{admin,guard,customer}@yopmail.com`, password `Password@12345`.
- Currency shown as `$`.

## Capabilities and Constraints

- Lots with levels and sections of typed bays (standard, compact, accessible, covered, EV); single and weekly
  recurring bookings (up to 8 weeks); conditional bay claims (409 on a lost race); mocked card, mobile money and
  pay-at-gate payments; refund matrix (24 h full, 4 h half, later none); Spot Cover (12%, min $2) and claims; gate
  check-in/out with overstay and no-show rules; incidents; alerts inbox; daily lot analytics.
- Several identical app replicas behind HAProxy plus MongoDB (single node or replica set), shared Redis, Runwick (PostgreSQL store) for every background job,
  S3-compatible storage. The sample stays standalone. Bun, Valibot, shadcn-svelte style local components; no native
  date or select inputs.

## Brand Commitments

- Name: **CarParker**, logo mark a teal "P" tile. Calm deep-teal primary, Archivo for text and headings, Barlow Semi
  Condensed for bay labels, plates and signage-like numbers; a car-park floor plan as the signature visual.
- Voice: short, practical, friendly ("Less circling. More arriving."). Sentence case. Money and rules stated plainly
  before every commitment.

## Evidence on Hand

- Seeded demo lots, bookings and sessions only. No real customers, testimonials or usage figures exist; do not invent
  them.

## Product Principles

1. The bay is the product: show the actual numbered space, live, before asking for anything.
2. Money and rules are never a surprise: price, cap, cover and refund rule are visible before paying.
3. Phone first for drivers and guards, phone-capable for admins: every task works at 360 px with 44 px targets.
4. Live where it matters (bay map, gate, alerts); everything else stays still.
5. Cost is a feature: a visit reads only what is on screen; background work is batched.

## Accessibility & Inclusion

WCAG 2.2 AA: 44 px touch targets, visible focus, reduced motion respected, one h1 per page, bay and status state never
conveyed by colour alone (labels and icons too), readable gate console in bright sun and at night.
