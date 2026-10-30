using FormatKit.App.Tests.Fakes;
using FormatKit.App.ViewModels;
using FormatKit.Core;

namespace FormatKit.App.Tests;

public class AboutViewModelTests
{
    [Fact]
    public void OpenLogFolder_OpensTheLogsDirectory()
    {
        var dialogs = new FakeDialogService();

        new AboutViewModel(dialogs).OpenLogFolderCommand.Execute(null);

        Assert.Equal([AppPaths.Default.LogsDirectory], dialogs.OpenedFolders);
    }

    [Fact]
    public void CliCommands_ListsTheSixCommandsWithDescriptions()
    {
        var commands = new AboutViewModel(new FakeDialogService()).CliCommands;

        Assert.Equal(6, commands.Count);
        Assert.All(commands, c => Assert.DoesNotContain("About_Cli_", c.Description));
    }
}
