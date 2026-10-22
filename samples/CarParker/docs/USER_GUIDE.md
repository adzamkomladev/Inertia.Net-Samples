# Using CarParker

## Demo and administrator access

From the CarParker directory:

```sh
dotnet run --project src/CarParker -- --seed-demo
dotnet run --project src/CarParker -- --create-admin
```

The demo accounts are `inertia.demo42.carparker.admin@yopmail.com`,
`inertia.demo42.carparker.guard@yopmail.com` and `inertia.demo42.carparker.customer@yopmail.com`;
all use `Password@12345`. Rerunning the seeder resumes its phases and updates old demo credentials once
without resetting live data. Seeding is optional for a new installation.

The admin command prompts for name, email and a hidden password and creates a verified administrator.
It rejects existing emails. In the app container use `dotnet CarParker.dll --create-admin` or `--seed-demo`.

## MongoDB: standalone or replica set

Set the mode and URL together in your environment, then restart the app:

| Deployment | Mode | Example URL |
|---|---|---|
| Standalone | `MONGO_MODE=single` | `mongodb://localhost:27017/?directConnection=true` |
| Replica set | `MONGO_MODE=replica` | `mongodb://localhost:27018/?replicaSet=rs0&directConnection=true` |

Single mode is the default outside production Compose. Production Compose defaults to replica mode;
its external database override is `DATABASE_URL_DOCKER`. The toggle changes application behavior; it does
not convert the database server's topology. No reseed is needed. Remove `replicaSet` from standalone URLs.
Replica mode fails at startup on a standalone server instead of silently disabling transactions.

Single-document guards, including claiming a free bay, remain atomic. Single mode has no rollback across
collections: a failed booking, refund or job enqueue can leave partial data requiring repair. Use replica mode
when those operations must commit together. See [DEPLOY.md](DEPLOY.md) for Coolify, Dokploy and Compose setup,
and [README.md](../README.md#mongodb-mode) for tests against either topology.

## Lots and operating hours

Open `/admin/lots`, create a lot or open an existing lot at `/admin/lots/{id}`.
Set its time zone and choose 24-hour access or daily opening and closing times, then save.
These hours apply every day; this is not a separate seven-day schedule. Existing lots default to 24-hour access.
New single and recurring bookings must fit within one open day in the lot's time zone. Existing bookings
are preserved. Overnight access requires the 24-hour option.

The lot screen also manages bays and their state overrides. Customers can inspect the live floor plan
and published hours before booking. CarParker's operating settings belong to each lot.

## Booking, refund and insurance details

| Start here | Detail or action |
|---|---|
| `/admin/bookings` | Open a booking at `/admin/bookings/{id}` to inspect the reservation and its related records. |
| `/account/bookings` | Open your booking at `/account/bookings/{id}`. |
| `/admin/refunds` | Review and approve/reject a refund, then record payment for an approved refund. |
| `/admin/insurance` | Open a Spot Cover claim at `/admin/claims/{id}` to inspect coverage, customer and decision/payment information; use approve/pay/reject as applicable. |
| `/admin/sessions` | Review parking sessions and use force-close when required. |

CarParker models parking bookings and Spot Cover; electronics repairs and installment plans belong to TechLandier.

## Team and notification sound

At `/admin/team`, create administrators or guards with unique emails and passwords of 12–128 characters.
Removing another staff member demotes them to customer and preserves bookings. Self-removal is blocked.
Current database roles determine private staff notification recipients.

Enable chimes with the header speaker button. Audio is opt-in per browser and may need interaction after reload.
Saved alerts remain in the inbox; workers deliver live events asynchronously. See [DEPLOY.md](DEPLOY.md)
for worker and SignalR requirements and [CONTRACT.md](CONTRACT.md) for routes and payloads.
