using System.Text.Json;
using FormatKit.Core.Profiles;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.Profiles;

public class PlanBuilderTests
{
    private static JsonElement Section(bool enabled) =>
        JsonDocument.Parse(enabled ? """{"enabled":true}""" : """{"enabled":false}""").RootElement.Clone();

    private static Profile ProfileOf(params (string Name, bool Enabled)[] sections) =>
        new(1, "t", sections.ToDictionary(s => s.Name, s => Section(s.Enabled)));

    [Fact]
    public void Steps_follow_module_registration_order()
    {
        var a = new FakeModule("a"); a.Steps.AddRange([new FakeStep("a1"), new FakeStep("a2")]);
        var b = new FakeModule("b"); b.Steps.Add(new FakeStep("b1"));

        var plan = PlanBuilder.Build(ProfileOf(("b", true), ("a", true)), [a, b]);

        Assert.Equal(["a1", "a2", "b1"], plan.Select(s => s.Id));
    }

    [Fact]
    public void Profile_base_directory_is_passed_to_create_steps()
    {
        var a = new FakeModule("a");
        var profile = ProfileOf(("a", true)) with { BaseDirectory = @"E:\usb" };

        PlanBuilder.Build(profile, [a]);

        Assert.Equal([@"E:\usb"], a.BaseDirectories);
    }

    [Fact]
    public void Disabled_module_contributes_no_steps()
    {
        var a = new FakeModule("a"); a.Steps.Add(new FakeStep("a1"));
        var b = new FakeModule("b"); b.Steps.Add(new FakeStep("b1"));

        var plan = PlanBuilder.Build(ProfileOf(("a", false), ("b", true)), [a, b]);

        Assert.Equal(["b1"], plan.Select(s => s.Id));
    }

    [Fact]
    public void Module_absent_from_profile_contributes_no_steps()
    {
        var a = new FakeModule("a"); a.Steps.Add(new FakeStep("a1"));
        var b = new FakeModule("b"); b.Steps.Add(new FakeStep("b1"));

        var plan = PlanBuilder.Build(ProfileOf(("b", true)), [a, b]);

        Assert.Equal(["b1"], plan.Select(s => s.Id));
    }

    [Fact]
    public void Duplicate_step_ids_throw()
    {
        var a = new FakeModule("a"); a.Steps.Add(new FakeStep("dup"));
        var b = new FakeModule("b"); b.Steps.Add(new FakeStep("dup"));

        Assert.Throws<InvalidOperationException>(() => PlanBuilder.Build(ProfileOf(("a", true), ("b", true)), [a, b]));
    }
}
