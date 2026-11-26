using System.Diagnostics;
using Microsoft.Playwright;

namespace KitchenSink.BrowserTests;

/// <summary>
/// Starts the real Kestrel app on a free port, then drives it with Chromium. Opt in with
/// <c>RUN_BROWSER_TESTS=1</c> after installing Playwright's browsers:
/// <c>pwsh bin/Debug/net10.0/playwright.ps1 install chromium</c>.
/// </summary>
public sealed class BrowserAppFixture : IAsyncLifetime
{
    public bool Enabled { get; private set; }
    public string UnavailableReason { get; private set; } = "";
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";

    readonly string _dataDir = Path.Combine(Path.GetTempPath(), "ks-browser-" + Guid.NewGuid().ToString("N"));
    Process? _app;
    Task<string>? _stdout;
    Task<string>? _stderr;
    IPlaywright? _playwright;
    IBrowser? _browser;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("RUN_BROWSER_TESTS") != "1")
        {
            UnavailableReason = "Set RUN_BROWSER_TESTS=1 to run the Playwright suite.";
            return;
        }

        try
        {
            var root = SampleRoot();
            var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}")
                ? "Release"
                : "Debug";
            var appDll = Path.Combine(root, "src", "KitchenSink", "bin", configuration, "net10.0", "KitchenSink.dll");
            if (!File.Exists(appDll))
            {
                UnavailableReason = $"The app was not built at {appDll}.";
                return;
            }

            var port = FreePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            Directory.CreateDirectory(_dataDir);

            var start = new ProcessStartInfo("dotnet", $"\"{appDll}\" --urls {BaseUrl}")
            {
                WorkingDirectory = Path.Combine(root, "src", "KitchenSink"),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["App__DataDir"] = _dataDir;
            start.Environment["DATABASE_PATH"] = Path.Combine(_dataDir, "browser.db");
            start.Environment["Demo__GlobalRateLimit"] = "false";
            start.Environment["Demo__DailyReset"] = "false";
            _app = Process.Start(start) ?? throw new InvalidOperationException("The app process did not start.");
            _stdout = _app.StandardOutput.ReadToEndAsync();
            _stderr = _app.StandardError.ReadToEndAsync();

            using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            for (var attempt = 0; attempt < 120; attempt++)
            {
                if (_app.HasExited)
                {
                    UnavailableReason = $"The app exited with code {_app.ExitCode}.";
                    return;
                }

                try
                {
                    using var health = await http.GetAsync("/health");
                    if (health.IsSuccessStatusCode)
                    {
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(250);
            }

            _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            Playwright = _playwright;
            Browser = _browser;
            Enabled = true;
        }
        catch (Exception exception)
        {
            UnavailableReason = exception.Message;
        }
    }

    public async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
        });
        var page = await context.NewPageAsync();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                Console.Error.WriteLine($"[browser] {message.Text}");
            }
        };
        return page;
    }

    public async Task SignInAsync(IPage page)
    {
        await page.GotoAsync("/login");
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();
        await page.WaitForURLAsync("**/dashboard");
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();

        if (_app is { HasExited: false })
        {
            try
            {
                _app.Kill(entireProcessTree: true);
                await _app.WaitForExitAsync();
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (_stdout is not null)
        {
            await _stdout;
        }

        if (_stderr is not null)
        {
            await _stderr;
        }

        _app?.Dispose();

        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    static string SampleRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "KitchenSink.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the sample root.");
    }

    static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

[CollectionDefinition("Browser")]
public sealed class BrowserCollection : ICollectionFixture<BrowserAppFixture>;
