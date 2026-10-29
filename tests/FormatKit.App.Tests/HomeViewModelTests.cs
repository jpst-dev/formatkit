using FormatKit.App.ViewModels;
using FormatKit.Core;
using FormatKit.Core.Running;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.App.Tests;

public class HomeViewModelTests
{
    [Fact]
    public void Home_InterruptedRun_ShowsBanner()
    {
        var files = new InMemoryFileSystem();
        var store = TestViewModels.Store(files);
        store.Save(new RunState(@"C:\p.json", "hash", ["a"], [], DateTimeOffset.UnixEpoch));

        Assert.True(new HomeViewModel(store).HasInterruptedRun);
    }

    [Fact]
    public void Home_NoState_NoBanner()
    {
        var home = new HomeViewModel(TestViewModels.Store(new InMemoryFileSystem()));

        Assert.False(home.HasInterruptedRun);
    }
}
