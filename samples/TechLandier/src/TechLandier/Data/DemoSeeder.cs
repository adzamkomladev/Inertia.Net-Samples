using System.Security.Cryptography;
using System.Text;
using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TechLandier.Features;

namespace TechLandier.Data;

public sealed record DemoSeedSize(
    int Users = 4, int Zones = 8, int Riders = 6, int Categories = 10, int Products = 120, int Orders = 400,
    int Plans = 60, int Warranties = 250, int WarrantyClaims = 15, int Insurances = 80, int InsuranceClaims = 25,
    int Repairs = 40, int Reviews = 90, int Tickets = 50, int Alerts = 10, int ProductViews = 200);

/// Demo data for the TechLandier storefront, deterministic (Bogus seed 42) and resumable:
/// every stage records a SeedCheckpoint, so an interrupted seed continues where it stopped.
public static class DemoSeeder
{
    public const string Password = "TechLandier-42-demo!";

    static readonly string[] LoginNames = ["admin", "customer", "customer2", "customer3"];
    static readonly string[] Brands = ["SonicWave", "PixelPeak", "ByteForge", "LumaVision", "NorthSync", "Voltrix", "KiteWorks", "HaloEdge", "SignalPoint", "GridForge"];
    static readonly (string Name, string Slug, string Icon, string Tagline)[] CategorySeeds =
    [
        ("Headphones", "headphones", "Headphones", "Immersive listening, wired or wireless."),
        ("Computers", "computers", "Cpu", "Desktop towers, mini PCs and laptops."),
        ("Phones", "phones", "Smartphone", "Flagship phones with unbeatable trade-ins."),
        ("Cameras", "cameras", "Camera", "Mirrorless, DSLR and action cameras."),
        ("TV & Home", "tv-home", "Tv", "OLED panels and home theatre packages."),
        ("Gaming", "gaming", "Gamepad2", "Consoles, controllers and gaming gear."),
        ("Wearables", "wearables", "Watch", "Smartwatches and fitness band deals."),
        ("Speakers", "speakers", "Speaker", "Party speakers and studio monitors."),
        ("Accessories", "accessories", "Zap", "Chargers, cases, mounts and extras."),
        ("Cables & Power", "cables-power", "Cable", "USB-C, HDMI, hubs and power banks."),
    ];
    static readonly string[][] CategoryNouns =
    [
        ["Studio Pro", "Aircancel", "BassLine", "OverEar", "Pods"],
        ["NovaBook", "Tower X", "Mini PC", "UltraBook", "WorkStation"],
        ["Galaxy S", "Pixel One", "Nord", "Edge", "Neo"],
        ["ShotMirror", "ActionGo", "LensPro", "FilmMaker", "ZoomX"],
        ["OLED Panel", "Cinema 55", "SoundBar", "TheatreView", "FrameTV"],
        ["PlayCube", "Controller Elite", "HandheldX", "RaceWheel", "ArcadeStick"],
        ["FitWatch", "BandLite", "ChronoFit", "TrailGPS", "PulseRing"],
        ["PartyBox", "StudioMonitor", "BoomMini", "ShelfSpeaker", "SoundDeck"],
        ["PowerBank", "MagCase", "DeskMount", "GripPad", "CleanKit"],
        ["USB-C Hub", "HDMI Cable", "FastCharger", "Dock Pro", "SurgeBlock"],
    ];
    static readonly (string Name, decimal Fee, string Eta, bool Pod)[] ZoneSeeds =
    [
        ("Airport City", 4m, "Same day if ordered by 2pm", true),
        ("East Legon", 6m, "Same day if ordered by 2pm", true),
        ("Osu - Labone", 5m, "Same day if ordered by 1pm", true),
        ("Adabraka", 4m, "Same day if ordered by 2pm", true),
        ("Tema", 8m, "1-2 days", false),
        ("Kasoa Corridor", 10m, "1-2 days", false),
        ("Madina", 8m, "Next business day", true),
        ("Dansoman", 9m, "1-2 days", true),
    ];
    static readonly (string Name, string Phone, string Vehicle, string Service)[] RiderSeeds =
    [
        ("Kwesi Boateng", "+233 24 100 2001", "Boxed bike", RiderServices.InHouse),
        ("Ama Mensah", "+233 24 100 2002", "Honda CB", RiderServices.Yango),
        ("Kojo Owusu", "+233 24 100 2003", "Delivery bike", RiderServices.Bolt),
        ("Nana Adjei", "+233 24 100 2004", "Scooter", RiderServices.Uber),
        ("Yaw Frimpong", "+233 24 100 2005", "Suzuki", RiderServices.InHouse),
        ("Efua Antwi", "+233 24 100 2006", "Vespa", RiderServices.Other),
    ];
    static readonly (string Label, string[] Values)[] SpecSeeds =
    [
        ("Ports", ["USB-C", "USB-C x2", "HDMI + USB-A"]),
        ("Battery", ["4 000 mAh", "8 000 mAh", "18-hour runtime"]),
        ("Finish", ["Matte black", "Silver", "Midnight blue", "Sand"]),
        ("Connectivity", ["Bluetooth 5.3", "Wi-Fi 6E", "Dual SIM"]),
    ];
    static readonly string[] TicketSubjects =
        ["Wrong item delivered", "Phone arrived cracked", "Delivery far too late", "Warranty repair help",
         "Refund not showing", "Insurance claim question", "Accessories missing in box"];
    static readonly string[] TicketCategoryList =
        [TicketCategories.WrongItem, TicketCategories.ProductFault, TicketCategories.LateDelivery, TicketCategories.Warranty,
         TicketCategories.Refund, TicketCategories.Insurance, TicketCategories.Other];

    public static Task RunAsync(AppDb db, CancellationToken ct = default) => RunAsync(db, new DemoSeedSize(), ct);

