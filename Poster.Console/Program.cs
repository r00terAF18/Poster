using Spectre.Console;
using Poster.Core;
using System.Net.Http;
using Spectre.Console.Rendering;
using Spectre.Console.Json;

namespace Poster.Console;

internal enum MainChoice
{
    NewRequestWorkspace,
    NewRequestStandalone,
    ManageVariables,
    ImportOpenApi,
    LoadWorkspace,
    SaveWorkspace,
    ViewHistory,
    Quit,
}

internal static class TuiApp
{
    private static Workspace _workspace = new();

    public static async Task Main(string[] args)
    {
        DrawBanner();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<MainChoice>()
                    .Title("[grey]Main menu[/]")
                    .UseConverter(ChoiceLabel)
                    .AddChoices(Enum.GetValues<MainChoice>()));

            switch (choice)
            {
                case MainChoice.NewRequestWorkspace:
                    await RunNewRequestAsync(standalone: false);
                    break;
                case MainChoice.NewRequestStandalone:
                    await RunNewRequestAsync(standalone: true);
                    break;
                case MainChoice.ManageVariables:
                    ManageVariables();
                    break;
                case MainChoice.ImportOpenApi:
                    await ImportOpenApiAsync();
                    break;
                case MainChoice.LoadWorkspace:
                    LoadWorkspaceFlow();
                    break;
                case MainChoice.SaveWorkspace:
                    SaveWorkspaceFlow();
                    break;
                case MainChoice.ViewHistory:
                    await ViewHistoryAsync();
                    break;
                case MainChoice.Quit:
                    return;
            }
        }
    }

    // ── Banner ────────────────────────────────────────────────────────────────

    private static void DrawBanner()
    {
        AnsiConsole.Write(new FigletText("Poster").Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]API testing tool  |  offline  |  v5[/]\n");
    }

    // ── New Request ───────────────────────────────────────────────────────────

    private static async Task RunNewRequestAsync(bool standalone)
    {
        var name = AnsiConsole.Ask<string>("Request [cyan]name[/]:", "New Request");

        // Method selection — includes all supported verbs
        var method = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("HTTP [cyan]method[/]:")
                .AddChoices("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"));

        var route = AnsiConsole.Ask<string>("[cyan]Route[/] (supports {{base_url}}):");
        if (!route.StartsWith("http") && !route.Contains("{{"))
            route = "/" + route.TrimStart('/');

        var httpMethod = ParseMethod(method);

        var req = standalone
            ? new PosterReqRes { Name = name, Route = route, HttpMethod = httpMethod }
            : new PosterReqRes(_workspace) { Name = name, Route = route, HttpMethod = httpMethod };

        // Body
        bool wantsBody = httpMethod != HttpMethod.Get &&
                         httpMethod != HttpMethod.Delete &&
                         httpMethod != HttpMethod.Head &&
                         httpMethod != HttpMethod.Options;
        if (wantsBody)
        {
            var body = AnsiConsole.Ask<string>("JSON [cyan]body[/] (leave blank to skip):", "");
            if (!string.IsNullOrWhiteSpace(body)) req.Body = body;
        }

        // Custom headers
        if (AnsiConsole.Confirm("Add custom [cyan]headers[/]?", defaultValue: false))
            AddHeaders(req);

        // Auth
        var authType = AnsiConsole.Prompt(
            new SelectionPrompt<AuthType>()
                .Title("[cyan]Authentication[/]:")
                .AddChoices(Enum.GetValues<AuthType>()));

        ConfigureAuth(req, authType);

        if (!standalone)
            _workspace.Requests.Add(req);

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Sending…", async _ => await req.SendAsync());

        RenderResult(req);
    }

    // ── Header input ──────────────────────────────────────────────────────────

    private static void AddHeaders(PosterReqRes req)
    {
        while (true)
        {
            var key = AnsiConsole.Ask<string>("Header [cyan]name[/] (blank to stop):", "");
            if (string.IsNullOrWhiteSpace(key)) break;
            var value = AnsiConsole.Ask<string>($"Value for [cyan]{key}[/]:");
            req.Headers[key] = value;
        }
    }

    // ── Auth configuration ────────────────────────────────────────────────────

    private static void ConfigureAuth(PosterReqRes req, AuthType type)
    {
        req.Auth.Type = type;
        switch (type)
        {
            case AuthType.Bearer:
                req.Auth.Token = AnsiConsole.Ask<string>("Bearer [cyan]token[/]:");
                break;
            case AuthType.Basic:
                req.Auth.Username = AnsiConsole.Ask<string>("[cyan]Username[/]:");
                req.Auth.Password = AnsiConsole.Ask<string>("[cyan]Password[/]:");
                break;
            case AuthType.ApiKey:
                req.Auth.ApiKeyHeader = AnsiConsole.Ask<string>("Header name:", "X-API-Key");
                req.Auth.Token = AnsiConsole.Ask<string>("[cyan]API key[/]:");
                break;
        }
    }

    // ── Variable management ───────────────────────────────────────────────────

    private static void ManageVariables()
    {
        AnsiConsole.MarkupLine($"\n[bold]Variables[/] — workspace [cyan]{_workspace.Name}[/]\n");

        if (_workspace.Variables.Count > 0)
        {
            var tbl = new Table().BorderColor(Color.Grey).Border(TableBorder.Rounded);
            tbl.AddColumn("Name").AddColumn("Value");
            foreach (var (k, v) in _workspace.Variables)
                tbl.AddRow($"[cyan]{k}[/]", v);
            AnsiConsole.Write(tbl);
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]No variables set.[/]");
        }

        AnsiConsole.WriteLine();
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Action:")
                .AddChoices("Set / update variable", "Delete variable", "Back"));

        if (action == "Set / update variable")
        {
            var key = AnsiConsole.Ask<string>("Variable [cyan]name[/]:");
            var value = AnsiConsole.Ask<string>($"Value for [cyan]{{{{{key}}}}}[/]:");
            _workspace.Variables[key] = value;
            AnsiConsole.MarkupLine($"[green]Set[/] [cyan]{{{{{key}}}}}[/] = {value}");
        }
        else if (action == "Delete variable")
        {
            if (_workspace.Variables.Count == 0) return;
            var key = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Delete which variable?")
                    .AddChoices(_workspace.Variables.Keys));
            _workspace.Variables.Remove(key);
            AnsiConsole.MarkupLine($"[red]Removed[/] [cyan]{{{{{key}}}}}[/]");
        }
    }

    // ── OpenAPI import ────────────────────────────────────────────────────────

    private static async Task ImportOpenApiAsync()
    {
        var source = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Import OpenAPI spec from:")
                .AddChoices("Local JSON file", "URL"));

        try
        {
            if (source == "Local JSON file")
            {
                var path = AnsiConsole.Ask<string>("File [cyan]path[/]:");
                _workspace.ImportFromOpenApiFile(path);
            }
            else
            {
                var url = AnsiConsole.Ask<string>("[cyan]URL[/]:");
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Fetching spec…", _ =>
                    {
                        _workspace.ImportFromOpenApiUrl(url);
                        return Task.CompletedTask;
                    });
            }

            AnsiConsole.MarkupLine(
                $"[green]Imported[/] {_workspace.Requests.Count} request(s) into workspace.");

            // Show any base_url that was auto-set
            if (_workspace.Variables.TryGetValue("base_url", out var baseUrl))
                AnsiConsole.MarkupLine($"[grey]base_url set to:[/] [cyan]{baseUrl}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Import failed:[/] {ex.Message}");
        }
    }

    // ── Result rendering ──────────────────────────────────────────────────────

    private static void RenderResult(PosterReqRes req)
    {
        if (!string.IsNullOrEmpty(req.Error))
        {
            AnsiConsole.Write(new Panel($"[red]{req.Error}[/]")
                .Header("[red]Error[/]").BorderColor(Color.Red));
            return;
        }

        if (req.ResponseMessage == null) return;

        var statusCode = (int)req.ResponseMessage.StatusCode;
        var statusColor = statusCode is >= 200 and < 300 ? "green" : "red";
        var header = $"[{statusColor}]{statusCode}[/]  " +
                     $"[grey]{req.Metric.ElapsedTime} ms  " +
                     $"↑ {FormatBytes(req.Metric.RequestSize)}  " +
                     $"↓ {FormatBytes(req.Metric.ResponseSize)}[/]";

        var body = req.ResponseBody ?? "";
        IRenderable content = LooksLikeJson(body)
            ? new JsonText(body)
            : new Markup(body.EscapeMarkup());

        AnsiConsole.Write(new Panel(content)
            .Header(header)
            .BorderColor(statusCode is >= 200 and < 300 ? Color.Green : Color.Red));
    }

    // ── Workspace flows ───────────────────────────────────────────────────────

    private static void LoadWorkspaceFlow()
    {
        var files = _workspace.ListLocal();
        if (files.Length == 0)
        {
            AnsiConsole.MarkupLine("[grey]No workspace files found.[/]");
            return;
        }

        var file = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Load which workspace?")
                .AddChoices(files));

        _workspace.Load(file);
        AnsiConsole.MarkupLine(
            $"[green]Loaded[/] [cyan]{_workspace.Name}[/] " +
            $"({_workspace.Requests.Count} request(s))");
    }

    private static void SaveWorkspaceFlow()
    {
        _workspace.Name = AnsiConsole.Ask("Workspace [cyan]name[/]:", _workspace.Name);
        _workspace.Save();
        AnsiConsole.MarkupLine($"[green]Saved[/] Workspace-{_workspace.Id}.json");
    }

    // ── History ───────────────────────────────────────────────────────────────

    private static async Task ViewHistoryAsync()
    {
        if (_workspace.Requests.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No requests in workspace.[/]");
            return;
        }

        var tbl = new Table().BorderColor(Color.Grey).Border(TableBorder.Rounded);
        tbl.AddColumn("#")
           .AddColumn("Name")
           .AddColumn("Method")
           .AddColumn("Route")
           .AddColumn("Last run")
           .AddColumn("Time (ms)")
           .AddColumn("↓ Size");

        for (int i = 0; i < _workspace.Requests.Count; i++)
        {
            var r = _workspace.Requests[i];
            tbl.AddRow(
                $"{i + 1}",
                r.Name,
                r.HttpMethod.Method,
                r.Route,
                r.LastExec == default ? "[grey]-[/]" : r.LastExec.ToString("HH:mm:ss"),
                r.Metric.ElapsedTime.ToString(),
                FormatBytes(r.Metric.ResponseSize));
        }
        AnsiConsole.Write(tbl);

        var pick = AnsiConsole.Ask<string>(
            "Re-run a request by [cyan]#[/] (or blank to go back):", "");
        if (int.TryParse(pick, out var idx) && idx >= 1 && idx <= _workspace.Requests.Count)
        {
            var req = _workspace.Requests[idx - 1];
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Sending…", async _ => await req.SendAsync());
            RenderResult(req);
        }
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static HttpMethod ParseMethod(string s) => s.ToUpperInvariant() switch
    {
        "POST" => HttpMethod.Post,
        "PUT" => HttpMethod.Put,
        "PATCH" => HttpMethod.Patch,
        "DELETE" => HttpMethod.Delete,
        "HEAD" => HttpMethod.Head,
        "OPTIONS" => HttpMethod.Options,
        _ => HttpMethod.Get,
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024):F1} MB",
    };

    private static bool LooksLikeJson(string s)
    {
        var t = s.TrimStart();
        return t.StartsWith('{') || t.StartsWith('[');
    }

    private static string ChoiceLabel(MainChoice c) => c switch
    {
        MainChoice.NewRequestWorkspace => "New request (workspace)",
        MainChoice.NewRequestStandalone => "New request (standalone)",
        MainChoice.ManageVariables => "Manage variables",
        MainChoice.ImportOpenApi => "Import OpenAPI spec",
        MainChoice.LoadWorkspace => "Load workspace",
        MainChoice.SaveWorkspace => "Save workspace",
        MainChoice.ViewHistory => "View request history",
        MainChoice.Quit => "Quit",
        _ => c.ToString(),
    };
}
