namespace FormatKit.Dev.Tests;

public class PowerShellProfileBlockTests
{
    private const string Init = "oh-my-posh init pwsh --config paradox | Invoke-Expression";

    [Fact]
    public void Appends_the_block_after_existing_content()
    {
        var result = PowerShellProfileBlock.Append("Set-Alias a b", Init);

        Assert.Equal(
            "Set-Alias a b\r\n\r\n" + PowerShellProfileBlock.Begin + "\r\n" + Init + "\r\n" + PowerShellProfileBlock.End + "\r\n",
            result);
    }

    [Fact]
    public void Empty_profile_gets_only_the_block() =>
        Assert.Equal(
            PowerShellProfileBlock.Begin + "\r\n" + Init + "\r\n" + PowerShellProfileBlock.End + "\r\n",
            PowerShellProfileBlock.Append("", Init));

    [Fact]
    public void Profile_block_not_duplicated()
    {
        var once = PowerShellProfileBlock.Append("x\r\n", Init)!;

        Assert.Null(PowerShellProfileBlock.Append(once, Init));
    }

    [Fact]
    public void Profile_with_existing_init_untouched() =>
        Assert.Null(PowerShellProfileBlock.Append("oh-my-posh init pwsh --config ~/my.omp.json | Invoke-Expression\n", Init));
}
