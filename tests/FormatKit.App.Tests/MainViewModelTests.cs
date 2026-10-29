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
    public void Media_IsHiddenUntilAvailable()
    {
        Assert.False(TestViewModels.CreateMain().IsMediaAvailable);
    }
}