    public static async Task RunAsync(AppDb db, DemoSeedSize size, CancellationToken ct = default)
    {
        Randomizer.Seed = new Random(42);
        db.Database.SetCommandTimeout(TimeSpan.Zero); // seed-time bulk statements exceed the 30s default at large sizes
        var f = new Faker("en");

        // The anchor pins the demo calendar: plans/due dates stay stable across resumed runs.
        var anchor = await db.SeedCheckpoints.FindAsync(["demo-42-anchor"], ct);
        if (anchor is null)
        {
            anchor = new SeedCheckpoint { Name = "demo-42-anchor", Seed = 42, UpdatedAt = DateTime.UtcNow.Date };
            db.Add(anchor);
            await db.SaveChangesAsync(ct);
        }
        var today = DateOnly.FromDateTime(DateTime.SpecifyKind(anchor.UpdatedAt, DateTimeKind.Utc).Date);
        var now = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(1);

        async Task Batch(string stage, Func<Task> seed)
        {
            var name = $"demo-42-{stage}";
            if (await db.SeedCheckpoints.FindAsync([name], ct) is not null) return;
            await seed();
            await db.SaveChangesAsync(ct);
            db.Add(new SeedCheckpoint { Name = name, Completed = 1, Seed = 42 });
            await db.SaveChangesAsync(ct);
        }

        async Task Insert<T>(IReadOnlyList<T> rows) where T : class
        {
            if (rows.Count == 0) return;
            db.AddRange(rows);
        }

        // ---------------------------------------------------------------- users: 1 admin + 3 customers
        var passwordHash = new PasswordHasher<User>().HashPassword(new User { Email = "", Name = "" }, Password);
        await Batch("users", () => Insert(Rows(0, size.Users, i => new User
        {
            DemoKey = $"demo-42-{LoginNames[i % LoginNames.Length]}-{i}",
            Email = i switch
            {
                0 => "admin@techlandier.test",
                1 => "customer@techlandier.test",
                2 => "customer2@techlandier.test",
                _ => "customer3@techlandier.test",
            },
            Name = i == 0 ? "Tayo Admin" : $"{f.Name.FirstName()} {f.Name.LastName()}",
            Role = i == 0 ? "admin" : "customer",
            PasswordHash = passwordHash,
            IsDemo = true,
            EmailVerifiedAt = now.AddDays(-120),
            CreatedAt = now.AddDays(-120),
            Preferences = i > 1 ? new() { EmailPromos = i % 2 == 0 } : new(),
        }).ToList()));

        // ---------------------------------------------------------------- zones + riders + categories
        await Batch("zones", () => Insert(Rows(0, size.Zones, i => ToZone(ZoneSeeds, i, now)).ToList()));
        await Batch("riders", () => Insert(Rows(0, size.Riders, i => ToRider(RiderSeeds, i, now)).ToList()));
        await Batch("categories", () => Insert(Rows(0, size.Categories, i => ToCategory(CategorySeeds, i, now)).ToList()));

        var zones = await db.DeliveryZones.AsNoTracking().OrderBy(z => z.Name)
            .Select(z => new ZoneRef(z.Id, z.Fee)).ToListAsync(ct);
        var riders = await db.Riders.AsNoTracking().OrderBy(r => r.Name).Select(r => r.Id).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).Select(c => c.Id).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().Where(u => u.DemoKey != null && u.DemoKey.StartsWith("demo-42-"))
            .OrderBy(u => u.DemoKey).Select(u => new { u.Id, u.Role }).ToListAsync(ct);
        if (users.Count != size.Users) throw new InvalidOperationException("Demo users changed; refusing to seed relationships against an incomplete set.");
        var customers = users.Where(u => u.Role == "customer").Select(u => u.Id).ToArray();
        var adminId = users.First(u => u.Role == "admin").Id;

        // ---------------------------------------------------------------- products
        await Batch("products", () => Insert(FormProducts(0, size.Products, categories, now)));

