using System.Globalization;
using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Cleanup.Journal;
using FormatKit.Core.Steps;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Actions;

internal sealed class RegistryHandler : IActionHandler
{
    public const string Type = "registry";

    public Task<bool> IsAppliedAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (RegistryAction)action;
        var current = ctx.Services.Registry.Read(a.Path, a.Name);
        return Task.FromResult(a.Delete ? current is null : current == Desired(a));
    }

    public Task<JournalAction> CaptureAsync(CleanupAction action, StepContext ctx, CancellationToken ct)
    {
        var a = (RegistryAction)action;
        if (a.IfFileExists is { } file && !ctx.Services.Files.FileExists(Environment.ExpandEnvironmentVariables(file)))
        {
            throw new TargetNotPresentException(file);
        }

        var previous = CapturePrevious(ctx.Services.Registry, a.Path, a.Name);
        ctx.Log.Debug("Registry {Path}|{Name} previous state: {Previous}", a.Path, a.Name, previous);
        return Task.FromResult(new JournalAction(Type, $"{a.Path}|{a.Name}", previous));
    }

    public Task<JournalAction> ApplyAsync(CleanupAction action, JournalAction captured, StepContext ctx, CancellationToken ct)
    {
        var a = (RegistryAction)action;
        var desired = a.Delete ? null : Desired(a);
        WriteAndVerify(ctx.Services.Registry, a.Path, a.Name, desired);
        ctx.Log.Debug("Registry {Path}|{Name} new state: {State}", a.Path, a.Name, Describe(desired));
        ctx.Log.Information("Registry {Path}|{Name} {Outcome}", a.Path, a.Name, desired is null ? "deleted" : $"set to {desired.Kind} {desired.Data}");
        return Task.FromResult(captured);
    }

    public Task UndoAsync(JournalAction recorded, StepContext ctx, CancellationToken ct)
    {
        var split = recorded.Target.LastIndexOf('|');
        if (split < 0)
        {
            throw new InvalidOperationException($"Invalid registry journal target '{recorded.Target}'.");
        }

        var path = recorded.Target[..split];
        var name = recorded.Target[(split + 1)..];
        Restore(ctx.Services.Registry, path, name, recorded.Previous);
        ctx.Log.Information("Registry {Path}|{Name} restored", path, name);
        return Task.CompletedTask;
    }

    internal static JsonElement CapturePrevious(IRegistry registry, string path, string name)
    {
        var current = registry.Read(path, name);
        return current is null
            ? JsonSerializer.SerializeToElement(new { absent = true })
            : JsonSerializer.SerializeToElement(new { kind = current.Kind.ToString().ToLowerInvariant(), data = current.Data });
    }

    /// <summary>Writes <paramref name="value"/> (deletes when null) and throws if the re-read differs.</summary>
    internal static void WriteAndVerify(IRegistry registry, string path, string name, RegistryValue? value)
    {
        if (value is null)
        {
            registry.Delete(path, name);
        }
        else
        {
            registry.Write(path, name, value);
        }

        var actual = registry.Read(path, name);
        if (actual != value)
        {
            throw new InvalidOperationException(
                $"Registry value '{path}|{name}' did not take the expected state (expected {Describe(value)}, found {Describe(actual)}).");
        }
    }

    internal static void Restore(IRegistry registry, string path, string name, JsonElement previous)
    {
        if (previous.TryGetProperty("absent", out var absent) && absent.GetBoolean())
        {
            registry.Delete(path, name);
            return;
        }

        var kind = Enum.Parse<RegistryKind>(previous.GetProperty("kind").GetString()!, ignoreCase: true);
        registry.Write(path, name, new RegistryValue(kind, previous.GetProperty("data").GetString()!));
    }

    private static RegistryValue Desired(RegistryAction a)
    {
        if (a.Kind is null || a.Value is null)
        {
            throw new InvalidOperationException($"Registry action '{a.Name}' has no kind or value.");
        }

        var value = a.Value.Value;
        var data = a.Kind switch
        {
            RegistryKind.Dword => value.GetUInt32().ToString(CultureInfo.InvariantCulture),
            RegistryKind.Qword => value.GetUInt64().ToString(CultureInfo.InvariantCulture),
            _ => value.GetString()!,
        };
        return new RegistryValue(a.Kind.Value, data);
    }

    private static string Describe(RegistryValue? value) => value is null ? "absent" : $"{value.Kind} {value.Data}";
}
