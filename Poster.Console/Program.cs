using Spectre.Console;
using Spectre.Console.Json;
using Poster.Core;
using Spectre.Console.Rendering;

// ── theme tokens ─────────────────────────────────────────────────────────────
// accent : oklch(65% 0.18 210)  → cyan-ish #2EA8C7
// surface: oklch(14% 0.01 240)  → near-black #1B1E23
// muted  : oklch(55% 0.01 240)  → #7E8390
// ─────────────────────────────────────────────────────────────────────────────

await TuiApp.RunAsync();

static class TuiApp
{
    public static async Task RunAsync()
    {
        Console.Title = "Poster";
        AnsiConsole.Clear();
        DrawBanner();

        var workspace = new Workspace();

        while (true)
        {
            var choice = MainMenu();

            switch (choice)
            {
                case MainChoice.NewRequest:
                    await RunNewRequestAsync(workspace, standalone: false);
                    break;
                case MainChoice.StandaloneRequest:
                    await RunNewRequestAsync(workspace, standalone: true);
                    break;
                case MainChoice.LoadWorkspace:
                    LoadWorkspaceFlow(workspace);
                    break;
                case MainChoice.SaveWorkspace:
                    SaveWorkspaceFlow(workspace);
                    break;
                case MainChoice.ViewHistory:
                    ViewHistory(workspace);
                    break;
                case MainChoice.Quit:
                    AnsiConsole.MarkupLine("[grey]bye.[/]");
                    return;
            }
        }
    }

    // ── banner ────────────────────────────────────────────────────────────────

    static void DrawBanner()
    {
        AnsiConsole.Write(
            new FigletText("Poster")
                .LeftJustified()
                .Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]lightweight TUI API tester[/]");
        AnsiConsole.WriteLine();
    }

    // ── main menu ─────────────────────────────────────────────────────────────

    enum MainChoice
    {
        NewRequest,
        StandaloneRequest,
        LoadWorkspace,
        SaveWorkspace,
        ViewHistory,
        Quit
    }

