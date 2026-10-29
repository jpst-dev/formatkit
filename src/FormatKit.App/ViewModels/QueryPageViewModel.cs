using CommunityToolkit.Mvvm.ComponentModel;
using FormatKit.Hosting;

namespace FormatKit.App.ViewModels;

/// <summary>
/// Base of the pages that query the system: a busy flag for the progress indicator, an error shown inside the page,
/// and a query that is cancelled when the user leaves the page.
/// </summary>
public abstract partial class QueryPageViewModel(Serilog.ILogger? log = null) : ObservableObject
{
    private readonly Serilog.ILogger _log = log ?? Serilog.Core.Logger.None;
    private CancellationTokenSource? _running;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    /// <summary>Cancels the running query; the page calls this when the user navigates away.</summary>
    public void Leave() => _running?.Cancel();

    /// <summary>
    /// Runs a system query guarded against WMI/COM failures. A newer query replaces the running one; a cancelled
    /// query shows no error and applies nothing.
    /// </summary>
    protected async Task RunQueryAsync<T>(Func<CancellationToken, Task<T>> query, Action<T> apply)
    {
        _running?.Cancel();
        var source = new CancellationTokenSource();
        _running = source;
        IsBusy = true;
        Error = null;
        try
        {
            var result = await SystemQuery.GuardAsync(() => query(source.Token));
            if (source.IsCancellationRequested)
                return;

            if (result.Error is not null)
            {
                _log.Warning("System query failed: {Error}", result.Error);
                Error = result.Error;
                return;
            }

            apply(result.Value!);
        }
        catch (OperationCanceledException)
        {
            // The user left the page: not an error.
        }
        finally
        {
            if (ReferenceEquals(_running, source))
            {
                _running = null;
                IsBusy = false;
            }

            source.Dispose();
        }
    }
}
