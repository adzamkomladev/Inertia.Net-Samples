# Product

<!-- impeccable:product-schema 1 -->

> Written from repository evidence (README, docs/CONTRACT.md, docs/USER_GUIDE.md, docs/SOCIAL_FEATURES.md) during the
> 2026 overhaul; facts marked *(inferred)* were not confirmed in an interview and should be corrected if wrong.

## Platform

web

## Users

- **Readers** browse the public latest feed and essay pages, often on a phone, often signed out, arriving from a
  shared link. Their job: read one essay comfortably, then maybe find the next one.
- **Writers** (signed-in, verified) draft and publish markdown essays with a cover and up to five topics, then check
  how they read through the analytics dashboard. Writing happens mostly on desktop; checking stats and replying to
  responses happens on phones too *(inferred)*.
- **Administrators** manage the team (grant and remove admin) and occasionally watch background jobs on the Runwick
  dashboard, sometimes from a phone.

## Product Purpose

Inkwell is a multi-user publishing platform: write essays in markdown, follow writers, get a personal feed
(Latest, For you, Following), and see how posts read. It is also the reference sample for Inertia.Net with React and
SSR on SQLite, so every feature doubles as a demonstration of an Inertia.Net capability done properly.

Success: an essay page is readable and fast on a phone from a cold link; a writer can go from idea to published post
without friction; the app runs cheaply in one container.

## Positioning

A calm, literary reading and writing room rather than a social feed: essays first, social signals (likes, reposts,
responses) kept secondary. It runs as one small container on SQLite with server-rendered public pages, which a
heavier publishing stack cannot match on cost.

## Operating Context

- Public pages (latest feed, essay pages) are server-rendered and cached; private pages render on the client.
- Notifications arrive live (SignalR) in a bell panel; email covers verification, responses, batched likes, a weekly
  writer digest and daily recommendations, each opt-out with one-click unsubscribe.
- Demo data: 5,000 users and 40,000 posts (`--seed-demo`); demo accounts `inertia.demo42.NNNNN@yopmail.com`.
- Live demo at inkwell.temtem.dev.

## Capabilities and Constraints

- Accounts with email verification and password reset; Precognition-validated forms.
- Markdown editor with drafts, cover images (S3-compatible storage) and up to five topics.
- Feeds with keyset infinite scroll; people directory and profiles; likes, reposts, bookmarks, archives, threaded
  responses; SQLite FTS5 search; cookie-free page-view analytics.
- One container, SQLite, Runwick for all background jobs. The sample stays standalone (no shared code with the other
  samples). Bun, Valibot, shadcn-style local components; no native date or select inputs.

## Brand Commitments

- Name: **Inkwell**. Editorial personality: paper and ink, serif headlines, a single warm brand accent.
- Voice: plain, warm, brief; no hype. Errors say what happened and what to do next.

## Evidence on Hand

- Real demo content from the seeder (generated essays, bios, topics). No real testimonials, customers or usage
  numbers exist; do not invent them.

## Product Principles

1. Reading comes first: the essay, its type and its pace outrank every social control.
2. Cost is a feature: a visit should touch the database only for what the reader actually sees.
3. Quiet over clever: motion and decoration only where they clarify state.
4. Phone parity: every reader and writer task works one-handed at 360 px.

## Accessibility & Inclusion

WCAG 2.2 AA: 44 px touch targets on touch layouts, visible focus, reduced motion respected, one h1 per page.
