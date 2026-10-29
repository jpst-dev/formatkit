using System.Globalization;
using FormatKit.Core.Steps;

namespace FormatKit.Dev.Tests;

public class DevItemTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentUICulture;

    public DevItemTests() => CultureInfo.CurrentUICulture = new CultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentUICulture = _original;

    [Fact]
    public void Pending_makes_step_fail_with_its_command()
    {
        var result = DevStepResult.From([
            new DevItem("Distro", DevItemOutcome.Done),
            new DevItem("Default user", DevItemOutcome.Pending, "Run `formatkit dev wsl-user` when you are at the PC.", "no password yet"),
            new DevItem("Key", DevItemOutcome.Failed, "Run `formatkit dev wsl-user` when you are at the PC."),
            new DevItem("Other", DevItemOutcome.Failed, "See the log for details."),
        ], reboot: true);

        Assert.Equal(StepOutcome.Failed, result.Outcome);
        Assert.Equal("Run `formatkit dev wsl-user` when you are at the PC. See the log for details.", result.NextAction);
        Assert.Equal(
            ["Distro: done", "Default user: pending — no password yet", "Key: failed", "Other: failed"],
            result.Messages);
    }

    [Fact]
    public void NotApplicable_alone_is_success()
    {
        var result = DevStepResult.From([
            new DevItem("A", DevItemOutcome.NotApplicable),
            new DevItem("B", DevItemOutcome.AlreadyInOrder),
        ]);

        Assert.Equal(StepOutcome.Succeeded, result.Outcome);
        Assert.Null(result.NextAction);
        Assert.Equal(["A: not applicable", "B: already in order"], result.Messages);
    }

    [Fact]
    public void Reboot_without_failures()
    {
        var result = DevStepResult.From([new DevItem("Platform", DevItemOutcome.Done)], reboot: true);

        Assert.Equal(StepOutcome.RebootRequired, result.Outcome);
        Assert.Equal(["Platform: done"], result.Messages);
    }

    [Fact]
    public void Outcome_text_is_localized()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pt-BR");

        var result = DevStepResult.From([new DevItem("Chave", DevItemOutcome.AlreadyInOrder)]);

        Assert.Equal(["Chave: já estava em ordem"], result.Messages);
    }

    [Fact]
    public void Backup_name_carries_the_local_time()
    {
        var now = new DateTimeOffset(2026, 10, 28, 9, 5, 7, TimeSpan.FromHours(-3));

        Assert.Equal(@"C:\Users\Joao\.gitconfig.formatkit-20261028-090507.bak", Backup.PathFor(@"C:\Users\Joao\.gitconfig", now));
    }
}
