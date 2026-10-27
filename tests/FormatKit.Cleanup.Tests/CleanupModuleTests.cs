using System.Text.Json;
using FormatKit.Cleanup.Catalog;
using FormatKit.Core;
using FormatKit.Core.Profiles;
using FormatKit.Core.Tests.Fakes;
using static FormatKit.Cleanup.Tests.TestItems;

namespace FormatKit.Cleanup.Tests;

public class CleanupModuleTests
{
    // Catalog order deliberately differs from category order.
    private static readonly CleanupModule Module = new(
        NewCatalog(
            Item("privacy.balanced", CleanupLevel.Balanced),
            Item("apps.safe", CleanupLevel.Safe),
            Item("performance.aggressive", CleanupLevel.Aggressive),
            Item("apps.aggressive", CleanupLevel.Aggressive)),
        new AppPaths(@"C:\FormatKitTest"),
        new InMemoryFileSystem());

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IReadOnlyList<ProfileError> Validate(string json) => Module.Validate(Json(json), "modules.cleanup");

    [Fact]
    public void Valid_section_has_no_errors()
    {
        Assert.Empty(Validate("""{"enabled":true,"level":"balanced","include":["apps.safe"],"exclude":["privacy.balanced"]}"""));
        Assert.Equal("cleanup", Module.Name);
    }

    [Fact]
    public void Missing_level_when_enabled_is_error()
    {
        var error = Assert.Single(Validate("""{"enabled":true}"""));

        Assert.Equal(ProfileErrorCode.MissingField, error.Code);
        Assert.Equal("modules.cleanup.level", error.Path);
        Assert.Empty(Validate("""{"enabled":false}"""));
    }

    [Fact]
    public void Unknown_include_id_reports_path()
    {
        var error = Assert.Single(Validate("""{"enabled":true,"level":"safe","include":["apps.safe","apps.nope"]}"""));

        Assert.Equal(ProfileErrorCode.InvalidValue, error.Code);
        Assert.Equal("modules.cleanup.include[1]", error.Path);
        Assert.Contains("apps.nope", error.Message);
    }

    [Fact]
    public void Overlap_is_error()
    {
        var error = Assert.Single(Validate("""{"enabled":true,"level":"safe","include":["privacy.balanced"],"exclude":["apps.safe","privacy.balanced"]}"""));

        Assert.Equal("modules.cleanup.exclude[1]", error.Path);
        Assert.Contains("privacy.balanced", error.Message);
    }

    [Fact]
    public void Aggressive_level_requires_confirmation()
    {
        var error = Assert.Single(Validate("""{"enabled":true,"level":"aggressive","exclude":["apps.aggressive"]}"""));

        Assert.Equal("modules.cleanup.confirmAggressive", error.Path);
        Assert.Contains("performance.aggressive", error.Message);
        Assert.DoesNotContain("apps.aggressive", error.Message);
    }

    [Fact]
    public void Included_aggressive_item_requires_confirmation()
    {
        var error = Assert.Single(Validate("""{"enabled":true,"level":"safe","include":["apps.aggressive"]}"""));

        Assert.Equal("modules.cleanup.confirmAggressive", error.Path);
        Assert.Contains("apps.aggressive", error.Message);
    }

    [Fact]
    public void Confirmed_aggressive_is_valid() =>
        Assert.Empty(Validate("""{"enabled":true,"level":"aggressive","confirmAggressive":true}"""));

    [Fact]
    public void Unknown_property_and_wrong_types_are_errors()
    {
        var errors = Validate("""{"enabled":"yes","level":"extreme","include":"apps.safe","exclude":[5],"confirmAggressive":1,"bogus":1}""");

        Assert.Equal(
            ["modules.cleanup.bogus", "modules.cleanup.enabled", "modules.cleanup.level", "modules.cleanup.include",
             "modules.cleanup.exclude[0]", "modules.cleanup.confirmAggressive"],
            errors.Select(e => e.Path));
    }

    [Fact]
    public void Creates_steps_in_category_order_skipping_empty_categories()
    {
        var steps = Module.CreateSteps(Json("""{"enabled":true,"level":"balanced"}"""));

        Assert.Equal(["cleanup.apps", "cleanup.privacy"], steps.Select(s => s.Id));
        Assert.All(steps, s => Assert.True(s.RequiresAdmin));
        Assert.All(steps, s => Assert.False(s.IsCritical));
    }

    [Fact]
    public void Disabled_section_creates_no_steps()
    {
        Assert.False(Module.IsEnabled(Json("""{"enabled":false,"level":"balanced"}""")));
        Assert.Empty(Module.CreateSteps(Json("""{"enabled":false,"level":"balanced"}""")));
    }
}
