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

public sealed record RegistryAction(string Path, string Name, RegistryKind? Kind, JsonElement? Value, bool Delete) : CleanupAction;

public sealed record ServiceAction(string Name, ServiceStartType StartType) : CleanupAction;

public sealed record AppxAction(string Package, string StoreId) : CleanupAction;

/// <summary><paramref name="Scheme"/> is <c>balanced</c>, <c>highPerformance</c> or <c>ultimate</c>.</summary>
public sealed record PowerSchemeAction(string Scheme) : CleanupAction;

public sealed record OptionalFeatureAction(string Name, OptionalFeatureState State) : CleanupAction;

public sealed record NamedAction(string Handler) : CleanupAction;
