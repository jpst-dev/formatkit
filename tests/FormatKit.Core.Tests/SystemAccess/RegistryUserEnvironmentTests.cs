using FormatKit.Core.SystemAccess;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.SystemAccess;

public class RegistryUserEnvironmentTests
{
    [Fact]
    public void RegistryUserEnvironment_writes_hkcu_environment()
    {
        var registry = new FakeRegistry();
        var broadcasts = 0;
        var env = new RegistryUserEnvironment(registry, () => broadcasts++);

        env.Set("JAVA_HOME", @"C:\jdk");

        Assert.Equal(new RegistryValue(RegistryKind.String, @"C:\jdk"), registry.Read(@"HKCU\Environment", "JAVA_HOME"));
        Assert.Equal(@"C:\jdk", env.Get("JAVA_HOME"));
        Assert.Null(env.Get("MISSING"));
        Assert.Equal(1, broadcasts);
    }
}
