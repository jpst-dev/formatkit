using System.Runtime.InteropServices;

namespace FormatKit.Core.WindowsUpdate;

/// <summary>Windows Update Agent access through late-bound COM, run on a dedicated STA thread.</summary>
public sealed class ComWindowsUpdateClient : IWindowsUpdateClient
{
    private const string SearchCriteria = "IsInstalled=0 and Type='Driver'";
    private const int FailureHResult = unchecked((int)0x80004005); // E_FAIL

    public Task<IReadOnlyList<DriverUpdate>> SearchDriversAsync(CancellationToken ct) =>
        RunOnStaThread<IReadOnlyList<DriverUpdate>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return Search();
            }
            catch (COMException ex)
            {
                // Surface as a non-COM exception so callers never deal with COM types.
                throw new InvalidOperationException(WindowsUpdateErrors.Describe(ex.HResult), ex);
            }
        });

    public Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<DriverUpdate> updates, CancellationToken ct) =>
        RunOnStaThread(() =>
        {
            try
            {
                return Install(updates, ct);
            }
            catch (COMException ex)
            {
                return new WindowsUpdateInstallResult(false, false, ex.HResult, []);
            }
        });

    private static IReadOnlyList<DriverUpdate> Search()
    {
        dynamic session = CreateComObject("Microsoft.Update.Session");
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic found = searcher.Search(SearchCriteria).Updates;

        var list = new List<DriverUpdate>();
        int count = found.Count;
        for (var i = 0; i < count; i++)
        {
            dynamic update = found.Item(i);
            string id = update.Identity.UpdateID;
            string title = update.Title;
            list.Add(new DriverUpdate(id, title, ReadDriverHardwareId(update)));
        }

        return list;
    }

    private static string? ReadDriverHardwareId(dynamic update)
    {
        try
        {
            string? id = update.DriverHardwareID;
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch (Exception)
        {
            return null; // property is absent on some update types
        }
    }

    private static WindowsUpdateInstallResult Install(IReadOnlyList<DriverUpdate> updates, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        dynamic session = CreateComObject("Microsoft.Update.Session");
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic found = searcher.Search(SearchCriteria).Updates;

        // Match the requested updates against a fresh search: COM update objects cannot cross calls.
        var wanted = updates.Select(u => u.UpdateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        dynamic toInstall = CreateComObject("Microsoft.Update.UpdateColl");
        var titles = new List<string>();
        int count = found.Count;
        for (var i = 0; i < count; i++)
        {
            dynamic update = found.Item(i);
            string id = update.Identity.UpdateID;
            if (wanted.Contains(id))
            {
                toInstall.Add(update);
                titles.Add((string)update.Title);
            }
        }

        if (titles.Count == 0)
        {
            return new WindowsUpdateInstallResult(true, false, 0, []);
        }

        ct.ThrowIfCancellationRequested();
        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = toInstall;
        dynamic download = downloader.Download();
        int downloadCode = download.ResultCode;
        if (downloadCode != 2 && downloadCode != 3)
        {
            return new WindowsUpdateInstallResult(false, false, SafeHResult(download), titles);
        }

        ct.ThrowIfCancellationRequested();
        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = toInstall;
        dynamic result = installer.Install();

        int code = result.ResultCode;
        bool reboot = result.RebootRequired;
        var failed = new List<string>();
        for (var i = 0; i < titles.Count; i++)
        {
            int perUpdate = result.GetUpdateResult(i).ResultCode;
            if (perUpdate != 2)
            {
                failed.Add(titles[i]);
            }
        }

        var succeeded = code == 2 || code == 3;
        return new WindowsUpdateInstallResult(succeeded, reboot, succeeded ? 0 : SafeHResult(result), failed);
    }

    private static int SafeHResult(dynamic result)
    {
        try
        {
            int hr = result.HResult;
            return hr != 0 ? hr : FailureHResult;
        }
        catch (Exception)
        {
            return FailureHResult;
        }
    }

    private static object CreateComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: true)!;
        return Activator.CreateInstance(type)!;
    }

    private static Task<T> RunOnStaThread<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (OperationCanceledException ex)
            {
                tcs.SetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "FormatKit.WindowsUpdate.STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
