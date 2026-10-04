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
            MainChoice choice = AnsiConsole.Prompt(
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
        string name = AnsiConsole.Ask<string>("Request [cyan]name[/]:", "New Request");

        // Method selection — includes all supported verbs
        string method = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("HTTP [cyan]method[/]:")
                .AddChoices("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "Back"));
        if (method == "Back") return;

        string route = AnsiConsole.Ask<string>("[cyan]Route[/] (supports {{base_url}}):");
        if (!route.StartsWith("http") && !route.Contains("{{"))
            route = "/" + route.TrimStart('/');

        HttpMethod httpMethod = ParseMethod(method);

        PosterReqRes req = standalone
            ? new PosterReqRes { Name = name, Route = route, HttpMethod = httpMethod }
            : new PosterReqRes(_workspace) { Name = name, Route = route, HttpMethod = httpMethod };

        // Body
        bool wantsBody = httpMethod != HttpMethod.Get &&
                         httpMethod != HttpMethod.Delete &&
                         httpMethod != HttpMethod.Head &&
                         httpMethod != HttpMethod.Options;
        if (wantsBody)
        {
            ConfigureRequestContent(req);
        }

        if (AnsiConsole.Confirm("Add query [cyan]parameters[/]?", defaultValue: false))
            AddQueryParams(req);

        // Custom headers
        if (AnsiConsole.Confirm("Add custom [cyan]headers[/]?", defaultValue: false))
            AddHeaders(req);

        // Auth
        AuthType authType = AnsiConsole.Prompt(
            new SelectionPrompt<AuthType>()
                .Title("[cyan]Authentication[/]:")
                .AddChoices(Enum.GetValues<AuthType>()));

        ConfigureAuth(req, authType);

        if (!standalone)
            _workspace.Requests.Add(req);

        await SendRequestAsync(req);

        RenderResult(req);
    }

    private static void ConfigureRequestContent(PosterReqRes req)
    {
        string contentType = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Request [cyan]content[/]:")
                .AddChoices("None", "Text / JSON body", "Multipart file upload"));

        if (contentType == "Text / JSON body")
        {
            req.Body = AnsiConsole.Ask<string>("Request [cyan]body[/] (leave blank to skip):", "");
            return;
        }

        if (contentType != "Multipart file upload") return;

        string fieldName = AnsiConsole.Ask<string>("Multipart field [cyan]name[/]:", "file");
        while (true)
        {
            string filePath = AnsiConsole.Ask<string>(
                "File [cyan]path[/] (blank to finish):", "");
            if (string.IsNullOrWhiteSpace(filePath)) break;

            filePath = Environment.ExpandEnvironmentVariables(filePath.Trim().Trim('"'));
            if (!File.Exists(filePath))
            {
                AnsiConsole.MarkupLine($"[red]File not found:[/] {filePath.EscapeMarkup()}");
                continue;
            }

            req.Files.Add(new FileUpload { FilePath = filePath, FieldName = fieldName });
            AnsiConsole.MarkupLine($"[green]Added:[/] {filePath.EscapeMarkup()}");
        }

        if (req.Files.Count == 0)
            AnsiConsole.MarkupLine("[yellow]No files selected; the request will have no body.[/]");
    }

    // ── Header input ──────────────────────────────────────────────────────────

    private static void AddHeaders(PosterReqRes req)
    {
        while (true)
        {
            string key = AnsiConsole.Ask<string>("Header [cyan]name[/] (blank to stop):", "");
            if (string.IsNullOrWhiteSpace(key)) break;
            string value = AnsiConsole.Ask<string>($"Value for [cyan]{key}[/]:");
            req.Headers[key] = value;
        }
    }

    private static void AddQueryParams(PosterReqRes req)
    {
        while (true)
        {
            string key = AnsiConsole.Ask<string>("Query [cyan]name[/] (blank to stop):", "");
            if (string.IsNullOrWhiteSpace(key)) break;
            string value = AnsiConsole.Ask<string>($"Value for [cyan]{key}[/]:");
            req.QueryParams[key] = value;
        }
    }

    private static async Task SendRequestAsync(PosterReqRes req)
    {
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        System.Console.CancelKeyPress += cancelHandler;
        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Sending… (Ctrl+C to cancel)", async _ =>
                    await req.SendAsync(cancellation.Token));
        }
        finally
        {
            System.Console.CancelKeyPress -= cancelHandler;
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
            Table tbl = new Table().BorderColor(Color.Grey).Border(TableBorder.Rounded);
            tbl.AddColumn("Name").AddColumn("Value");
            foreach ((string k, string v) in _workspace.Variables)
                tbl.AddRow($"[cyan]{k}[/]", v);
            AnsiConsole.Write(tbl);
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]No variables set.[/]");
        }

        AnsiConsole.WriteLine();
        string action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Action:")
                .AddChoices("Set / update variable", "Delete variable", "Back"));

        if (action == "Set / update variable")
        {
            string key = AnsiConsole.Ask<string>("Variable [cyan]name[/]:");
            string value = AnsiConsole.Ask<string>($"Value for [cyan]{{{{{key}}}}}[/]:");
            _workspace.Variables[key] = value;
            AnsiConsole.MarkupLine($"[green]Set[/] [cyan]{{{{{key}}}}}[/] = {value}");
        }
        else if (action == "Delete variable")
        {
            if (_workspace.Variables.Count == 0) return;
            string key = AnsiConsole.Prompt(
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
        string source = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Import OpenAPI spec from:")
                .AddChoices("Local JSON file", "URL", "Back"));
        if (source == "Back") return;

        try
        {
            if (source == "Local JSON file")
            {
                string path = AnsiConsole.Ask<string>("File [cyan]path[/]:");
                _workspace.ImportFromOpenApiFile(path);
            }
            else
            {
                string url = AnsiConsole.Ask<string>("[cyan]URL[/]:");
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
            if (_workspace.Variables.TryGetValue("base_url", out string? baseUrl))
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

        int statusCode = (int)req.ResponseMessage.StatusCode;
        string statusColor = statusCode is >= 200 and < 300 ? "green" : "red";
        string header = $"[{statusColor}]{statusCode}[/]  " +
                        $"[grey]{req.Metric.ElapsedTime} ms  " +
                        $"↑ {FormatBytes(req.Metric.RequestSize)}  " +
                        $"↓ {FormatBytes(req.Metric.ResponseSize)}[/]";

        string body = req.ResponseBody ?? "";
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
        string[] files = _workspace.ListLocal();
        if (files.Length == 0)
        {
            AnsiConsole.MarkupLine("[grey]No workspace files found.[/]");
            return;
        }

        string file = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Load which workspace?")
                .AddChoices(files.Append("Back")));
        if (file == "Back") return;

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

        Table tbl = new Table().BorderColor(Color.Grey).Border(TableBorder.Rounded);
        tbl.AddColumn("#")
           .AddColumn("Name")
           .AddColumn("Method")
           .AddColumn("Route")
           .AddColumn("Last run")
           .AddColumn("Time (ms)")
           .AddColumn("↓ Size");

        for (int i = 0; i < _workspace.Requests.Count; i++)
        {
            PosterReqRes r = _workspace.Requests[i];
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

        string pick = AnsiConsole.Ask<string>(
            "Select a request by [cyan]#[/] (or blank to go back):", "");
        if (int.TryParse(pick, out int idx) && idx >= 1 && idx <= _workspace.Requests.Count)
        {
            PosterReqRes req = _workspace.Requests[idx - 1];
            string action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Request [cyan]{req.Name}[/]")
                    .AddChoices("Run request", "Delete request", "Back"));
            if (action == "Run request")
            {
                await SendRequestAsync(req);
                RenderResult(req);
            }
            else if (action == "Delete request")
            {
                _workspace.Requests.RemoveAt(idx - 1);
                AnsiConsole.MarkupLine("[green]Request deleted.[/]");
            }
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
        string t = s.TrimStart();
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
