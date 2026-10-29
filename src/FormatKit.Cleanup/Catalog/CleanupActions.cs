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
