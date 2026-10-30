using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

/// <summary>Selects the "No Sounds" scheme and clears every event's current sound.</summary>
internal sealed class SoundSchemeHandler : IActionHandler
{
    public const string Name = "soundScheme";

    private const string SchemesPath = @"HKCU\AppEvents\Schemes";
    private const string AppsPath = SchemesPath + @"\Apps";
    private static readonly RegistryValue None = new(RegistryKind.String, ".None");
    private static readonly RegistryValue Empty = new(RegistryKind.String, "");

    public Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var registry = ctx.Services.Registry;
        return Task.FromResult(registry.Read(SchemesPath, "") == None
            && CurrentPaths(registry).All(path => IsEmpty(registry.Read(path, ""))));
    }

    public Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var registry = ctx.Services.Registry;
        var currents = CurrentPaths(registry)
            .Where(path => registry.Read(path, "") is not null)
            .Select(path => new { path, value = RegistryHandler.CapturePrevious(registry, path, "") })
            .ToList();
        ctx.Log.Debug("Sound scheme previous state: scheme {Scheme}, {Count} event sounds", registry.Read(SchemesPath, ""), currents.Count);
        return Task.FromResult(new JournalAction(Name, Name, JsonSerializer.SerializeToElement(new
        {
            scheme = RegistryHandler.CapturePrevious(registry, SchemesPath, ""),
            currents,
        })));
    }

    public Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        var registry = ctx.Services.Registry;
        RegistryHandler.WriteAndVerify(registry, SchemesPath, "", None);
        foreach (var path in CurrentPaths(registry).Where(path => !IsEmpty(registry.Read(path, ""))))
        {
            RegistryHandler.WriteAndVerify(registry, path, "", Empty);
        }

        ctx.Log.Information("Sound scheme set to No Sounds");
        return Task.FromResult(captured);
    }

    public Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var registry = ctx.Services.Registry;
        RegistryHandler.Restore(registry, SchemesPath, "", recorded.Previous.GetProperty("scheme"));
        foreach (var current in recorded.Previous.GetProperty("currents").EnumerateArray())
        {
            RegistryHandler.Restore(registry, current.GetProperty("path").GetString()!, "", current.GetProperty("value"));
        }

        ctx.Log.Information("Sound scheme restored");
        return Task.CompletedTask;
    }

    private static bool IsEmpty(RegistryValue? value) => value is null || value.Data.Length == 0;

    private static List<string> CurrentPaths(IRegistry registry) =>
        registry.GetSubKeyNames(AppsPath)
            .SelectMany(app => registry.GetSubKeyNames($@"{AppsPath}\{app}")
                .Select(evt => $@"{AppsPath}\{app}\{evt}\.Current"))
            .ToList();
}
