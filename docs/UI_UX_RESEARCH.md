# UI/UX Research: Refining TechLandier, CarParker and Inkwell

Research date: November 2026. This report covers the UI/UX overhaul of three Inertia.js 3 apps (React/Vue/Svelte, Tailwind 4, shadcn-style components, SSR for public pages). Every app keeps its current visual identity; the aim is refinement, not a rebrand.

How to read it: each section gives **Do** / **Don't** guidance with a source link for each claim. Items marked *(convention)* are common industry practice with no primary research source; treat them as defaults you can change. Baymard figures come from their free public articles. Their full guideline database is paywalled, so a few specifics (for example, Baymard's own position on sticky add-to-cart bars) could not be verified and are labeled as such.

---

## 1. Mobile e-commerce (TechLandier)

### Why it matters
- The average cart abandonment rate is **70.22%**. Excluding shoppers who were "just browsing", the top reasons are: extra costs too high (40%), slow delivery (20%), not trusting the site with card details (19%), forced account creation (18%), checkout too long or complex (17%), site errors (17%), and total cost not visible up front (12%). [Baymard: cart abandonment](https://baymard.com/lists/cart-abandonment-rate)
- 59% of mobile sites rate "mediocre" or worse for checkout UX (2026 benchmark). [Baymard: Checkout UX 2026](https://baymard.com/research-articles/current-state-of-checkout-ux)
- 62% of mobile product pages rate "mediocre" or worse, against 52% on desktop. [Baymard: Product Page UX 2026](https://baymard.com/research-articles/current-state-ecommerce-product-page-ux)

### Product page
- **Do** make the price large, close to heading size, and easy to find. 11% of mobile sites make the price hard to locate. [Baymard: price discounts](https://baymard.com/research-articles/product-page-price-discounts)
- **Do** show a total-cost estimate (shipping, fees) near the buy section; 67% of sites don't. **Do** show or link the return policy in the main product content, because 60% of users look for it there. [Baymard: Product Page UX 2026](https://baymard.com/research-articles/current-state-ecommerce-product-page-ux)
- **Do** use buttons, not drop-downs, for variant selection (storage size, colour), so out-of-stock options are visible before selection; 57% of sites get this wrong. [Baymard: Product Page UX 2026](https://baymard.com/research-articles/current-state-ecommerce-product-page-ux)
- **Do** provide "in scale" images, such as a phone in a hand or a laptop on a desk. 42% of users try to judge size from images. [Baymard: Product Page UX 2026](https://baymard.com/research-articles/current-state-ecommerce-product-page-ux)
- **Do** let guests save or wishlist without signing up (89% of sites don't), and reply visibly to negative reviews (89% don't). [same](https://baymard.com/research-articles/current-state-ecommerce-product-page-ux)
- **Do** state the delivery promise as a **date**, not a speed ("Arrives Thu 19 Nov", not "2-day shipping"). 48% of sites still use speeds. Keep date ranges within about 5–7 days. [Baymard: delivery date vs speed](https://baymard.com/blog/shipping-speed-vs-delivery-date)
- **Do** show order cut-off times as a live countdown ("Order within 3h 12m"); 83% of sites don't. [Baymard: Checkout UX 2026](https://baymard.com/research-articles/current-state-of-checkout-ux)
- **Sticky CTA:** A/B evidence is mixed. One test of a bottom sticky bar on a long product page saw +25% sales ([Conversion Rate Experts](https://conversion-rate-experts.com/sticky-cta-win-report/)). Another saw add-to-cart rise 6.8% while conversion *fell* 7.7% ([CleanCommit](https://cleancommit.io/ab-tests/mobile-bottom-sticky-add-to-cart-button/)). No public Baymard guideline was found. **Recommendation:** use a slim sticky bar that shows only after the in-page buy button scrolls out of view. It should show price, "from $X/mo", and the CTA. Reserve `scroll-padding-bottom` for it (see §9).

### Buy-now-pay-later presentation
- **Do** show BNPL availability prominently on the product page. Baymard saw a shopper about to abandon a high-ticket purchase until he noticed financing. [Baymard: furniture UX](https://baymard.com/blog/furniture-and-home-decor-ux-research)
- **Do** place the payment-messaging element on the product, cart **and** payment pages. Affirm-style offers vary by cart size (Pay in 30, Pay in 4, or monthly). [Stripe: Affirm](https://edge-docs.stripe.com/payments/affirm)
- **Do** include "APR" whenever you show a rate. Once you describe a specific loan, show the full terms: price or loan amount, monthly payment, term length, APR, and any down payment. [Affirm: compliant marketing](https://www.affirm.com/business/blog/marketing-compliant)
- **Do** keep the representative example within one click of the "trigger term" (for example "$41/mo"), and base it on the median or majority APR. [Klarna: US BNPL promotion rules](https://docs.klarna.com/marketing/solutions/grab-and-go/advertising-legal-guidelines/united-states/bnpl-promotion-rules-101/)
- **Do** link a disclosure that sits apart from its message with a hyperlink or a matching symbol (*, †). [Affirm: compliant marketing](https://www.affirm.com/business/blog/marketing-compliant)
- **Pattern** *(convention, modelled on Klarna and Affirm widgets)*:
  - Under the price, one line: "or 4 × $124.75, 0% interest · *Learn more*".
  - "Learn more" opens a bottom sheet with a plan picker (segmented control: 3/6/12 months). For each plan show the monthly amount, total payable, APR and first-payment date, plus a small schedule table.
  - In checkout, show the same schedule again in the order summary.
- **Don't** show only the monthly figure without the total payable and the APR one tap away. **Don't** call a plan "free" if fees apply.

### Cart and checkout
- **Do** offer guest checkout. Move "create an account" to the confirmation step, where a password is the only extra input; 56% of sites don't. [Baymard: Checkout UX 2026](https://baymard.com/research-articles/current-state-of-checkout-ux), [Baymard: confirmation page](https://baymard.com/research-articles/order-confirmation-page)
- **Do** aim for about **8 form fields** in total. The 2024 average was 11.3.
  - Use a single "Full name" field.
  - Hide "Address line 2" and "Company" behind a link.
  - Collapse the coupon field.
  - Default billing address to shipping address.
  
  [Baymard: form fields](https://baymard.com/blog/checkout-flow-average-form-fields)
- **Do** collapse completed steps into summaries with "Edit" links (accordion checkout); 72% of sites don't. **Do** mark both required *and* optional fields; 32% of testers missed a required field when only optional fields were marked. **Do** explain why a phone number is needed, since 68% of shoppers are reluctant to give one. [Baymard: Checkout UX 2026](https://baymard.com/research-articles/current-state-of-checkout-ux)
- **Do** use +/− buttons, alone or with a text field, for cart quantity; 95% of sites don't. [same](https://baymard.com/research-articles/current-state-of-checkout-ux)
- **Do** put labels above fields. **Don't** use placeholder-only labels or split inputs (phone, postcode, name). **Do** write labels that make sense on their own ("Billing phone"), because the keyboard covers about 50% of the screen. [Baymard: mobile checkout](https://baymard.com/blog/mobile-checkout)
- **Do** set the right keyboard per field with `type`, `inputmode`, `autocomplete`, and `autocapitalize`/`autocorrect` off where they don't apply. 60% of sites miss at least 2 of 5 keyboard optimizations. Ask for the postcode first and prefill city and region. [Baymard: mobile checkout](https://baymard.com/blog/mobile-checkout), [Baymard: touch keyboard cheat sheet](https://baymard.com/labs/touch-keyboard-types)
- **Do** auto-detect card type from the IIN and auto-space the card number with an input mask. [Baymard: L.L.Bean card form](https://baymard.com/blog/credit-card-form-ux-llbean)
- **Trust cues:** **Do** visually enclose the card fields (border or tinted background) and place a security badge next to them. Users rate the payment form's security by how it *looks*; 89% of sites don't reinforce these fields. Don't reuse the same styling for non-sensitive fields. [Baymard: perceived security](https://baymard.com/research-articles/perceived-security-of-payment-form), [Baymard: reinforce card fields](https://baymard.com/blog/visually-reinforce-sensitive-fields)

### Confirmation page
- **Do** always show an order summary, the expected arrival date, and "confirmation sent to x@y". Extras (one-step account creation, a short survey, warranty or insurance upsell) are fine underneath. [Baymard: order confirmation](https://baymard.com/research-articles/order-confirmation-page)

---

## 2. Booking and reservation flows on mobile (CarParker)

- **Do** use a calendar picker for near dates. Always allow typing as well. **Don't** use split day/month/year drop-downs. [NN/g: date input](https://www.nngroup.com/articles/date-input/)
- **Do** label today as "Today". Disable or grey out unavailable dates and remove illogical ones: past dates, or an end before the start. [same](https://www.nngroup.com/articles/date-input/)
- **Do** highlight the selected range, with start and end styled differently. **Don't** shift the calendar when the start date changes; users tap where a date *used* to be. [same](https://www.nngroup.com/articles/date-input/)
- **Do** show a short list (≤10 items) instead of a calendar when only a few slots are valid. [same](https://www.nngroup.com/articles/date-input/)
- **Time selection** *(convention)*:
  - Use chips in 15- or 30-minute steps, grouped as Morning/Afternoon/Evening, instead of wheel pickers. Wheels are slow when there are many values ([NN/g](https://www.nngroup.com/articles/date-input/)).
  - Show duration presets (1h, 2h, 4h, All day) that compute the end time.
  - Each chip must be at least 44px tall (§9).
- **Availability display** *(convention)*:
  - Show availability right in the picker: "12 bays free" or a dot-density indicator.
  - Show the live price for the chosen range in a sticky summary bar ("Lot B · Tue 14:00–18:00 · $12.00 · Book").
  - Don't make users submit a form to find out a slot is full.
- **Spell out month names**, for example "Tue 17 Nov", to avoid 10/11 ambiguity for international users. [NN/g: date input](https://www.nngroup.com/articles/date-input/)
- **Confirmation and receipt:**
  - Show a confirmation screen with the booking reference, lot address and map link, bay, entry window, price paid, cancellation or refund policy, and "receipt emailed".
  - Mirror the order-confirmation essentials from §1 ([Baymard](https://baymard.com/research-articles/order-confirmation-page)).
  - Add "Add to calendar" (.ics) and a scannable QR or booking code sized for the guard tablet *(convention)*.

---

## 3. Mobile admin and dashboard patterns (all three consoles, CarParker guard console)

### Navigation
- Hidden (hamburger) navigation cut content discoverability by more than 20% and made mobile tasks about 15% slower than visible or "combo" navigation. With **4 or fewer** top-level items, show them visibly; with more, show the top few and hide the rest. [NN/g: hamburger menus](https://www.nngroup.com/articles/hamburger-menus/)
- **Recommendation:** on phones, use a **bottom bar with 4 destinations plus "More"**. "More" opens the full drawer. The four should be the most frequent jobs, for example Orders, Deliveries, Support, More.
  - For the CarParker guard console, use 3–4 fixed actions (Check-in, Check-out, Incidents, Search).
  - Keep the left sidebar at ≥1024px. Vertical left navigation scales well on desktop. [NN/g: vertical nav](https://www.nngroup.com/articles/vertical-nav/)

### Data tables on small screens
- **Do** cut weak columns, keep text legible without zoom, and lock the header row (and the first column if horizontal scroll remains). If content overflows, make that obvious by showing a cut-off column, not dots. [NN/g: mobile tables](https://www.nngroup.com/articles/mobile-tables/)
- **Do** let users choose which columns or rows to view, and group secondary attributes in accordions. [same](https://www.nngroup.com/articles/mobile-tables/)
- **Pattern:** below about 768px, render each row as a **card**:
  - Primary identifier and status badge on line 1.
  - 2–3 priority fields on line 2.
  - Amount aligned right.
  - Tapping the card opens the detail view.
  
  Use **priority columns**: mark columns `always`, `md+` or `lg+`, and hide the lower tiers on narrow screens. ([Pega responsive tables](https://academy.pega.com/pt-br/topic/responsive-ui-and-tabular-data/v1/in/1331), [Setproduct 2026](https://www.setproduct.com/blog/data-table-ui-design))
- **Do** vary list indicators by colour **and** icon, not colour alone. NN/g measured about 37% faster scanning with both. [NN/g: visual indicators](https://www.nngroup.com/articles/visual-indicators-differentiators/)

### Filters, bulk actions, detail views
- **Filters** *(convention)*:
  - Put a "Filters (3)" button that opens a full-height bottom sheet with "Apply" and "Reset" pinned at the bottom.
  - Show active filters as removable chips above the list.
  - Persist filters in the URL query string; this works with Inertia's `preserveState` and the back button.
- **Bulk actions:**
  - Enter selection mode with a visible "Select" button. Don't rely on long-press alone: it is undiscoverable, and mobile browsers reserve long-press for text selection. ([Material selection](https://m2.material.io/design/interaction/selection), [bulk selection research roundup](https://www.saasui.design/blog/saas-bulk-actions-ux-patterns))
  - While anything is selected, show a contextual bar with a count ("3 selected"), the bulk actions, and a clear exit.
- **Detail views** *(convention)*:
  - Use a full-screen route on mobile, not a modal, so back and refresh work.
  - Group the status timeline, key facts, and actions.
  - Pin the primary action (for example "Mark delivered") in a bottom action bar. Put destructive actions in an overflow menu behind confirmation.
- **Touch targets:** WCAG 2.2 AA requires a 24×24 CSS px minimum (§9). Design admin controls at **44×44px** (Apple HIG and WCAG AAA 2.5.5); Material uses 48dp. [Target-size summary](https://tetralogical.com/blog/2022/12/20/foundations-target-size/)
  - For the guard tablet, used one-handed and possibly gloved, go larger: 56px+ primary buttons *(convention)*.

---

## 4. Reading and publishing UX (Inkwell)

- **Measure:** set body text at **50–75 characters per line**. Baymard's own example is a 20px serif in a 560px column, about 72 characters per line. In Tailwind, use `max-w-[65ch]`, or `prose` with `max-w-prose`. [Baymard: line length](https://baymard.com/blog/line-length-readability)
- **Body size** *(convention)*: 18–21px on desktop and 17–18px on mobile, with line-height 1.5–1.7 and paragraph spacing of about 1em. Never go below 16px on mobile, or iOS zooms inputs.
- **Reading progress:** an eye-tracking study found only 4 of 18 readers fixated on a custom scroll-progress bar. It is low-value, so keep it thin (2–3px) and decorative, or skip it. [Viget eye-tracking](https://www.viget.com/articles/should-you-use-a-custom-scroll-indicator-a-study-with-eye-tracking/)
  - A **reading-time estimate** is more useful: word count ÷ about 230–265 wpm (Medium uses 265). [Reading-time roundup](https://bushe.co/blog/how-to-estimate-reading-time-on-any-web-page-automatically/)
- **Feeds:**
  - **Do** restore scroll position when returning from a post.
  - **Do** show a loading indicator *before* the end of the loaded items.
  - **Don't** use pure infinite scroll if the footer matters.
  - **Consider** a "Load more" button with an "x of y" count.
  - Use the ARIA `feed` role.
  
  [NN/g: infinite scrolling](https://www.nngroup.com/articles/infinite-scrolling-tips/)
  - Inertia 3's `WhenVisible` and merge props fit "load more" well.
- **Comment threads:**
  - Cap visible nesting at **2–3 levels** on mobile. Beyond that, flatten with "replying to @name" or a "Continue thread →" link. [Uxcel: comments](https://uxcel.com/lessons/comments-best-practices-499), [Logora: threading](https://www.logora.com/lexique/comment-threading)
  - Collapse long branches ("Show 12 more replies").
  - Use thin vertical thread lines instead of deep indentation; about 12–16px per level *(convention)*.
- **Micro-interactions:**
  - Likes and bookmarks should update **optimistically**: change the count and icon instantly, and roll back with a toast on failure. Inertia 3 has first-class optimistic updates that revert automatically on error. [Inertia: optimistic updates](https://inertiajs.com/docs/v3/the-basics/optimistic-updates)
  - Keep the "pop" at about 100ms (§6), and keep the target at 44px even if the icon is 20px.

---

## 5. Perceived performance

- **Response-time limits:** under 0.1s feels instant, so no indicator is needed. Up to 1s, flow is kept. Over 1s, show that the system is busy. Over 10s, show percent-done plus a way to cancel. [NN/g: response times](https://www.nngroup.com/articles/response-times-3-important-limits/)
- **Skeleton vs spinner:**
  - Under 1s, show nothing; flashing loaders feel jarring.
  - Use **skeletons for full-page or content-region loads**, matched to the real layout. Don't use "frame-only" skeletons that show just the header and footer.
  - Use **spinners for a single module or button** (about 2–10s).
  - Use **progress bars for anything over 10s** or measurable (uploads, PDF export).
  
  [NN/g: skeleton screens](https://www.nngroup.com/articles/skeleton-screens/)
  - In Inertia, render skeletons in `<Deferred>` fallbacks and keep the router progress bar for page visits. Delay any spinner by about 300–500ms so fast responses never show it *(convention)*.
- **Optimistic UI:** use it for low-risk, reversible actions: likes, bookmarks, toggles, reordering, marking messages read. **Don't** use it for payments, refunds, check-in/out, or anything with side effects users can't undo. [Inertia: optimistic updates](https://inertiajs.com/docs/v3/the-basics/optimistic-updates)
- **View Transitions:**
  - **Same-document** (SPA) view transitions are Baseline "newly available" since Firefox 144 (October 2025). [web.dev](https://web.dev/blog/same-document-view-transitions-are-now-baseline-newly-available)
  - **Cross-document** transitions are still *limited availability*: Chrome/Edge 126+ and Safari 18.2+, not Firefox. [Web Features Explorer](https://web-platform-dx.github.io/web-features-explorer/features/cross-document-view-transitions/)
  - Element-scoped `element.startViewTransition()` is Chrome 147+ only. [web.dev April 2026](https://web.dev/blog/web-platform-04-2026)
  - Inertia 3 supports `viewTransition: true` per visit or as a global default, and falls back gracefully. [Inertia: view transitions](https://inertiajs.com/docs/v3/the-basics/view-transitions)
  - **Do** treat transitions as progressive enhancement: a cross-fade, or a shared element such as a product image to its detail hero.
  - **Do** disable them under reduced motion:
    ```css
    @media (prefers-reduced-motion: reduce) {
      ::view-transition-group(*), ::view-transition-old(*), ::view-transition-new(*) { animation: none !important; }
    }
    ```
- **CLS:**
  - **Do** set `width`/`height` on every `<img>` and use `height:auto`, or `aspect-ratio`.
  - Reserve `min-height` for embeds, maps and widgets. **Don't** collapse reserved space when content is missing.
  
  [web.dev: optimize CLS](https://web.dev/articles/optimize-cls)
  - **Fonts:** declare a metric-matched fallback `@font-face` over a local system font using `size-adjust`, `ascent-override`, `descent-override` and `line-gap-override`. Values must be measured per font pair, or generated by tools such as Capsize or fontaine. [web.dev: size-adjust](https://web.dev/articles/css-size-adjust), [Shopify: font-swap CLS](https://shopify.dev/docs/storefronts/themes/best-practices/performance/reduce-cls-font-swapping)
  - Self-host fonts and preload the body font *(convention)*.
- **INP:** the target is **≤200ms** at the 75th percentile; 200–500ms needs improvement and over 500ms is poor.
  - Yield to the main thread after updating the UI and defer non-critical work.
  - Avoid write-then-read layout thrashing.
  - Keep the DOM small; use `content-visibility:auto` for long feeds and tables.
  
  [web.dev: optimize INP](https://web.dev/articles/optimize-inp)

---

## 6. Motion and micro-interactions

- **Durations:**
  - Most UI motion: 100–400ms; 500ms is the outer limit, and "too long" is the more common failure.
  - Toggles and checkboxes: about 100ms.
  - Modals and sheets: 200–300ms.
  - Exits a little shorter than entrances (for example 300ms in, 200–250ms out).
  
  [NN/g: animation duration](https://www.nngroup.com/articles/animation-duration/)
- **Easing:** use **ease-out** for entering elements and **ease-in** for exiting ones. Avoid linear except for spinners and progress bars. [NN/g](https://www.nngroup.com/articles/animation-duration/)
  - Material 3's commonly cited tokens are standard `cubic-bezier(0.2,0,0,1)` and emphasized-decelerate `cubic-bezier(0.05,0.7,0.1,1)`. These come from a secondary source; check them against m3.material.io. [M3 motion tokens](https://www.skills.lc/shelbeely/Shelbeely-Agent-skills/shelbeely-shelbeely-agent-skills-skills-material-design-3-motion-skill-md)
  - Define 3 duration tokens (fast 120ms, base 200ms, slow 300ms) and 2 easings as Tailwind 4 `@theme` variables *(convention)*.
- **What to animate:** animate only `transform` and `opacity`, which stay off the layout path (see INP/CLS). Animate state changes such as add-to-cart confirmation, toast in and out, sheet open, accordion, like "pop", and status change. **Don't** animate first page load, large layout reflows, or anything that delays a primary action.
- **Reduced motion:** *reduce, don't remove*.
  - Replace movement with opacity cross-fades so state changes stay visible.
  - Turn off parallax, auto-playing carousels and scroll-linked effects.
  
  [Smashing: reduced motion](https://www.smashingmagazine.com/2020/09/design-reduced-motion-sensitivities), [Pleo design system](https://telescope.pleo.io/foundations/motion/motion.accessibility)

---

## 7. Transactional email

- **Layout:**
  - A single column, max-width **600px**, inside a 100%-width wrapper table.
  - Build the layout with `<table role="presentation">` (flex and grid support is unreliable) and inline CSS.
  - Stack columns under 600px, and make tap targets at least 44×44px.
  
  [Email on Acid: mobile email](https://emailonacid.com/blog/article/email-development/designing_html_emails_for_mobile_devices), [WooCommerce email HTML practices](https://developer.woocommerce.com/docs/features/email/email-html-best-practices/)
- **Bulletproof buttons:** make buttons from live HTML text on a padded, coloured table cell with a real link, not an image. Set a fallback `bgcolor`. [Sender: email design](https://www.sender.net/blog/email-design-guide/)
- **Dark mode** (about 40% of opens according to Litmus):
  - Add `<meta name="color-scheme" content="light dark">` and `supported-color-schemes`.
  - Use `@media (prefers-color-scheme: dark)` for Apple Mail and iOS, and duplicate the rules with `[data-ogsc]` for Outlook apps.
  - Expect forced inversion in Outlook for Windows and the Gmail apps.
  - Use transparent PNG logos with a light stroke or glow, and avoid pure #000/#fff.
  
  [Litmus: dark mode guide](https://www.litmus.com/blog/the-ultimate-guide-to-dark-mode-for-email-marketers)
- **Preheader:** write 70–100 characters of hidden preview text that adds to the subject. Mobile clients show about 35–90 characters, so front-load the key fact ("Order #TL-1042 confirmed · arrives Thu 19 Nov"). [MessageGears](https://messagegears.com/resources/blog/transactional-email-best-practices-with-examples/), [Zoho ZeptoMail](https://www.zoho.com/zeptomail/articles/using-email-preheaders.html)
- **Plain-text part:** send `multipart/alternative` with a readable text part. Its direct effect on deliverability is small, but some clients use it for previews and notifications. [Suped](https://www.suped.com/learn/email-deliverability/should-i-still-send-multipartalternative-emails)
- **Sender rules (Gmail since Feb 2024; Yahoo):**
  - All senders need SPF or DKIM, valid PTR, TLS, and a spam rate under 0.3% (aim for under 0.1%).
  - Bulk senders (5,000+/day to Gmail) also need SPF, DKIM and DMARC (`p=none` is fine), with From-domain alignment.
  
  [Google sender guidelines](https://support.google.com/a/answer/81126), [Yahoo sender best practices](https://senders.yahooinc.com/best-practices/)
- **One-click unsubscribe:** required for **marketing and subscribed** mail. **Transactional mail (receipts, password resets, booking confirmations) is excluded.** Send the two kinds from separate addresses or subdomains. [Google sender FAQ](https://support.google.com/a/answer/14229414?hl=en), [Gmail subscription guidelines](https://support.google.com/mail/answer/15263077?hl=en)
  - Yahoo requires unsubscribes to be honoured within **2 days**. [Yahoo](https://senders.yahooinc.com/best-practices/)
  - RFC 8058 implementation:
    - `List-Unsubscribe: <https://…/u/{opaque-token}>` (an optional `mailto:` may be added).
    - `List-Unsubscribe-Post: List-Unsubscribe=One-Click`.
    - Both headers must be covered by the DKIM `h=` tag.
    - The endpoint accepts a POST with no cookies or auth, returns no redirect, and asks for no confirmation.
    
    [RFC 8058](https://www.rfc-editor.org/rfc/rfc8058.html)
  - Inkwell digests and notifications count as subscribed mail, so they need this.
- **Receipt content checklist** (combines §1 and §8):
  - Brand, order or booking reference, and date/time.
  - Itemised lines (quantity, unit price, line total), subtotal, discounts, shipping/fees, tax, and total.
  - Payment method (brand plus last 4) and BNPL schedule if used.
  - Delivery or booking date, address or lot.
  - "View order" button, support contact, returns/cancellation link, and a PDF link or attachment.
  
  [Baymard: confirmation](https://baymard.com/research-articles/order-confirmation-page)

---

## 8. PDF receipts, invoices, statements

- **EU VAT invoice (Directive 2006/112/EC, Art. 226)** must include:
  - issue date;
  - a **sequential, gap-free number**;
  - supplier VAT ID (and customer VAT ID for reverse-charge B2B);
  - full names and addresses of both parties;
  - quantity and description of goods or services;
  - tax point date if it differs;
  - taxable amount per rate, VAT rate, and VAT amount in the invoice currency;
  - the exemption or reverse-charge wording where relevant.
  
  Simplified invoices for low values vary by country, so default to the full set. [Stripe: EU VAT invoice requirements](https://stripe.com/resources/more/eu-vat-invoice-content-requirements)
- **Layout conventions** *(convention)*:
  - A4/Letter-safe margins (≥15mm) and the brand mark top-left.
  - Document type ("Invoice", "Receipt", "Statement"), number and dates top-right.
  - Seller and buyer blocks, then the line-item table: description | qty | unit | tax | amount, with amounts right-aligned in **tabular figures**.
  - Totals block bottom-right with the grand total emphasised.
  - Payment status and method; a footer with legal entity details, support contact, and "Page x of y".
  - The table header repeats on page breaks.
  - Use embedded fonts and real text, not images, so the PDF is searchable and accessible.
- **BNPL / credit statement** (TechLandier) *(convention)*:
  - Statement period, opening balance, payments received, fees or interest, and closing balance.
  - The installment schedule table (due date, amount, status: paid, due or overdue) and the next payment due, highlighted.
  - APR and total payable, consistent with the disclosures in §1 ([Affirm](https://www.affirm.com/business/blog/marketing-compliant)).
- **Parking receipt** (CarParker): booking reference, lot and bay, actual entry and exit times from the guard check-in/out, duration, rate, overstay fees, insurance add-on, and any refunds as negative lines.

---

## 9. Accessibility essentials (WCAG 2.2)

- **2.5.8 Target Size (Minimum), AA:**
  - Pointer targets must be at least **24×24 CSS px**, or spaced so that 24px circles centred on each target don't overlap.
  - Inline links in sentences are exempt.
  - 2.5.5 (Enhanced) uses **44×44px**; adopt that for primary actions.
  
  [W3C: 2.5.8](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html)
  - Watch for: icon buttons in table rows, chip "×" removers, pagination, rating stars.
- **2.4.11 Focus Not Obscured (Minimum), AA:** a focused element must not be *entirely* hidden by author content such as sticky headers or footers, sticky CTAs, cookie banners, toasts or bottom bars. Fix it with `scroll-padding-top/bottom` equal to the sticky bar heights (technique C43), or make the banner modal. [W3C: 2.4.11](https://www.w3.org/WAI/WCAG22/Understanding/focus-not-obscured-minimum.html)
- **2.5.7 Dragging Movements, AA:** anything done by dragging needs a single-pointer alternative. A swipe gesture does **not** count as that alternative.
  - Sliders: allow tapping the track, or provide a number input.
  - Sortable lists: provide up/down buttons.
  - Maps: provide pan buttons, or a list view of lots.
  - Carousels: provide previous/next buttons.
  - Swipe-to-dismiss or swipe actions: provide visible buttons too.
  
  [W3C: 2.5.7](https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements.html)
- **Also:** status is never shown by colour alone (§3). Toasts and optimistic rollbacks are announced through `aria-live="polite"`. Errors are linked to their fields with `aria-describedby`. Skeletons are `aria-hidden`, with an `aria-busy` region.

---

## 10. Applying this to each app

### TechLandier (storefront, account, admin)
1. **Product page buy box:**
   - Large price.
   - One-line BNPL teaser ("or 4 × $X · 0% APR · Learn more") opening a plan-picker bottom sheet with monthly amount, total payable, APR and schedule.
   - Delivery **date** plus a cut-off countdown.
   - Returns link and a total-cost hint.
2. **Variant and add-on selection as buttons/chips** (storage, colour, warranty or insurance tier), with stock state visible before the tap.
3. **Conditional sticky buy bar** on mobile (appears once the main CTA scrolls away). Reserve `scroll-padding-bottom` for it.
4. **Guest checkout in an accordion:** collapsed completed steps with "Edit". Account creation offered on the confirmation page (password only).
5. **Trim checkout to about 8 fields:**
   - Full-name field; hidden Address line 2, Company and coupon.
   - Billing = shipping by default.
   - Postcode-first prefill.
   - Correct `inputmode`/`autocomplete` on every input.
6. **Payment step:** enclosed and tinted card fields with a security badge, IIN brand detection, and an auto-spaced card mask. Show the BNPL schedule again in the order summary.
7. **Confirmation page and receipt email:** summary, arrival date, "email sent to…", then the warranty and insurance upsell. The PDF invoice must meet Art. 226 (sequential numbering, VAT breakdown).
8. **Credit-plan account view:** next payment due, highlighted; schedule table; PDF statement; "pay now" and "pay off early" with the totals shown.
9. **Admin on mobile:**
   - Bottom bar (Orders, Deliveries, Support, More).
   - Orders, credit plans and claims shown as card lists with priority fields and status badges (colour + icon).
   - Filter bottom sheet and URL-persisted chips.
10. **Bulk actions** (orders, reviews moderation) through a visible "Select" button plus a contextual count bar.
11. **Skeletons for deferred analytics and catalog panels;** spinners only inside buttons, delayed about 400ms. Fix image `width`/`height` on catalog grids to remove CLS.
12. **Optimistic updates for low-risk admin toggles** (publish review, feature product). Never for refunds or credit decisions.

### CarParker (customer, guard console, admin)
1. **Booking picker:**
   - Calendar with today labelled and past or full days disabled.
   - Time **chips** (15/30-minute) plus duration presets instead of wheels.
   - Typed entry still allowed.
2. **Live availability in the picker** ("12 bays free"). A sticky summary bar shows the lot, window, price and "Book".
3. **Unambiguous dates everywhere** ("Tue 17 Nov, 14:00–18:00"). Make the time zone explicit if lots span zones.
4. **Confirmation screen:** reference, QR/booking code, lot address and map link, bay, entry window, price, cancellation and refund policy, "Add to calendar", and "receipt emailed".
5. **Map plus list toggle for lots.** The map needs non-drag alternatives such as zoom and pan buttons and the list view (2.5.7).
6. **Guard console optimized for one-handed tablet or phone use:**
   - 3–4 fixed bottom actions (Check-in, Check-out, Incidents, Search).
   - Primary buttons 56px+.
   - Large plate or reference search with a numeric/alphanumeric `inputmode`.
7. **Check-in/out is never optimistic.** Show a clear pending state, then a full-width success or failure banner with haptic-style motion (about 200ms) and `aria-live`.
8. **Incident reporting form:** labels above fields, photo upload with a progress bar (an upload counts as a progress-bar task), and draft auto-save.
9. **Admin bookings, sessions, refunds and claims:**
   - Card lists with status (colour + icon), priority columns at md+, and filter sheet and chips.
   - Bulk refund requires a confirmation dialog with the totals.
10. **Parking receipts and PDF invoices:** actual entry and exit times, duration, rate, overstay and insurance lines, refunds as negative lines, sequential numbering.
11. **Transactional emails** (booking confirmed, reminder, receipt): 600px, preheader with lot, date and time, bulletproof "View booking" button, and a dark-mode-safe logo.

### Inkwell (reading and social platform)
1. **Article typography:**
   - `max-width: 65ch`, an 18–21px body, line-height about 1.6.
   - Metric-matched fallback `@font-face` (`size-adjust`) for the serif body font to remove CLS.
2. **Reading-time estimate** (word count ÷ about 250 wpm) in the post header. Any progress bar stays 2–3px and decorative.
3. **Feeds:** restore scroll position on back navigation; load early (`WhenVisible`) with a visible loader; or use "Load more" with counts. Use `role="feed"` and `content-visibility:auto` on off-screen cards for INP.
4. **Skeleton cards** matching the real card layout for feed and profile loads. No spinner for sub-second loads.
5. **Optimistic likes, bookmarks and follows** with automatic rollback and a polite toast on failure. A 100ms scale "pop" that becomes a fade under reduced motion.
6. **Comment threads:**
   - At most 2–3 visible levels on mobile, then "Continue thread →".
   - Collapsed long branches.
   - Thin thread lines instead of deep indentation.
   - Reply and like targets at least 44px.
7. **View Transitions** between the feed card and the post page (shared cover image or title), through Inertia `viewTransition`. Cross-fade fallback and none under reduced motion.
8. **Notifications:** grouped by day, unread state shown by dot **and** weight (not colour alone), and "mark all read" (optimistic).
9. **Author analytics on mobile:** KPI tiles (reads, read ratio, followers) above a single chart. Tables collapse to cards. Chart data is also available as a list or table.
10. **Editor:** draft auto-save with "Saved · 12:04" status. Image uploads with a progress bar and reserved aspect-ratio placeholders. Publish confirmation shows the canonical URL.
11. **Digest and notification emails** carry RFC 8058 one-click unsubscribe headers (DKIM-signed) and honour opt-outs within 2 days. Password resets and receipts are sent from a separate transactional address.
12. **Mobile bottom bar** (Home, Search, Write, Notifications, Profile). Tap targets at least 44px, and focus never hidden behind the bar (`scroll-padding-bottom`).
