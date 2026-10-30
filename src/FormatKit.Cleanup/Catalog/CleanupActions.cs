using System.Text.Json;
using System.Text.Json.Serialization;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Cleanup.Catalog;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RegistryAction), "registry")]
[JsonDerivedType(typeof(ServiceAction), "service")]
[JsonDerivedType(typeof(AppxAction), "appx")]
[JsonDerivedType(typeof(PowerSchemeAction), "powerScheme")]
[JsonDerivedType(typeof(OptionalFeatureAction), "optionalFeature")]
[JsonDerivedType(typeof(ScheduledTaskAction), "scheduledTask")]
[JsonDerivedType(typeof(StartupItemAction), "startupItem")]
[JsonDerivedType(typeof(PowerSettingAction), "powerSetting")]
[JsonDerivedType(typeof(NamedAction), "named")]
public abstract record CleanupAction;

/// <param name="IfFileExists">When set, the action applies only if this file exists; environment variables are expanded. Otherwise the item is not applicable here.</param>
public sealed record RegistryAction(string Path, string Name, RegistryKind? Kind, JsonElement? Value, bool Delete, string? IfFileExists = null) : CleanupAction;

public sealed record ServiceAction(string Name, ServiceStartType StartType) : CleanupAction;

public sealed record AppxAction(string Package, string StoreId) : CleanupAction;

/// <summary><paramref name="Scheme"/> is <c>balanced</c>, <c>highPerformance</c> or <c>ultimate</c>.</summary>
public sealed record PowerSchemeAction(string Scheme) : CleanupAction;

public sealed record OptionalFeatureAction(string Name, OptionalFeatureState State) : CleanupAction;

public sealed record NamedAction(string Handler) : CleanupAction;

/// <param name="Path">Task folder, starting and ending with a backslash (for example <c>\Microsoft\Windows\Application Experience\</c>).</param>
public sealed record ScheduledTaskAction(string Path, string Name, bool Enabled) : CleanupAction;

/// <param name="Subgroup">Power subgroup GUID.</param>
/// <param name="Setting">Power setting GUID.</param>
/// <param name="Ac">Index while plugged in.</param>
/// <param name="Dc">Index on battery; null leaves it unchanged.</param>
public sealed record PowerSettingAction(string Subgroup, string Setting, int Ac, int? Dc) : CleanupAction;

/// <param name="Name">Value name under the Run key.</param>
public sealed record StartupItemAction(string Name, ItemScope Scope) : CleanupAction;
