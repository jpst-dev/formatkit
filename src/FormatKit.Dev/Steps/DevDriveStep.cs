using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;
using FormatKit.Dev.Localization;

namespace FormatKit.Dev.Steps;

/// <summary>
/// Creates the Dev Drive on a dynamic VHDX, registers its remount at startup and points the package-cache
/// variables at it (spec 6.5, D7). The VHDX is not attached again after a reboot (spec 12), so the remount item is always present.
/// </summary>
internal sealed class DevDriveStep(DevDriveOptions options) : IStep
{
    private const string DriveLabel = "Dev Drive";
    private const string RemountLabel = "remount at startup";
    private const string VersionKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const long Gib = 1024L * 1024 * 1024;

    /// <summary>The first Windows 11 build that formats Dev Drives (22H2 with the October 2022 update).</summary>
    private static readonly (int Build, int Ubr) MinimumVersion = (22621, 2338);

    /// <summary>Room the VHDX's host volume must keep free beyond the drive's size.</summary>
    private const int HeadroomGB = 10;

    private static readonly Dictionary<string, string> CacheVariables = new(StringComparer.Ordinal)
    {
        ["npm"] = "npm_config_cache",
        ["nuget"] = "NUGET_PACKAGES",
        ["pip"] = "PIP_CACHE_DIR",
        ["cargo"] = "CARGO_HOME",
    };

    public string Id => "dev.devdrive";

    public string Title => Strings.Get("Step_Title_DevDrive");

    public bool RequiresAdmin => true;

    public bool IsCritical => false;