    static MainChoice MainMenu()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<MainChoice>()
                .Title("[cyan1]What do you want to do?[/]")
                .UseConverter(c => c switch
                {
                    MainChoice.NewRequest        => "New request  (workspace)",
                    MainChoice.StandaloneRequest => "New request  (standalone)",
                    MainChoice.LoadWorkspace     => "Load workspace",
                    MainChoice.SaveWorkspace     => "Save workspace",
                    MainChoice.ViewHistory       => "View request history",
                    MainChoice.Quit              => "Quit",
                    _                            => c.ToString()
                })
                .AddChoices(Enum.GetValues<MainChoice>()));
    }

    // ── new request flow ──────────────────────────────────────────────────────

    static async Task RunNewRequestAsync(Workspace workspace, bool standalone)
    {
        AnsiConsole.WriteLine();

        // ── name
        string name = AnsiConsole.Ask<string>("Request name: ", "New Request");

        // ── base URL (workspace default or per-request)
        string baseUrl = AnsiConsole.Ask<string>("Base URL: ", "http://127.0.0.1:8000");

        // ── HTTP method
        var method = AnsiConsole.Prompt(
            new SelectionPrompt<HttpMethod>()
                .Title("HTTP method:")
                .UseConverter(m => m.Method)
                .AddChoices(
                    HttpMethod.Get,
                    HttpMethod.Post,
                    HttpMethod.Put,
                    HttpMethod.Delete,
                    HttpMethod.Patch));

        // ── route
        string route = AnsiConsole.Ask<string>("Route: ", "/health");
        if (!route.StartsWith('/')) route = $"/{route}";

        // ── body (only for non-GET)
        string body = "";
        if (method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            body = AnsiConsole.Ask<string>("JSON body: ", "{}");
        }

        // ── build request
        var req = standalone
            ? new PosterReqRes
              {
                  HttpMethod = method,
                  Route      = $"{baseUrl}{route}",
                  Name       = name,
                  Body       = body
              }
            : new PosterReqRes(workspace)
              {
                  HttpMethod = method,
                  Route      = $"{baseUrl}{route}",
                  Name       = name,
                  Body       = body
              };

        if (!standalone)
            workspace.Requests.Add(req);

        // ── send with spinner
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots2)
            .SpinnerStyle(Style.Parse("cyan1"))
            .StartAsync("[grey]Sending…[/]", async _ => await req.SendAsync());

        // ── render result
        RenderResult(req);
    }

    // ── result panel ──────────────────────────────────────────────────────────

    static void RenderResult(PosterReqRes req)
    {
        AnsiConsole.WriteLine();

        if (req.Error is not null)
        {
            AnsiConsole.Write(
                new Panel($"[red]{Markup.Escape(req.Error)}[/]")
                    .Header("[red bold] Error [/]", Justify.Center)
                    .RoundedBorder()
                    .BorderColor(Color.Red));
            return;
        }

        if (req.ResponseMessage is null)
        {
            AnsiConsole.MarkupLine("[red]No response received.[/]");
            return;
        }

        var resp     = req.ResponseMessage;
        var metric   = req.Metric;
        bool success = resp.IsSuccessStatusCode;

        // read body (already consumed in SendAsync via ReadAsStringAsync,
        // so we buffer it once inside PosterReqRes and expose it — see note below)
        string responseBody = req.ResponseBody ?? "(empty)";

        string statusColor  = success ? "green" : "red";
        string statusLabel  = $"[{statusColor} bold]{(int)resp.StatusCode} {resp.StatusCode}[/]";
        string metricLabel  =
            $"[grey]{metric.ElapsedTime} ms  " +
            $"↑ {FormatBytes(metric.RequestSize)}  " +
            $"↓ {FormatBytes(metric.ResponseSize)}[/]";

        string headerText = $"{statusLabel}  {metricLabel}";

        IRenderable body;
        if (success && LooksLikeJson(responseBody))
        {
            body = new JsonText(responseBody);
        }
        else
        {
            body = new Markup(Markup.Escape(responseBody));
        }

        AnsiConsole.Write(
            new Panel(body)
                .Header($" {Markup.Escape(req.Name)} ", Justify.Left)
                .RoundedBorder()
                .BorderColor(success ? Color.Cyan1 : Color.Red)
                .Expand());
    }

    // ── workspace: load ───────────────────────────────────────────────────────

    static void LoadWorkspaceFlow(Workspace workspace)
    {
        var files = workspace.ListLocal();
        if (files.Length == 0)
        {
            AnsiConsole.MarkupLine("[grey]No workspace files found in the current directory.[/]");
            return;
        }

        var chosen = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan1]Choose a workspace file:[/]")
                .AddChoices(files));

        workspace.Load(chosen);
        AnsiConsole.MarkupLine($"[green]Loaded:[/] [grey]{Markup.Escape(workspace.Name)}[/] " +
                               $"([grey]{workspace.Requests.Count} requests[/])");
    }

    // ── workspace: save ───────────────────────────────────────────────────────

    static void SaveWorkspaceFlow(Workspace workspace)
    {
        workspace.Name = AnsiConsole.Ask("Workspace name: ", workspace.Name);
        workspace.Save();
        AnsiConsole.MarkupLine($"[green]Saved[/] → [grey]Workspace-{workspace.Id}.json[/]");
    }

    // ── history ───────────────────────────────────────────────────────────────

    static void ViewHistory(Workspace workspace)
    {
        if (workspace.Requests.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No requests in this workspace yet.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[cyan1]#[/]").RightAligned())
            .AddColumn("[cyan1]Name[/]")
            .AddColumn("[cyan1]Method[/]")
            .AddColumn("[cyan1]Route[/]")
            .AddColumn("[cyan1]Status[/]")
            .AddColumn("[cyan1]Time[/]")
            .AddColumn("[cyan1]↓ Size[/]");

        int i = 1;
        foreach (var req in workspace.Requests)
        {
            string status = req.Error is not null
                ? "[red]error[/]"
                : req.ResponseMessage is null
                    ? "[grey]—[/]"
                    : req.ResponseMessage.IsSuccessStatusCode
                        ? $"[green]{(int)req.ResponseMessage.StatusCode}[/]"
                        : $"[red]{(int)req.ResponseMessage.StatusCode}[/]";

            table.AddRow(
                $"[grey]{i++}[/]",
                Markup.Escape(req.Name),
                $"[cyan1]{req.HttpMethod.Method}[/]",
                $"[grey]{Markup.Escape(req.Route)}[/]",
                status,
                $"[grey]{req.Metric.ElapsedTime} ms[/]",
                $"[grey]{FormatBytes(req.Metric.ResponseSize)}[/]");
        }

        AnsiConsole.Write(table);

        // allow re-run of a history item
        bool rerun = AnsiConsole.Confirm("Re-run a request?", defaultValue: false);
        if (!rerun) return;

        int idx = AnsiConsole.Ask<int>("Request number: ", 1) - 1;
        if (idx < 0 || idx >= workspace.Requests.Count)
        {
            AnsiConsole.MarkupLine("[red]Invalid number.[/]");
            return;
        }

        var chosen = workspace.Requests[idx];
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots2)
            .SpinnerStyle(Style.Parse("cyan1"))
            .Start("[grey]Sending…[/]", _ => chosen.SendAsync().GetAwaiter().GetResult());

        RenderResult(chosen);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    static string FormatBytes(long bytes) => bytes switch
    {
        < 1024                 => $"{bytes} B",
        < 1024 * 1024          => $"{bytes / 1024.0:F1} KB",
        _                      => $"{bytes / (1024.0 * 1024):F1} MB"
    };

    static bool LooksLikeJson(string s)
    {
        var t = s.TrimStart();
        return t.StartsWith('{') || t.StartsWith('[');
    }
}
