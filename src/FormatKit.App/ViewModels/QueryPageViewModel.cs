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
    private readonly Dictionary<object, CancellationTokenSource> _running = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    /// <summary>Cancels the running query; the page calls this when the user navigates away.</summary>
    public void Leave()
    {
        foreach (var source in _running.Values.ToList())
            source.Cancel();
    }

    /// <summary>
    /// Runs a system query guarded against WMI/COM failures. Queries with different <paramref name="slot"/> keys run
    /// independently; a repeat of the same slot replaces the older one. A cancelled or replaced query shows no error
    /// and applies nothing. <see cref="IsBusy"/> stays true while any query runs.
    /// </summary>
    protected async Task RunQueryAsync<T>(Func<CancellationToken, Task<T>> query, Action<T> apply, object? slot = null)
    {
        var key = slot ?? string.Empty;
        if (_running.TryGetValue(key, out var previous))
            previous.Cancel();
        var source = new CancellationTokenSource();
        _running[key] = source;
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
        catch (Exception ex)
        {
            _log.Error(ex, "Showing the query result failed");
            Error = ex.Message;
        }
        finally
        {
            if (_running.TryGetValue(key, out var current) && ReferenceEquals(current, source))
                _running.Remove(key);
            IsBusy = _running.Count > 0;
            source.Dispose();
        }
    }
}