    public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct) => Task.FromResult(false);

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var items = new List<DevItem>();

        if (!IsSupportedBuild(ctx.Services.Registry))
        {
            items.Add(new DevItem(DriveLabel, DevItemOutcome.NotApplicable, Strings.Get("Next_UpdateWindows")));
            items.Add(new DevItem(RemountLabel, DevItemOutcome.NotApplicable));
            items.AddRange(options.Caches.Select(c => new DevItem(c, DevItemOutcome.NotApplicable)));
            return DevStepResult.From(items);
        }

        var drive = await EnsureDriveAsync(ctx, ct).ConfigureAwait(false);
        items.Add(drive);
        if (drive.Outcome is DevItemOutcome.Failed)
        {
            // Not attempted, but listed so none vanishes.
            items.Add(new DevItem(RemountLabel, DevItemOutcome.NotApplicable));
            items.AddRange(options.Caches.Select(c => new DevItem(c, DevItemOutcome.NotApplicable)));
            return DevStepResult.From(items);
        }

        items.Add(await EnsureRemountAsync(ctx, ct).ConfigureAwait(false));
        foreach (var cache in options.Caches)
            items.Add(EnsureCache(cache, ctx));
        return DevStepResult.From(items);
    }

    /// <summary>(CurrentBuildNumber, UBR) at or above the minimum; a value that cannot be read counts as too old.</summary>
    private static bool IsSupportedBuild(IRegistry registry)
    {
        var build = registry.Read(VersionKey, "CurrentBuildNumber");
        var ubr = registry.Read(VersionKey, "UBR");
        if (build is not { Kind: RegistryKind.String } || ubr is not { Kind: RegistryKind.Dword }
            || !int.TryParse(build.Data, out var buildNumber) || !int.TryParse(ubr.Data, out var revision))
            return false;
        return (buildNumber, revision).CompareTo(MinimumVersion) >= 0;
    }

    private async Task<DevItem> EnsureDriveAsync(StepContext ctx, CancellationToken ct)
    {
        var disks = ctx.Services.VirtualDisks;
        try
        {
            var state = await disks.GetAsync(options.Path, ct).ConfigureAwait(false);
            var attachedNow = false;
            if (state is { Attached: false })
            {
                await disks.AttachAsync(options.Path, ct).ConfigureAwait(false);
                state = await disks.GetAsync(options.Path, ct).ConfigureAwait(false);
                attachedNow = true;
                if (state is null)
                {
                    // Never create over a file that was just there.
                    ctx.Log.Warning("{Path} is unknown after it was attached", options.Path);
                    return new DevItem(DriveLabel, DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
                }
            }

            if (state is null)
                return await CreateAsync(ctx, ct).ConfigureAwait(false);

            if (state.Attached && state.Letter is { } letter
                && char.ToUpperInvariant(letter) == options.Letter && string.Equals(state.Label, options.Label, StringComparison.OrdinalIgnoreCase))
                return new DevItem(DriveLabel, attachedNow ? DevItemOutcome.Done : DevItemOutcome.AlreadyInOrder);

            ctx.Log.Warning("{Path} is attached as {Letter} {Label}, not {WantedLetter} {WantedLabel}",
                options.Path, state.Letter, state.Label, options.Letter, options.Label);
            return new DevItem(DriveLabel, DevItemOutcome.Failed, Strings.Get("Next_OtherVhdx"));
        }
        catch (VirtualDiskException ex)
        {
            ctx.Log.Warning(ex, "The Dev Drive at {Path} could not be set up", options.Path);
            return new DevItem(DriveLabel, DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // FreeBytes (DriveInfo) throws for a missing or not-ready host drive.
            ctx.Log.Warning(ex, "The host drive of {Path} could not be used", options.Path);
            return new DevItem(DriveLabel, DevItemOutcome.Failed, Strings.Get("Next_SeeLog"), ex.Message);
        }
    }

    private async Task<DevItem> CreateAsync(StepContext ctx, CancellationToken ct)
    {
        var disks = ctx.Services.VirtualDisks;
        if (disks.IsLetterInUse(options.Letter))
            return new DevItem(DriveLabel, DevItemOutcome.Failed, Strings.Get("Next_OtherVhdx"), Strings.Format("Detail_LetterInUse", options.Letter));

        var needed = options.SizeGB + HeadroomGB;
        var free = disks.FreeBytes(Path.GetPathRoot(options.Path)!);
        if (free < needed * Gib)
            return new DevItem(DriveLabel, DevItemOutcome.Failed, Strings.Get("Next_OtherVhdx"), Strings.Format("Detail_LowSpace", free / Gib, needed));

        await disks.CreateDevDriveAsync(options.Path, options.SizeGB, options.Letter, options.Label, ct).ConfigureAwait(false);
        return new DevItem(DriveLabel, DevItemOutcome.Done);
    }

    /// <summary>Registered every run: the call replaces any existing task, and there is no cheap way to tell whether it exists.</summary>
    private async Task<DevItem> EnsureRemountAsync(StepContext ctx, CancellationToken ct)
    {
        try
        {
            await ctx.Services.VirtualDisks.RegisterRemountTaskAsync(options.Path, ct).ConfigureAwait(false);
            return new DevItem(RemountLabel, DevItemOutcome.Done);
        }
        catch (VirtualDiskException ex)
        {
            ctx.Log.Warning(ex, "The remount task for {Path} could not be registered", options.Path);
            return new DevItem(RemountLabel, DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
        }
    }

    private DevItem EnsureCache(string cache, StepContext ctx)
    {
        var variable = CacheVariables[cache];
        var folder = $@"{options.Letter}:\packages\{cache}";
        var environment = ctx.Services.UserEnvironment;
        try
        {
            var current = environment.Get(variable);
            if (string.IsNullOrWhiteSpace(current))
            {
                ctx.Services.Files.CreateDirectory(folder);
                environment.Set(variable, folder);
                return new DevItem(cache, DevItemOutcome.Done);
            }

            if (string.Equals(current.TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Services.Files.CreateDirectory(folder);
                return new DevItem(cache, DevItemOutcome.AlreadyInOrder);
            }

            // D7: a value the user chose is not overwritten.
            ctx.Log.Warning("{Variable} already points to {Value}; it is kept instead of {Folder}", variable, current, folder);
            return new DevItem(cache, DevItemOutcome.AlreadyInOrder, Detail: Strings.Format("Detail_Kept", current));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ctx.Log.Warning(ex, "The {Cache} cache folder {Folder} could not be prepared", cache, folder);
            return new DevItem(cache, DevItemOutcome.Failed, Strings.Get("Next_SeeLog"));
        }
    }
}
