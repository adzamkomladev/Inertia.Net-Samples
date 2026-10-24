# Using TechLandier

## Demo and administrator access

Open the [live demo](https://techlandier.yebi.africa/). Sign in as
`inertia.demo42.techlandier.admin@yopmail.com` or `inertia.demo42.techlandier.customer@yopmail.com`;
both use `Password@12345`.

For your own database, run these commands from the TechLandierDistributed directory:

```sh
dotnet run --project apps/MainApp -- --seed-demo
dotnet run --project apps/MainApp -- --create-admin
```

Seeding is optional. It creates sample products, orders, installment schedules, care cases and deliveries.
Rerunning resumes the seeder and applies its one-time demo credential update without deleting live data.
The admin command prompts for name, email and a hidden password, creates a verified administrator, and exits.
It rejects an existing email instead of replacing or promoting that account. In an app container, use
`dotnet TechLandierDistributed.MainApp.dll --create-admin` (or `--seed-demo`). Use your own credentials for real administrators.

## Products and the storefront

Open **Catalog** at `/admin/catalog`. Filter by status (active, draft, low stock, archived), search by name, brand or
keyword, and page through 20 products at a time. **New product** opens a side sheet: drop in up to eight photos (the
first is the cover; reorder with the arrows or make any photo the cover), then set the name, brand, category,
visibility, price and discount (the sheet shows what customers pay), opening stock, low-stock alert, warranty,
insurance, description, keywords and specification rows. Uploads need the S3 settings in [deployment](DEPLOY.md);
products without photos use generated art.

Select a product to open its page: photo gallery, price, stock, sales and rating, details and the stock history.
**Edit** reopens the sheet; **Adjust stock** records a restock or write-off in the history; **View in store** opens the
public page, which shows the full photo gallery.

Customers browse the redesigned home page, deals and `/products`, then open `/products/{slug}` for product
information, reviews and related products. The home page includes category and product sections and saved store hours.
The SSR configuration and page contracts are described in [CONTRACT.md](CONTRACT.md); the public storefront
supports SSR, while the login page renders on the client. Preferred SSR can fall back to client rendering
if its runtime is unavailable, so an enabled setting alone does not prove a deployed response was server rendered.

## Order, installment and care details

| Start here | Open | What to inspect or do |
|---|---|---|
| `/admin/orders` | An order row, `/admin/orders/{number}` | Customer, items, payments, delivery, timeline, financing and cover; use the available order actions and rider assignment. |
| `/admin/credit` | A plan, `/admin/credit/{id}` | Full installment schedule, amounts, paid dates and the associated order/customer. |
| `/admin/care` | A warranty claim, insurance claim or repair | Dedicated detail page with case information and the applicable decision/status actions. |
| `/account/orders` | An order | Customer order detail, timeline, delivery, installments and cover. |
| `/account/credit` | Installment payment action | Pay an eligible installment and review active plans or financing restrictions. |
| `/account/care/repairs` | A repair | Repair status and history. Warranty and insurance claim submission are in their respective care lists. |

Care detail URLs are `/admin/care/warranty-claims/{id}`, `/admin/care/insurance-claims/{id}` and
`/admin/care/repairs/{id}`. Payment processing is simulated in this sample; do not treat demo payments as a live gateway.

## Drivers and delivery regions

Open **Deliveries** at `/admin/deliveries`. The summary tiles show runs waiting for a rider, runs on the road,
pay-on-delivery cash riders are holding and today's deliveries. The **Board** tab lists runs by stage (to dispatch,
on the road, delivered) with search and a zone filter. Use **Assign rider** to pick from a searchable list sorted by
spare capacity, then record pickup, delivery (cash collected and who received the parcel) or a failed attempt with a
reason; failed runs return to the dispatch lane for reassignment. The row menu adds a note for the rider or calls the
customer. **Riders** and **Zones** tabs manage drivers (contact, vehicle, service, runs-at-once capacity, availability)
and delivery regions (fee, ETA, cash on delivery, checkout availability) through side sheets.

## Store settings and working hours

Open `/admin/settings` to change the store name, support email, phone, address and time zone.
For each weekday, set the store and delivery opening/closing times separately, or mark either closed, then save.
Each window must end after it starts on the same day. These are published fulfilment hours shown on the home
and checkout pages; customers can still place orders outside them.

## Team management and notifications

Open `/admin/team` to create another administrator with a unique email and a password of 12–128 characters.
Removing another administrator demotes them to customer and preserves their account and orders.
You cannot remove your own administrator access. Public registration never grants administrator access.

Use the header speaker button to enable notification chimes. This is an opt-in browser preference; click or
interact with the page after a reload to unlock browser audio. Sound accompanies live events, not every historical
alert. Check the alerts inbox for saved notifications. Workers must be running for queued realtime delivery;
see [DEPLOY.md](DEPLOY.md) for worker and SignalR deployment requirements.