        var products = await db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.DemoKey != null && p.DemoKey.StartsWith("demo-42-product-"))
            .OrderBy(p => p.DemoKey)
            .Select(p => new ProductRef(p.Id, p.Name, p.Brand, p.Price, p.DiscountPercent, p.Stock,
                p.WarrantyMonths, p.IsInsurable, p.Status, p.Category.Icon))
            .ToListAsync(ct);
        if (products.Count != size.Products) throw new InvalidOperationException("Demo products changed; refusing incomplete seed.");
        var activeProducts = products.Where(p => p.Status == ProductStatuses.Active).ToList();
        var productById = products.ToDictionary(p => p.Id, p => p);

        // ---------------------------------------------------------------- orders + items (one product line per order)
        var orders = FormOrders(size, products, zones, customers, f, now);
        await Batch("orders", async () =>
        {
            await Insert(orders.Select(o => o.Order).ToList());
            await Insert(orders.Select(o => o.Item).ToList());
        });

        var ordersKeyed = await db.Orders.AsNoTracking()
            .Where(o => o.DemoKey != null && o.DemoKey.StartsWith("demo-42-order-"))
            .OrderBy(o => o.DemoKey)
            .Select(o => new OrderKey(o.Id, o.Number, o.UserId, o.ZoneId, o.Status, o.Total, o.Subtotal, o.DiscountTotal,
                o.ShippingFee, o.PaymentMethod, o.PaymentStatus, o.CreatedAt, o.PaidAt, o.ShippedAt, o.DeliveredAt,
                o.CancelledAt, o.CancelReason, o.ReceiptNumber))
            .ToListAsync(ct);
        if (ordersKeyed.Count != orders.Count) throw new InvalidOperationException("Demo orders changed; refusing incomplete seed.");

        // ---------------------------------------------------------------- payments + timeline events
        await Batch("payments", () => Insert(Rows(0, ordersKeyed.Count, i => FormPayment(ordersKeyed[i])).ToList()));
        await Batch("events", () => Insert(Rows(0, ordersKeyed.Count, i => FormEvents(ordersKeyed[i])).SelectMany(e => e).ToList()));

        // ---------------------------------------------------------------- installment plans (BNPL on paid card/momo orders)
        var (plans, installments) = BuildPlans(size, ordersKeyed);
        await Batch("plans", async () =>
        {
            await Insert(plans);
            await Insert(installments);
        });

        // ---------------------------------------------------------------- deliveries for shipped/delivered/pod orders
        var deliveries = BuildDeliveries(ordersKeyed, riders);
        await Batch("deliveries", () => Insert(deliveries));

        // ---------------------------------------------------------------- warranties (auto-created for warranty products)
        var items = await db.OrderItems.AsNoTracking()
            .Join(db.Orders.Where(o => o.DemoKey != null), i => i.OrderId, o => o.Id,
                (i, o) => new ItemRef(i.Id, o.Id, o.UserId, o.Status, o.PaidAt, o.CreatedAt, i.ProductId, i.Qty))
            .ToListAsync(ct);
        var warrantyRows = BuildWarranties(items, productById, size.Warranties);
        await Batch("warranties", () => Insert(warrantyRows));
        var warranties = await db.Warranties.AsNoTracking().Where(w => w.DemoKey != null)
            .OrderBy(w => w.DemoKey).Select(w => new { w.Id, w.StartOn }).ToListAsync(ct);

        if (warranties.Count > 0)
            await Batch("warranty-claims", () => Insert(Rows(0, size.WarrantyClaims, i =>
            {
                var w = warranties[i % warranties.Count];
                var created = w.StartOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(7 + i % 40);
                var step = i % 5;
                var symptom = new[] { "charging", "working", "pairing", "holding battery" }[i % 4];
                return new WarrantyClaim
                {
                    WarrantyId = w.Id,
                    Issue = $"Device stopped {symptom} after {2 + step} weeks.",
                    Status = new[] { WarrantyClaimStatuses.Submitted, WarrantyClaimStatuses.InReview, WarrantyClaimStatuses.Approved, WarrantyClaimStatuses.Rejected, WarrantyClaimStatuses.Resolved }[step],
                    DecisionNote = step switch { 2 or 4 => "Replacement authorised after inspection.", 3 => "Outside coverage after inspection.", _ => null },
                    CreatedAt = created,
                    UpdatedAt = created,
                };
            }).ToList()));

        // ---------------------------------------------------------------- device insurance + claims
        var insuranceRows = BuildInsurances(items, productById, size.Insurances);
        await Batch("insurances", () => Insert(insuranceRows));
        var policies = await db.DeviceInsurances.AsNoTracking().Where(w => w.DemoKey != null)
            .OrderBy(w => w.DemoKey).Select(w => new { w.Id, w.StartOn, w.CoverageAmount }).ToListAsync(ct);

        if (policies.Count > 0)
            await Batch("insurance-claims", () => Insert(Rows(0, size.InsuranceClaims, i =>
            {
                var p = policies[i % policies.Count];
                var created = p.StartOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1 + i % 20);
                var incident = new[] { "damage", "theft", "malfunction" }[i % 3];
                var during = new[] { "commute", "storage", "daily use", "a drop on the floor" }[i % 4];
                return new InsuranceClaim
                {
                    DeviceInsuranceId = p.Id,
                    IncidentType = incident,
                    Description = $"Screen cracked during {during}. Asking for the insured amount.",
                    IncidentOn = p.StartOn.AddDays(2 + i % 150),
                    PayoutAmount = i % 5 is 2 or 3 ? Math.Round(p.CoverageAmount, 2, MidpointRounding.AwayFromZero) : null,
                    Status = new[] { InsuranceClaimStatuses.Submitted, InsuranceClaimStatuses.UnderReview, InsuranceClaimStatuses.Approved, InsuranceClaimStatuses.Paid, InsuranceClaimStatuses.Rejected }[i % 5],
                    PaidAt = i % 5 == 3 ? p.StartOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(9) : null,
                    CreatedAt = created,
                    UpdatedAt = created,
                };
            }).ToList()));

        // ---------------------------------------------------------------- repair tickets
        var repairs = BuildRepairs(size, activeProducts, customers, now);
        await Batch("repairs", () => Insert(repairs));

        // ---------------------------------------------------------------- reviews: unique per user per product
        var reviews = BuildReviews(size, items, customers);
        await Batch("reviews", () => Insert(reviews));
        await db.Database.ExecuteSqlAsync($"""
            UPDATE "Products" p SET
                "RatingCount" = COALESCE((SELECT COUNT(*)::integer FROM "Reviews" r WHERE r."ProductId" = p."Id" AND r."Status" = {ReviewStatuses.Published}), 0),
                "RatingAvg" = COALESCE((SELECT ROUND(AVG(r."Rating")::numeric, 2) FROM "Reviews" r WHERE r."ProductId" = p."Id" AND r."Status" = {ReviewStatuses.Published}), 0),
                "SoldCount" = COALESCE((SELECT SUM(i."Qty")::integer FROM "OrderItems" i JOIN "Orders" o ON o."Id" = i."OrderId" WHERE i."ProductId" = p."Id" AND o."Status" <> {OrderStatuses.Cancelled}), 0)
            """, ct);

        // ---------------------------------------------------------------- tickets + messages
        var (tickets, ticketMessages) = BuildTickets(size, ordersKeyed, customers, adminId, f, now);
        await Batch("tickets", async () =>
        {
            await Insert(tickets);
            await Insert(ticketMessages);
        });

        // ---------------------------------------------------------------- alerts for the demo customer
        var demoCustomer = customers[0];
        await Batch("alerts", () => Insert(Rows(0, size.Alerts, i => new Alert
        {
            UserId = demoCustomer,
            Kind = new[] { AlertKinds.OrderStatus, AlertKinds.InstallmentDue, AlertKinds.Warranty, AlertKinds.Reply, AlertKinds.Insurance, AlertKinds.Repair, AlertKinds.Promo }[i % 7],
            Title = new[] { "Order TL-2026 confirmed", "Installment due in 3 days", "Warranty registered", "Support replied to your ticket", "Insurance policy active", "Repair quoted at $45", "Weekend deals are live" }[i % 7],
            Body = "Seeded sample notification for the demo dashboard.",
            Href = i % 7 == 0 ? "/account/orders" : i % 7 == 1 ? "/account/credit" : i % 7 == 2 ? "/account/care/warranties" : i % 7 == 3 ? "/account/support" : i % 7 == 4 ? "/account/care/insurance" : i % 7 == 5 ? "/account/care/repairs" : "/deals",
            ReadAt = i % 3 == 0 ? null : now.AddDays(-2),
            CreatedAt = now.AddDays(-i),
        }).ToList()));

        // ---------------------------------------------------------------- product views (analytics hash rows)
        await Batch("product-views", () => Insert(Rows(0, size.ProductViews, i => new ProductView
        {
            ProductId = activeProducts[i % activeProducts.Count].Id,
            ViewerHash = Token($"viewer-{i}")[..32],
            Referrer = i % 4 == 0 ? "google.com" : i % 4 == 1 ? "twitter.com" : null,
            At = now.AddDays(-(i % 30)).AddMinutes(i % 1440),
        }).ToList()));

        // ---------------------------------------------------------------- stock movements: restock rows keep SUM(movements) == stock
        var soldPerProduct = items.Where(x => x.Status != OrderStatuses.Cancelled)
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        var releasedPerProduct = items.Where(x => x.Status == OrderStatuses.Cancelled)
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        var movements = products.Select((p, i) => new StockMovement
        {
            ProductId = p.Id,
            Delta = p.Stock + soldPerProduct.GetValueOrDefault(p.Id) - releasedPerProduct.GetValueOrDefault(p.Id),
            Reason = StockReasons.Restock,
            Note = "Opening inventory",
            ByUserId = adminId,
            At = now.AddDays(-(i % 120 + 1)),
        })
        .Concat(items.Where(x => x.Status != OrderStatuses.Cancelled).Select(x => new StockMovement
        {
            ProductId = x.ProductId,
            Delta = -x.Qty,
            Reason = StockReasons.Sale,
            OrderId = x.OrderId,
            Note = "Demo sale",
            At = x.PaidAt ?? x.CreatedAt,
        }))
        .Concat(items.Where(x => x.Status == OrderStatuses.Cancelled).Select(x => new StockMovement
        {
            ProductId = x.ProductId,
            Delta = x.Qty,
            Reason = StockReasons.Release,
            OrderId = x.OrderId,
            Note = "Order cancelled; stock released",
            At = x.CreatedAt.AddDays(2),
        }))
        .ToList();
        await Batch("stock-movements", () => Insert(movements));

        // ---------------------------------------------------------------- sales rollup history (30 days of daily stats)
        await Batch("sales-rollup", () => RollupSales(db, ordersKeyed, today, ct));

        await db.Database.ExecuteSqlRawAsync("ANALYZE;", ct);
        Console.WriteLine("Demo seed 42 complete: storefront catalog, orders, BNPL, care, support and analytics history");
        Console.WriteLine($"  logins: admin@techlandier.test / customer@techlandier.test — password {Password}");
    }

    /// Rebuild SalesDailyStat rows for the trailing 30 days from the demo orders (idempotent per day).
    static async Task RollupSales(AppDb db, List<OrderKey> orders, DateOnly today, CancellationToken ct)
    {
        var windowStart = today.AddDays(-30);
        var bnplTotals = await db.PayPlans.AsNoTracking()
            .Where(p => p.Order.DemoKey != null)
            .Select(p => new { p.OrderId, p.Order.Total }).ToListAsync(ct);
        var bnplByOrder = bnplTotals.GroupBy(p => p.OrderId).ToDictionary(g => g.Key, g => g.First().Total);

        var rows = orders
            .Where(o => o.PaidAt != null && DateOnly.FromDateTime(o.PaidAt.Value) >= windowStart)
            .GroupBy(o => DateOnly.FromDateTime(o.PaidAt!.Value))
            .Select(g => new SalesDailyStat
            {
                Date = g.Key,
                Revenue = g.Sum(o => o.Total),
                OrderCount = g.Count(),
                BnplShare = g.Where(o => bnplByOrder.ContainsKey(o.Id)).Sum(o => bnplByOrder[o.Id]),
                PickupCount = g.Count(o => o.PaymentMethod == PaymentMethods.PayOnDelivery),
                DeliveryCount = g.Count(o => o.DeliveredAt != null),
            })
            .OrderBy(r => r.Date)
            .ToList();
        if (rows.Count == 0) return;
        await db.SalesDailyStats.Where(s => s.Date >= windowStart && s.Date <= today).ExecuteDeleteAsync(ct);
        db.AddRange(rows);
    }

    // ------------------------------------------------------------------------- helpers

    static IEnumerable<T> Rows<T>(int start, int end, Func<int, T> make)
    {
        for (var i = start; i < end; i++) yield return make(i);
    }

    static DeliveryZone ToZone((string Name, decimal Fee, string Eta, bool Pod)[] seeds, int i, DateTime now) =>
        new()
        {
            Name = seeds[i % seeds.Length].Name,
            Fee = seeds[i % seeds.Length].Fee,
            EtaText = seeds[i % seeds.Length].Eta,
            AllowPayOnDelivery = seeds[i % seeds.Length].Pod,
            SortOrder = i,
        };

    static Rider ToRider((string Name, string Phone, string Vehicle, string Service)[] seeds, int i, DateTime now) =>
        new()
        {
            Name = seeds[i % seeds.Length].Name,
            Phone = seeds[i % seeds.Length].Phone,
            Vehicle = seeds[i % seeds.Length].Vehicle,
            Service = seeds[i % seeds.Length].Service,
            CreatedAt = now.AddDays(-60),
        };

    static Category ToCategory((string Name, string Slug, string Icon, string Tagline)[] seeds, int i, DateTime now) =>
        new()
        {
            Name = seeds[i % seeds.Length].Name,
            Slug = seeds[i % seeds.Length].Slug,
            Icon = seeds[i % seeds.Length].Icon,
            Tagline = seeds[i % seeds.Length].Tagline,
            SortOrder = i,
            CreatedAt = now.AddDays(-120),
        };

    static List<Product> FormProducts(int start, int end, List<Guid> categories, DateTime now)
    {
        return [.. Rows(start, end, i =>
        {
            var categoryIndex = i % CategorySeeds.Length;
            var category = CategorySeeds[categoryIndex];
            var brand = Brands[i % Brands.Length];
            var nouns = CategoryNouns[categoryIndex];
            var noun = nouns[i / CategorySeeds.Length % nouns.Length];
            var variant = (char)('A' + i % 6);
            var name = $"{brand} {noun} {variant}{(i % 3 == 0 ? " Pro" : i % 3 == 1 ? " Max" : "")}";
            var basePrice = (categoryIndex switch { 1 => 1200m, 2 => 890m, 3 => 740m, 4 => 1500m, 5 => 480m, 6 => 260m, 0 => 180m, 7 => 320m, 8 => 45m, _ => 25m })
                + (i % 9) * 37m;
            var discounted = i % 10 < 3;
            return new Product
            {
                DemoKey = $"demo-42-product-{i:D4}",
                Slug = Slugify(name, i),
                Name = name,
                Brand = brand,
                CategoryId = categories[i % categories.Count],
                Summary = $"{category.Tagline} The {noun} {variant} from {brand} — {SpecSeeds[i % SpecSeeds.Length].Values[0]} and a {12 + (i % 3) * 12}-month warranty.",
                Description = $"The {name} sits in our {category.Name} range. Built around a {SpecSeeds[(i + 1) % SpecSeeds.Length].Values[0]} platform, it ships with {SpecSeeds[i % SpecSeeds.Length].Label.ToLowerInvariant()}: {string.Join(", ", SpecSeeds[i % SpecSeeds.Length].Values)}. TechLandier verifies every unit before it leaves the warehouse.",
                Price = Math.Round(basePrice, 2),
                CompareAtPrice = discounted ? Math.Round(basePrice * 1.35m, 2) : null,
                DiscountPercent = discounted ? Pricing.ClampDiscount(new[] { 10, 15, 20, 25, 30 }[i % 5]) : 0,
                SpecsJson = System.Text.Json.JsonSerializer.Serialize(
                    SpecSeeds.Select(s => new { label = s.Label, value = s.Values[i % s.Values.Length] }).ToList()),
                WarrantyMonths = (i % 5) switch { 0 => 0, 1 => 12, 2 => 12, 3 => 24, _ => 36 },
                IsInsurable = categoryIndex is 1 or 2 or 3 or 5 or 6,
                Stock = i % 13 == 0 ? 2 + i % 3 : 8 + i % 52,
                LowStockThreshold = 5,
                Status = i % 50 == 49 ? ProductStatuses.Draft : ProductStatuses.Active,
                PublishedAt = now.AddDays(-60),
                CreatedAt = now.AddDays(-60),
                UpdatedAt = now.AddDays(-60),
                Keywords = $"{brand} {name} {category.Name}",
            };
        })];
    }

    static List<(Order Order, OrderItem Item)> FormOrders(DemoSeedSize size, List<ProductRef> products, List<ZoneRef> zones, Guid[] customers, Faker f, DateTime now)
    {
        var list = new List<(Order, OrderItem)>();
        for (var i = 0; i < size.Orders; i++)
        {
            var p = products[i % products.Count];
            var customer = customers[i % customers.Length];
            var zone = zones[i % zones.Count];
            var qty = 1 + i % 3;
            var unit = Pricing.Display(p.Price, p.DiscountPercent);
            var subtotal = Math.Round(unit * qty, 2);
            var discount = Math.Round((p.Price - unit) * qty, 2);
            var status = (i % 20) switch
            {
                < 2 => OrderStatuses.Pending,
                < 4 => OrderStatuses.Paid,
                < 6 => OrderStatuses.Processing,
                < 11 => OrderStatuses.Shipped,
                < 17 => OrderStatuses.Delivered,
                < 18 => OrderStatuses.Cancelled,
                _ => OrderStatuses.Delivered,
            };
            var createdAt = now.AddDays(-(i % 45)).AddHours(i % 20);
            var paid = status is not (OrderStatuses.Pending or OrderStatuses.Cancelled);
            var paymentStatus = paid ? PaymentStatuses.Paid : PaymentStatuses.Unpaid;
            DateTime? shipped = status is OrderStatuses.Shipped or OrderStatuses.Delivered ? createdAt.AddDays(1) : null;
            DateTime? delivered = status == OrderStatuses.Delivered ? createdAt.AddDays(3) : null;
            var order = new Order
            {
                DemoKey = $"demo-42-order-{i:D5}",
                Number = $"TL-{createdAt.Year}-{(i + 1):D5}",
                UserId = customer,
                Status = status,
                PaymentMethod = i % 10 is 8 or 9 ? PaymentMethods.PayOnDelivery : i % 10 is 6 or 7 ? PaymentMethods.MobileMoney : PaymentMethods.Card,
                PaymentStatus = paymentStatus,
                Subtotal = subtotal,
                DiscountTotal = discount,
                ShippingFee = zone.Fee,
                Total = subtotal + zone.Fee,
                ZoneId = zone.Id,
                AddressSnapshot = new OrderAddress
                {
                    RecipientName = f.Name.FullName(),
                    Phone = f.Phone.PhoneNumber("+233 2# ### ####"),
                    StreetLabel = $"GA-{100 + i % 300}-{400 + i % 500} {f.Address.StreetName()}",
                },
                CheckoutAttemptId = $"demo-42-attempt-{i:D5}",
                AccessToken = Token($"order-{i}")[..16],
                ReceiptNumber = paid ? OrderUtils.NewReceiptNumber(createdAt.AddHours(1)) : null,
                PaidAt = paid ? createdAt.AddHours(1) : null,
                ShippedAt = shipped,
                DeliveredAt = delivered,
                CancelledAt = status == OrderStatuses.Cancelled ? createdAt.AddDays(1) : null,
                CancelReason = status == OrderStatuses.Cancelled ? "Customer changed their mind." : null,
                CreatedAt = createdAt,
                UpdatedAt = delivered ?? shipped ?? createdAt,
            };
            var item = new OrderItem
            {
                OrderId = order.Id,
                ProductId = p.Id,
                Name = p.Name,
                Sku = $"TL-{i:D5}",
                ImageArt = $"{p.CategoryIcon}:{p.Brand}",
                UnitPrice = unit,
                Qty = qty,
                LineTotal = subtotal,
            };
            list.Add((order, item));
        }
        return list;
    }

    static Payment FormPayment(OrderKey o)
    {
        var paid = o.PaymentStatus == PaymentStatuses.Paid;
        return new Payment
        {
            OrderId = o.Id,
            Method = o.PaymentMethod,
            ProviderTxRef = paid ? Token($"pay-{o.Number}")[..14].ToUpperInvariant() : null,
            Amount = o.Total,
            Status = paid ? PaymentStatuses.Paid : o.PaymentMethod == PaymentMethods.PayOnDelivery ? PaymentStatuses.Pending : PaymentStatuses.Unpaid,
            PaidAt = o.PaidAt,
            CreatedAt = o.CreatedAt,
        };
    }

    static IEnumerable<OrderEvent> FormEvents(OrderKey o)
    {
        yield return new OrderEvent { OrderId = o.Id, Status = OrderStatuses.Pending, Note = "Order placed; awaiting payment.", At = o.CreatedAt };
        if (o.PaidAt != null)
            yield return new OrderEvent { OrderId = o.Id, Status = OrderStatuses.Paid, Note = $"Payment received ({o.PaymentMethod.Replace('_', ' ')}).", At = o.PaidAt.Value };
        if (o.ShippedAt != null)
        {
            yield return new OrderEvent { OrderId = o.Id, Status = OrderStatuses.Processing, Note = "Picked and packed at the warehouse.", At = o.ShippedAt.Value.AddHours(-2) };
            yield return new OrderEvent { OrderId = o.Id, Status = OrderStatuses.Shipped, Note = "Handed to the delivery rider.", At = o.ShippedAt.Value };
        }
        if (o.DeliveredAt != null)
            yield return new OrderEvent { OrderId = o.Id, Status = OrderStatuses.Delivered, Note = "Delivered and signed for.", At = o.DeliveredAt.Value };
        if (o.CancelledAt != null)
            yield return new OrderEvent { OrderId = o.Id, Status = OrderStatuses.Cancelled, Note = o.CancelReason, At = o.CancelledAt.Value };
    }

    static (List<PayPlan> Plans, List<Installment> Installments) BuildPlans(DemoSeedSize size, List<OrderKey> orders)
    {
        var plans = new List<PayPlan>();
        var installments = new List<Installment>();
        // BNPL only for paid card / mobile-money orders (never pay-on-delivery).
        var eligible = orders.Where(o => o.PaidAt != null && o.PaymentMethod != PaymentMethods.PayOnDelivery).ToList();
        for (var i = 0; i < size.Plans && plans.Count < size.Plans && i * 7 < eligible.Count; i++)
        {
            var o = eligible[i * 7];
            if (plans.Any(p => p.OrderId == o.Id)) continue;
            var count = new[] { 3, 6, 12 }[i % 3];
            var fee = Math.Round(o.Total * Pricing.BnplFeePercent / 100m, 2, MidpointRounding.AwayFromZero);
            var per = Math.Round((o.Total + fee) / count, 2, MidpointRounding.AwayFromZero);
            var plan = new PayPlan
            {
                DemoKey = $"demo-42-plan-{i:D4}",
                OrderId = o.Id,
                UserId = o.UserId,
                InstallmentCount = count,
                AmountPerInstallment = per,
                FeeTotal = fee,
                StartedAt = o.PaidAt!.Value,
            };
            // One overdue story for the first plan (two paid, third overdue); one fully-paid plan at i == 1.
            var paidCount = i == 0 ? 2 : i == 1 ? count : i % 4;
            for (var n = 1; n <= count; n++)
            {
                var due = DateOnly.FromDateTime(o.PaidAt.Value.AddDays(14 * n));
                var isPaid = i == 0 ? n <= 2 : n <= paidCount;
                var overdue = i == 0 && n == 3;
                installments.Add(new Installment
                {
                    PlanId = plan.Id,
                    Number = n,
                    Amount = per,
                    DueDate = due,
                    PaidAt = isPaid ? due.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1) : null,
                    PaymentRef = isPaid ? Token($"inst-{i}-{n}")[..12].ToUpperInvariant() : null,
                    Status = isPaid ? InstallmentStatuses.Paid : overdue ? InstallmentStatuses.Overdue : InstallmentStatuses.Due,
                });
            }
            plans.Add(plan);
        }
        // Older plans are marked Completed with every installment paid. Keeping one active plan per customer leaves
        // room under the two-plan checkout cap, so demo logins can still try installments.
        foreach (var group in plans.GroupBy(plan => plan.UserId))
        {
            foreach (var stale in group.OrderByDescending(plan => plan.StartedAt).Skip(1))
            {
                stale.Status = PayPlanStatuses.Completed;
                stale.CompletedAt = stale.StartedAt.AddDays(14 * stale.InstallmentCount);
                foreach (var paid in installments.Where(i => i.PlanId == stale.Id))
                {
                    paid.Status = InstallmentStatuses.Paid;
                    paid.PaidAt = paid.DueDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1);
                    paid.PaymentRef ??= Token($"inst-{paid.PlanId}-{paid.Number}")[..12].ToUpperInvariant();
                }
            }
        }
        return (plans, installments);
    }

    static List<Delivery> BuildDeliveries(List<OrderKey> orders, List<Guid> riders)
    {
        var list = new List<Delivery>();
        var i = 0;
        foreach (var o in orders.Where(o => o.Status is OrderStatuses.Shipped or OrderStatuses.Delivered
            || (o.PaymentMethod == PaymentMethods.PayOnDelivery && o.Status is OrderStatuses.Pending or OrderStatuses.Paid or OrderStatuses.Processing)))
        {
            var failed = i % 17 == 0 && o.Status == OrderStatuses.Shipped;
            var status = failed ? DeliveryStatuses.Failed
                : o.Status == OrderStatuses.Shipped ? (i % 3 == 0 ? DeliveryStatuses.PickedUp : DeliveryStatuses.Assigned)
                : o.Status == OrderStatuses.Delivered ? DeliveryStatuses.Delivered
                : DeliveryStatuses.Unassigned;
            list.Add(new Delivery
            {
                OrderId = o.Id,
                ZoneId = o.ZoneId,
                RiderId = status == DeliveryStatuses.Unassigned ? null : riders[i % riders.Count],
                Status = status,
                CashCollected = o.PaymentMethod == PaymentMethods.PayOnDelivery && o.Status == OrderStatuses.Delivered ? o.Total : null,
                FeeCollected = o.PaymentMethod == PaymentMethods.PayOnDelivery && o.Status == OrderStatuses.Delivered ? o.ShippingFee : null,
                FailedReason = failed ? "Address unreachable after two attempts" : null,
                AssignedAt = status != DeliveryStatuses.Unassigned ? (o.ShippedAt ?? o.CreatedAt.AddHours(2)) : null,
                PickedUpAt = status is DeliveryStatuses.PickedUp or DeliveryStatuses.Delivered ? o.ShippedAt?.AddHours(3) : null,
                DeliveredAt = status == DeliveryStatuses.Delivered ? o.DeliveredAt : null,
                CreatedAt = o.ShippedAt ?? o.CreatedAt,
            });
            i++;
        }
        return list;
    }

    static List<Warranty> BuildWarranties(List<ItemRef> items, Dictionary<Guid, ProductRef> productById, int cap)
    {
        var list = new List<Warranty>();
        foreach (var item in items.Where(x => x.Status != OrderStatuses.Cancelled && (productById.GetValueOrDefault(x.ProductId)?.WarrantyMonths ?? 0) > 0))
        {
            if (list.Count >= cap) break;
            var start = DateOnly.FromDateTime(item.PaidAt ?? item.CreatedAt);
            list.Add(new Warranty
            {
                DemoKey = $"demo-42-warranty-{list.Count:D4}",
                OrderItemId = item.OrderItemId,
                UserId = item.UserId,
                SerialNumber = list.Count % 3 == 0 ? $"SN-{Token($"sn-{list.Count}")[..10].ToUpperInvariant()}" : null,
                StartOn = start,
                EndOn = start.AddMonths(productById[item.ProductId].WarrantyMonths),
            });
        }
        return list;
    }

    static List<DeviceInsurance> BuildInsurances(List<ItemRef> items, Dictionary<Guid, ProductRef> productById, int cap)
    {
        var list = new List<DeviceInsurance>();
        foreach (var item in items.Where(x => x.Status == OrderStatuses.Delivered && (productById.GetValueOrDefault(x.ProductId)?.IsInsurable ?? false)))
        {
            if (list.Count >= cap) break;
            var product = productById[item.ProductId];
            var start = DateOnly.FromDateTime(item.PaidAt ?? item.CreatedAt);
            list.Add(new DeviceInsurance
            {
                DemoKey = $"demo-42-insurance-{list.Count:D4}",
                UserId = item.UserId,
                OrderId = item.OrderId,
                ProductId = item.ProductId,
                DeviceName = product.Name,
                DeviceSerial = $"SN-{Token($"dev-{list.Count}")[..10].ToUpperInvariant()}",
                Premium = Math.Round(product.Price * Pricing.InsurancePremiumPercent, 2, MidpointRounding.AwayFromZero),
                CoverageAmount = Math.Round(product.Price * Pricing.InsuranceCoveragePercent, 2, MidpointRounding.AwayFromZero),
                StartOn = start,
                EndOn = start.AddYears(1),
            });
        }
        return list;
    }

    static List<RepairTicket> BuildRepairs(DemoSeedSize size, List<ProductRef> products, Guid[] customers, DateTime now)
    {
        var problems = new[]
        {
            "Screen flickers after waking from sleep.", "Battery drains within two hours.", "Will not pair over Bluetooth.",
            "Charging port only works at an angle.", "Keyboard keys stick.", "Overheats while charging.",
            "Speaker crackles at high volume.", "Cracked screen after a fall.",
        };
        var technicians = new[] { "Adjoa Fixer", "Ben Torsi", "Cynthia Ohene", "David Ampo" };
        var statuses = new[] { RepairStatuses.Received, RepairStatuses.Quoted, RepairStatuses.Repairing, RepairStatuses.Ready, RepairStatuses.PickedUp, RepairStatuses.Closed };
        return [.. Rows(0, size.Repairs, i =>
        {
            var product = products[i % products.Count];
            var status = statuses[i % statuses.Length];
            var created = now.AddDays(-(5 + i % 40));
            var history = "[]";
            foreach (var s in statuses)
            {
                history = RepairHistory.Append(history, new RepairEvent(s, null, created.AddDays(Array.IndexOf(statuses, s))));
                if (s == status) break;
            }
            return new RepairTicket
            {
                UserId = customers[i % customers.Length],
                DeviceName = product.Name,
                DeviceSerial = $"SN-{Token($"repair-{i}")[..10].ToUpperInvariant()}",
                Problem = problems[i % problems.Length],
                Status = status,
                QuotedCost = status == RepairStatuses.Received ? null : 25m + (i % 7) * 15m,
                TechnicianName = status == RepairStatuses.Received ? null : technicians[i % technicians.Length],
                AppointmentOn = status == RepairStatuses.Received ? DateOnly.FromDateTime(now.AddDays(2 + i % 5)) : null,
                StatusHistoryJson = history,
                CreatedAt = created,
                UpdatedAt = created.AddDays(2),
            };
        })];
    }

    static List<Review> BuildReviews(DemoSeedSize size, List<ItemRef> items, Guid[] customers)
    {
        var titles = new[] { "Exactly as described", "Solid for the price", "Battery could be better", "Impressive build", "Would buy again", "Good, not perfect" };
        var bodies = new[]
        {
            "Delivery was quick and the packaging was excellent. Three weeks in and it performs flawlessly.",
            "Great value at the sale price. Knocked a star off because the charger is proprietary.",
            "Does everything promised on the product page. TechLandier's warranty registration took two minutes.",
            "Feels premium in the hand. Battery life is a touch below the spec sheet under heavy use.",
            "Second device I have bought here — the BNPL schedule made it painless.",
        };
        var seen = new HashSet<(Guid, Guid)>();
        var list = new List<Review>();
        foreach (var item in items.Where(x => x.Status == OrderStatuses.Delivered).OrderBy(x => x.OrderItemId))
        {
            if (list.Count >= size.Reviews) break;
            if (!seen.Add((item.UserId, item.ProductId))) continue;
            var seedInt = Math.Abs(item.OrderItemId.GetHashCode());
            var rating = seedInt % 7 == 0 ? 2 : 3 + seedInt % 3;
            list.Add(new Review
            {
                DemoKey = $"demo-42-review-{list.Count:D4}",
                ProductId = item.ProductId,
                UserId = item.UserId,
                OrderItemId = item.OrderItemId,
                Rating = rating,
                Title = titles[seedInt % titles.Length],
                Body = bodies[seedInt % bodies.Length],
                Status = list.Count % 23 == 22 ? ReviewStatuses.Hidden : ReviewStatuses.Published,
                CreatedAt = (item.PaidAt ?? item.CreatedAt).AddDays(2),
                UpdatedAt = (item.PaidAt ?? item.CreatedAt).AddDays(2),
            });
        }
        return list;
    }

    static (List<Ticket> Tickets, List<TicketMessage> Messages) BuildTickets(DemoSeedSize size, List<OrderKey> orders, Guid[] customers, Guid adminId, Faker f, DateTime now)
    {
        var tickets = new List<Ticket>();
        var messages = new List<TicketMessage>();
        for (var i = 0; i < size.Tickets; i++)
        {
            var guest = i % 7 == 6;
            var order = i % 2 == 0 ? orders[i % orders.Count] : null;
            var created = now.AddDays(-(1 + i % 25)).AddHours(i % 12);
            var status = new[] { TicketStatuses.Received, TicketStatuses.Open, TicketStatuses.PendingCustomer, TicketStatuses.Resolved, TicketStatuses.Closed }[i % 5];
            var priority = new[] { TicketPriorities.Low, TicketPriorities.Normal, TicketPriorities.Normal, TicketPriorities.High, TicketPriorities.Urgent }[i % 5];
            var sla = priority switch { TicketPriorities.Urgent => 24, TicketPriorities.High => 48, TicketPriorities.Low => 168, _ => 72 };
            var ticket = new Ticket
            {
                DemoKey = $"demo-42-ticket-{i:D4}",
                Number = $"T-{1000 + i}",
                UserId = guest ? null : customers[i % customers.Length],
                GuestEmail = guest ? $"guest{i}@yopmail.com" : null,
                OrderId = order?.Id,
                Subject = TicketSubjects[i % TicketSubjects.Length],
                Category = TicketCategoryList[i % TicketCategoryList.Length],
                Status = status,
                Priority = priority,
                DueAt = created.AddHours(sla),
                FirstResponseAt = status == TicketStatuses.Received ? null : created.AddHours(2 + i % 10),
                ResolvedAt = status is TicketStatuses.Resolved or TicketStatuses.Closed ? created.AddDays(1 + i % 3) : null,
                LastMessageAt = created.AddHours(3),
                CreatedAt = created,
                UpdatedAt = created.AddHours(3),
            };
            messages.Add(new TicketMessage
            {
                TicketId = ticket.Id,
                AuthorUserId = guest ? null : ticket.UserId,
                IsStaff = false,
                Body = $"Hi — {TicketSubjects[i % TicketSubjects.Length].ToLowerInvariant()}. Order {order?.Number ?? "(guest)"}. Reported from the {(i % 2 == 0 ? "order page" : "account")}. Please help.",
                At = created,
            });
            if (ticket.FirstResponseAt != null)
                messages.Add(new TicketMessage
                {
                    TicketId = ticket.Id,
                    AuthorUserId = adminId,
                    IsStaff = true,
                    Body = "Thanks for the report — we have pulled the order details and are looking into it now. You will hear back within the SLA window.",
                    At = ticket.FirstResponseAt.Value,
                });
            tickets.Add(ticket);
        }
        return (tickets, messages);
    }

    static string Slugify(string value, int i) => $"{value.ToLowerInvariant().Replace(" ", "-").Replace("&", "and").Replace(",", "").Replace(".", "")}-{i:D4}";

    /// Deterministic token from input (hex, lowercase) — stable across seed resumes.
    static string Token(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Projection rows used between the bulk stages (kept tiny on purpose — the seeder never needs full entities).
    sealed record ProductRef(Guid Id, string Name, string Brand, decimal Price, int DiscountPercent, int Stock,
        int WarrantyMonths, bool IsInsurable, string Status, string CategoryIcon);
    sealed record ZoneRef(Guid Id, decimal Fee);
    sealed record OrderKey(Guid Id, string Number, Guid UserId, Guid? ZoneId, string Status, decimal Total, decimal Subtotal,
        decimal DiscountTotal, decimal ShippingFee, string PaymentMethod, string PaymentStatus, DateTime CreatedAt,
        DateTime? PaidAt, DateTime? ShippedAt, DateTime? DeliveredAt, DateTime? CancelledAt, string? CancelReason, string? ReceiptNumber);
    sealed record ItemRef(Guid OrderItemId, Guid OrderId, Guid UserId, string Status, DateTime? PaidAt, DateTime CreatedAt, Guid ProductId, int Qty);
}
