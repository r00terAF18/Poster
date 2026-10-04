using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poster.Core;

namespace Poster.Desktop.ViewModels;

public partial class RequestViewModel : ObservableObject
{
    private static readonly HttpMethod[] MethodList =
        [HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete, HttpMethod.Head, HttpMethod.Options];

    private readonly WorkspaceSession _session;
    private PosterReqRes _request = null!;

    public IReadOnlyList<string> Methods { get; } = MethodList.Select(m => m.Method).ToArray();
    public IReadOnlyList<string> AuthTypes { get; } = ["None", "Bearer token", "Basic", "API key"]; // same order as Poster.Core.AuthType
    public IReadOnlyList<string> Sections { get; } = ["Params", "Headers", "Auth", "Body"];

    // ── Editor ──
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _route = "";
    [ObservableProperty] private int _selectedMethodIndex;
    [ObservableProperty] private string _queryText = "";
    [ObservableProperty] private string _headersText = "";
    [ObservableProperty] private string _body = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowToken), nameof(ShowBasic), nameof(ShowApiKeyHeader), nameof(ShowNoAuth))]
    private int _selectedAuthIndex;

    [ObservableProperty] private string _token = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _apiKeyHeader = "X-API-Key";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParamsSection), nameof(IsHeadersSection), nameof(IsAuthSection), nameof(IsBodySection))]
    private int _selectedSectionIndex;

    public bool IsParamsSection => SelectedSectionIndex == 0;
    public bool IsHeadersSection => SelectedSectionIndex == 1;
    public bool IsAuthSection => SelectedSectionIndex == 2;
    public bool IsBodySection => SelectedSectionIndex == 3;

    public bool ShowNoAuth => SelectedAuthIndex == (int)AuthType.None;
    public bool ShowToken => SelectedAuthIndex is (int)AuthType.Bearer or (int)AuthType.ApiKey;
    public bool ShowBasic => SelectedAuthIndex == (int)AuthType.Basic;
    public bool ShowApiKeyHeader => SelectedAuthIndex == (int)AuthType.ApiKey;

    // ── Response ──
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private bool _hasResponse;
    [ObservableProperty] private bool _isSuccess;
    [ObservableProperty] private bool _isFailure;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _metricsText = "";
    [ObservableProperty] private string _responseText = "";

    public RequestViewModel(WorkspaceSession session)
    {
        _session = session;
        session.OpenRequested += Load;
        session.Loaded += NewRequest;
        NewRequest();
    }

    [RelayCommand]
    private void NewRequest()
    {
        Load(new PosterReqRes(_session.Workspace)
        {
            Name = "Untitled request",
            Route = "https://httpbin.org/get",
            HttpMethod = HttpMethod.Get,
        });
    }

    private void Load(PosterReqRes request)
    {
        _request = request;
        _request.SetParent(_session.Workspace);

        Name = request.Name;
        Route = request.Route;
        QueryText = Formatting.FormatPairs(request.QueryParams);
        HeadersText = Formatting.FormatPairs(request.Headers);
        Body = request.Body;

        int method = Array.FindIndex(MethodList, m => m == request.HttpMethod);
        SelectedMethodIndex = method < 0 ? 0 : method;

        SelectedAuthIndex = (int)request.Auth.Type;
        Token = request.Auth.Token;
        Username = request.Auth.Username;
        Password = request.Auth.Password;
        ApiKeyHeader = request.Auth.ApiKeyHeader;

        SelectedSectionIndex = 0;
        ShowResponse();
    }

    /// <summary>Copies the editor fields onto the underlying request.</summary>
    private void Apply()
    {
        _request.Name = string.IsNullOrWhiteSpace(Name) ? "Untitled request" : Name.Trim();
        _request.Route = Route.Trim();
        _request.HttpMethod = MethodList[Math.Clamp(SelectedMethodIndex, 0, MethodList.Length - 1)];
        _request.QueryParams = Formatting.ParsePairs(QueryText);
        _request.Headers = Formatting.ParsePairs(HeadersText);
        _request.Body = Body;
        _request.Auth.Type = (AuthType)Math.Clamp(SelectedAuthIndex, 0, AuthTypes.Count - 1);
        _request.Auth.Token = Token;
        _request.Auth.Username = Username;
        _request.Auth.Password = Password;
        _request.Auth.ApiKeyHeader = string.IsNullOrWhiteSpace(ApiKeyHeader) ? "X-API-Key" : ApiKeyHeader.Trim();
        _request.SetParent(_session.Workspace);
    }

    private void AddToWorkspace()
    {
        if (!_session.Workspace.Requests.Contains(_request))
            _session.Workspace.Requests.Add(_request);
        _session.RaiseChanged();
    }

    // Generates SendCommand and SendCancelCommand.
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        if (IsSending) return;

        Apply();
        if (string.IsNullOrWhiteSpace(_request.Route))
        {
            _session.Notify("Enter a request URL first");
            return;
        }

        AddToWorkspace();
        IsSending = true;
        try
        {
            await _request.SendAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _request.Error = exception.Message;
        }
        finally
        {
            IsSending = false;
        }

        ShowResponse();
    }

    [RelayCommand]
    private void SaveRequest()
    {
        Apply();
        AddToWorkspace();
        try
        {
            _session.Workspace.Save();
            _session.Notify($"Saved to {_session.Workspace.Name}");
        }
        catch (Exception exception)
        {
            _session.Notify($"Save failed: {exception.Message}");
        }
    }

    private void ShowResponse()
    {
        HttpResponseMessage? response = _request.ResponseMessage;

        if (_request.Error != null)
        {
            HasResponse = true;
            IsSuccess = false;
            IsFailure = true;
            StatusText = _request.Error == "Request cancelled." ? "Cancelled" : "Failed";
            ResponseText = _request.Error;
        }
        else if (response != null)
        {
            HasResponse = true;
            IsSuccess = response.IsSuccessStatusCode;
            IsFailure = !IsSuccess;
            StatusText = $"{(int)response.StatusCode} {response.ReasonPhrase ?? response.StatusCode.ToString()}";
            ResponseText = Formatting.PrettyBody(_request.ResponseBody);
        }
        else
        {
            HasResponse = false;
            IsSuccess = false;
            IsFailure = false;
            StatusText = "";
            ResponseText = "";
            MetricsText = "";
            return;
        }

        Metrics metric = _request.Metric;
        MetricsText = $"{metric.ElapsedTime} ms  ·  ↑ {Formatting.Bytes(metric.RequestSize)}  ·  ↓ {Formatting.Bytes(metric.ResponseSize)}";
    }
}
