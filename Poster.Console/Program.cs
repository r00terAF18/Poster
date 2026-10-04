using Spectre.Console;
using System.Diagnostics;
using Spectre.Console.Json;
using System.Diagnostics.Tracing;
using Poster.Core;


AnsiConsole.MarkupLine("[bold cyan]Poster![/]");
AnsiConsole.MarkupLine("TUI API Testing tool!");

string base_url = AnsiConsole.Ask<string>("Base URL please: ", "http://127.0.0.1:8000");


string str_http_method = AnsiConsole.Prompt(new SelectionPrompt<string>()
    .Title("Choose Http method:")
    .AddChoices(Enum.GetNames<HttpMethod>()));

string str_route = AnsiConsole.Ask<string>("Route: ", "health");

if (!str_route.StartsWith("/"))
    str_route = $"/{str_route}";

using HttpClient client = new();

HttpResponseMessage response = null;
long request_size = 0;
int response_size = 0;

Stopwatch watch = new();

try
{
    switch (str_http_method)
    {
        case "GET":
            watch.Start();
            response = await client.GetAsync(new Uri($"{base_url}{str_route}"));
            break;
        case "POST":
            string body = AnsiConsole.Ask<string>("Json Body: ", "{\"data\": 1}");
            request_size = System.Text.Encoding.UTF8.GetByteCount(body);
            StringContent http_content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            watch.Start();
            response = await client.PostAsync(new Uri($"{base_url}{str_route}"), http_content);
            break;
        default:
            AnsiConsole.MarkupLine("[bold red]No valid http method was selected![/]");
            break;
    }
}
catch (HttpRequestException ex)
{

    AnsiConsole.MarkupLineInterpolated($"[bold red]Error![/]\n{ex.Message}");
    return;
}


watch.Stop();

if (response == null)
{
    AnsiConsole.MarkupLine("[bold red]No response received![/]");
    return;
}


string content = await response.Content.ReadAsStringAsync();
response_size = System.Text.Encoding.UTF8.GetByteCount(content);

if (response.IsSuccessStatusCode)
{
    JsonText jsonText = new JsonText(content);
    var panel = new Panel(jsonText)
        .Header(
            $"[green]{response.StatusCode} - Timing: {watch.ElapsedMilliseconds} ms  | Request Size: {request_size} | Response Size: {response_size}[/]",
            Justify.Center)
        .RoundedBorder();
    AnsiConsole.Write(panel);
}
else
{
    AnsiConsole.MarkupLineInterpolated($"[bold red]Something went wrong! - {response.StatusCode} - {content}[/]");
}

AnsiConsole.MarkupLineInterpolated(
    $"[gray]Timing: {watch.ElapsedMilliseconds} ms | Request Size: {request_size} | Response Size: {response.Content.Headers.ContentLength}[/]");

enum HttpMethod
{
    GET,
    POST,
    PUT,
    DELETE
};