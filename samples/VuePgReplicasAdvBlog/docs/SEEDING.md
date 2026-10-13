# Migrations and seeding

MainApp owns the one-shot CLI. Worker processes the background jobs the CLI produces. Both commands exit without starting an HTTP server; they do not require a browser.

## Aspire

AppHost always starts `migrate`, waits for successful completion, and then starts replicas. Migrations run once per deployment, outside the serving replicas.

For demo content, opt into one additional `seed` resource before starting Aspire:

```powershell
$env:Seed__Demo = 'true'
$env:Seed__Users = '200'
$env:Seed__Posts = '1000'
dotnet dnx --yes aspire.cli@13.6.1 -- run --apphost (Resolve-Path apphost.cs).Path
```

AppHost waits for migrations, runs `seed`, waits for it to finish, then starts MainApp, Worker and Realtime. Clear `Seed__Demo` after the seed finishes if you do not want to run it on every subsequent start:

```powershell
Remove-Item Env:Seed__Demo, Env:Seed__Users, Env:Seed__Posts -ErrorAction SilentlyContinue
```

The same opt-in works when publishing Compose. Normally leave it disabled for production; do not add demo accounts to a live user database.

## Compose or a deployed container

From the sample directory, against the existing stack's database:

```powershell
docker compose run --rm migrate
docker compose run --rm -e Seed__Users=200 -e Seed__Posts=1000 app --seed-demo
docker compose run --rm app --reindex-search
```

Run reindex only when `SEARCH_DRIVER=meilisearch` and its connection settings are configured. Worker must run to consume queued indexing and discovery jobs. In Aspire-generated Compose, use `app1` instead of `app`; the generated migrator also remains named `migrate`.

For Dokploy/Coolify, execute the equivalent command in one MainApp container's terminal:

```sh
Seed__Users=200 Seed__Posts=1000 dotnet MainApp.dll --seed-demo
dotnet MainApp.dll --reindex-search
```

The container inherits the same database, gateway URL and feature settings as its serving process. Do not run the seed separately in every replica. The CLI takes a PostgreSQL session advisory lock and rejects a second concurrent seed.

## Counts, repeatability and login

`--seed-demo` defaults to **200 users and 1,000 posts**. Set `Seed__Users` and `Seed__Posts` to change them; the minimums are 20 users and 40 posts. For the original large dataset, choose 78,000 users and 500,000 posts explicitly. Related follows, comments, likes and discovery data increase the total row count.

The seeder uses deterministic demo keys and checkpoints. Rerunning with the **same counts** resumes missing batches and does not duplicate existing demo rows. Preserve those counts when resuming; changing them against an existing seeded database is rejected rather than silently producing incomplete relationships. It does not reset user-created content. Use a fresh database/volume for a different dataset size.

Demo accounts are already verified. Example login:

```text
Email: inertia.demo42.00001@yopmail.com
Password: Demo-42-correct-horse!
```

Demo preferences disable scheduled/comment email. The application's reserved demo-address handling captures generated mail locally rather than sending it through Maileroo.

Renamed samples have their own database defaults and volumes. Original volumes remain intact. To migrate real data between the original and renamed samples, use PostgreSQL backup/restore rather than pointing two PostgreSQL containers at the same data volume.
