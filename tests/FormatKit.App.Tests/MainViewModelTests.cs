using FormatKit.App.ViewModels;

namespace FormatKit.App.Tests;

public class MainViewModelTests
{
    [Fact]
    public void Navigate_ChangesCurrentPage()
    {
        var main = TestViewModels.CreateMain();

        main.NavigateCommand.Execute(Page.About);

        Assert.Equal(Page.About, main.Current);
    }

    [Fact]
    public void Media_IsInTheNavigationAndOpens()
    {
        var main = TestViewModels.CreateMain();

        Assert.Contains(main.NavItems, n => n.Page == Page.Media);
        main.NavigateCommand.Execute(Page.Media);

        Assert.IsType<MediaViewModel>(main.CurrentViewModel);
    }

    [Fact]
    public void MediaJobStarting_BlocksARun_AndARun_BlocksAMediaJob()
    {
        var main = TestViewModels.CreateMain();

        main.Media.IsBusy = true;
        Assert.True(main.RunProfile.OtherWorkRunning);
        Assert.True(main.IsWorkInProgress);

        main.Media.IsBusy = false;
        Assert.False(main.RunProfile.OtherWorkRunning);
        Assert.False(main.IsWorkInProgress);

        main.RunProfile.IsBusy = true;
        Assert.True(main.Media.OtherWorkRunning);
        Assert.True(main.IsWorkInProgress);
    }

    [Fact]
    public async Task ResumeDuringAMediaJob_StartsNothing()
    {
        var main = TestViewModels.CreateMain();
        main.Media.IsBusy = true;

        await main.ResumeAsync();

        Assert.False(main.RunProfile.IsRunInProgress);
    }
}
