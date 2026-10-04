using CommunityToolkit.Mvvm.ComponentModel;

namespace Poster.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;
    private CancellationTokenSource? _noticeCts;

    public RequestViewModel Request { get; }
    public HistoryViewModel History { get; }
    public VariablesViewModel Variables { get; }
    public WorkspacesViewModel Workspaces { get; }

    public IReadOnlyList<string> PageTitles { get; } = ["Request", "History", "Variables", "Workspaces"];

    [ObservableProperty] private int _selectedPageIndex;
    [ObservableProperty] private object _currentPage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);
    public string WorkspaceName => _session.Workspace.Name;

    public MainViewModel(WorkspaceSession session)
    {
        _session = session;
        Request = new RequestViewModel(session);
        History = new HistoryViewModel(session);
        Variables = new VariablesViewModel(session);
        Workspaces = new WorkspacesViewModel(session);
        _currentPage = Request;

        session.NoticeRequested += ShowNotice;
        session.Changed += () => OnPropertyChanged(nameof(WorkspaceName));
        session.Loaded += () => SelectedPageIndex = 0;
        session.OpenRequested += _ => SelectedPageIndex = 0;
    }

    partial void OnSelectedPageIndexChanged(int value) =>
        CurrentPage = value switch
        {
            1 => History,
            2 => Variables,
            3 => Workspaces,
            _ => Request,
        };

    private async void ShowNotice(string message)
    {
        _noticeCts?.Cancel();
        CancellationTokenSource cts = _noticeCts = new CancellationTokenSource();
        Notice = message;

        try
        {
            await Task.Delay(2800, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cts.IsCancellationRequested) Notice = null;
    }
}
