# Product

<!-- impeccable:product-schema 1 -->

> Written from repository evidence (README, docs/CONTRACT.md, docs/USER_GUIDE.md, the seeder and the pages) during the
> 2026 overhaul; facts marked *(inferred)* were not confirmed in an interview and should be corrected if wrong.

## Platform

web

## Users

- **Shoppers** browse phones, laptops and accessories, mostly on a phone *(inferred from the Accra address, mobile
  money and rider delivery)*, often signed out. Their job: find a device, trust the price and the delivery estimate,
  and pay in full or split it into installments.
- **Customers** (signed in) track orders, pay installments, file warranty or insurance claims, follow repairs and
  support tickets, and download receipts and BNPL statements.
- **Store staff and admins** run the catalog and stock, move orders through fulfilment, assign riders on the delivery
  board, answer support, decide care claims and watch sales. Mostly desktop at the counter; on a phone on the shop
  floor or on the road *(inferred)*.

## Product Purpose

TechLandier is an electronics store with buy-now-pay-later: gadgets today, paid over time in 3, 6 or 12 installments.
It is also the reference sample for Inertia.Net with Vue, SSR and PostgreSQL, so every feature doubles as a
demonstration of an Inertia.Net capability done properly.

Success: a shopper on a phone goes from product to placed order without hesitation and understands exactly what an
installment plan costs; staff can run the day's orders and deliveries from any screen.

## Positioning

A trustworthy local electronics shop rather than a marketplace: one curated catalog, clear prices with the plan fee
shown up front, rider delivery with honest ETAs, and after-sale care (warranty, insurance, repairs) in the same account.

## Operating Context

- Home, product, deals and catalog pages are server-rendered and cached; account and admin pages render on the client.
- Order, delivery and alert changes arrive live (SignalR); email covers verification, order receipts, installment
  notices and support/care updates; receipts and statements download as PDF.
- Demo data: 120 products, 400 orders, 60 BNPL plans (`--seed-demo`); demo logins
  `inertia.demo42.techlandier.admin@yopmail.com` and `inertia.demo42.techlandier.customer@yopmail.com`.
- Live demo at techlandier.temtem.dev. Currency shown as `$`; the store address is in Accra.

## Capabilities and Constraints

- Catalog with stock movements, deals and compare-at prices; checkout by card, mobile money or pay on delivery.
- BNPL plans with reminders, overdue handling and weekly statements; warranties, device insurance, repair tickets;
  verified-purchase reviews; delivery zones and riders; support tickets with SLAs; sales analytics.
- One app container, self-hosted PostgreSQL (Coolify), Runwick for every background job, S3-compatible storage.
  The sample stays standalone. Bun, Valibot, shadcn-vue style local components; no native date or select inputs.

## Brand Commitments

- Name: **TechLandier**. A confident, clean tech-retail look: blue brand accent, Geist and Inter, product art front
  and centre.
- Voice: plain and reassuring about money; never pushy about credit. Costs (fee, installment amount, due dates) are
  always stated before the customer commits.

## Evidence on Hand

- Seeded demo catalog, orders, plans and reviews. No real customers, testimonials or sales figures exist; do not invent
  them.

## Product Principles

1. Money is never a surprise: totals, fees and schedules are visible before every commitment.
2. Phone first for shoppers, phone-capable for staff: every task works at 360 px.
3. Live where it matters: order and delivery boards update in place; everything else stays still.
4. Cost is a feature: a visit touches the database only for what is on screen.

## Accessibility & Inclusion

WCAG 2.2 AA: 44 px touch targets on touch layouts, visible focus, reduced motion respected, one h1 per page, status
never conveyed by colour alone.
