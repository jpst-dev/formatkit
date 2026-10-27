using System.Runtime.InteropServices;
using FormatKit.Core.Localization;

namespace FormatKit.Core.WindowsUpdate;

/// <summary>Windows Update Agent access through late-bound COM, run on a dedicated STA thread.</summary>
public sealed class ComWindowsUpdateClient(TimeSpan? searchTimeout = null, TimeSpan? installTimeout = null)
    : IWindowsUpdateClient
{
    private const string SearchCriteria = "IsInstalled=0 and Type='Driver'";
    private const int FailureHResult = unchecked((int)0x80004005); // E_FAIL
    private const int TimeoutHResult = unchecked((int)0x800705B4); // ERROR_TIMEOUT

    private readonly TimeSpan _searchTimeout = searchTimeout ?? Timeouts.Query;
    private readonly TimeSpan _installTimeout = installTimeout ?? Timeouts.Install;

    public async Task<IReadOnlyList<DriverUpdate>> SearchDriversAsync(CancellationToken ct)
    {
        var work = RunOnStaThread<IReadOnlyList<DriverUpdate>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return Search();
            }
            catch (COMException ex)
            {
                // Surface as a non-COM exception so callers never deal with COM types.
                throw new InvalidOperationException($"{WindowsUpdateErrors.Describe(ex.HResult)} (0x{ex.HResult:X8})", ex);
            }
        });

        try
        {
            return await work.WaitAsync(_searchTimeout, ct);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException($"{Strings.Get("WindowsUpdate_Error_Timeout")} (0x{TimeoutHResult:X8})");
        }
    }

    public async Task<WindowsUpdateInstallResult> InstallAsync(IReadOnlyList<DriverUpdate> updates, CancellationToken ct)
    {
        var work = RunOnStaThread(() =>
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

        try
        {
            return await work.WaitAsync(_installTimeout, ct);
        }
        catch (TimeoutException)
        {
            // The STA thread is a background thread and is left to finish on its own.
            return new WindowsUpdateInstallResult(false, false, TimeoutHResult, updates.Select(u => u.Title).ToList());
        }
    }

    private static IReadOnlyList<DriverUpdate> Search()
    {
        var com = new List<object>();
        try
        {
            dynamic session = Track(com, CreateComObject("Microsoft.Update.Session"));
            dynamic searcher = Track(com, session.CreateUpdateSearcher());
            dynamic found = Track(com, searcher.Search(SearchCriteria).Updates);

            var list = new List<DriverUpdate>();
            int count = found.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic update = Track(com, found.Item(i));
                string id = update.Identity.UpdateID;
                string title = update.Title;
                list.Add(new DriverUpdate(id, title, ReadDriverHardwareId(update)));
            }

            return list;
        }
        finally
        {
            Release(com);
        }
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
        var com = new List<object>();
        try
        {
            ct.ThrowIfCancellationRequested();
            dynamic session = Track(com, CreateComObject("Microsoft.Update.Session"));
            dynamic searcher = Track(com, session.CreateUpdateSearcher());
            dynamic found = Track(com, searcher.Search(SearchCriteria).Updates);

            // Match the requested updates against a fresh search: COM update objects cannot cross calls.
            var wanted = updates.Select(u => u.UpdateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            dynamic toInstall = Track(com, CreateComObject("Microsoft.Update.UpdateColl"));
            var titles = new List<string>();
            int count = found.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic update = Track(com, found.Item(i));
                string id = update.Identity.UpdateID;
                if (!wanted.Contains(id))
                {
                    continue;
                }

                bool eulaAccepted = update.EulaAccepted;
                if (!eulaAccepted)
                {
                    update.AcceptEula();
                }

                toInstall.Add(update);
                titles.Add((string)update.Title);
            }

            if (titles.Count == 0)
            {
                return new WindowsUpdateInstallResult(true, false, 0, [], []);
            }

            ct.ThrowIfCancellationRequested();
            dynamic downloader = Track(com, session.CreateUpdateDownloader());
            downloader.Updates = toInstall;
            dynamic download = Track(com, downloader.Download());
            int downloadCode = download.ResultCode;
            if (downloadCode != 2 && downloadCode != 3)
            {
                return new WindowsUpdateInstallResult(false, false, SafeHResult(download), titles);
            }

            ct.ThrowIfCancellationRequested();
            dynamic installer = Track(com, session.CreateUpdateInstaller());
            installer.Updates = toInstall;
            dynamic result = Track(com, installer.Install());

            int code = result.ResultCode;
            bool reboot = result.RebootRequired;
            var failed = new List<string>();
            var installed = new List<string>();
            for (var i = 0; i < titles.Count; i++)
            {
                int perUpdate = result.GetUpdateResult(i).ResultCode;
                (perUpdate == 2 ? installed : failed).Add(titles[i]);
            }

            var succeeded = code == 2 || code == 3;
            return new WindowsUpdateInstallResult(succeeded, reboot, succeeded ? 0 : SafeHResult(result), failed, installed);
        }
        finally
        {
            Release(com);
        }
    }

    private static T Track<T>(List<object> com, T value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            com.Add(value);
        }

        return value;
    }

    private static void Release(List<object> com)
    {
        foreach (var o in com)
        {
            try
            {
                Marshal.FinalReleaseComObject(o);
            }
            catch (Exception)
            {
                // Best effort: releasing must never mask the real result.
            }
        }
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
