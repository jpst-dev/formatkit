using FormatKit.Core.Profiles;
using FormatKit.Core.Tests.Fakes;

namespace FormatKit.Core.Tests.Profiles;

public class ProfileLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fk-" + Guid.NewGuid().ToString("N"));
    private readonly FakeModule _fake = new("fake");

    public ProfileLoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private ProfileLoader Loader() => new([_fake]);

    private string Write(string json)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string Valid = """{"schemaVersion":1,"name":"PC","modules":{"fake":{"enabled":true}}}""";

    [Fact]
    public void Valid_profile_loads()
    {
        var result = Loader().Load(Write(Valid));

        Assert.True(result.IsValid);
        Assert.Equal("PC", result.Value!.Profile.Name);
        Assert.Equal(1, result.Value.Profile.SchemaVersion);
        Assert.True(result.Value.Profile.Modules.ContainsKey("fake"));
        Assert.Equal(64, result.Value.Hash.Length);
        Assert.Equal(result.Value.Hash.ToLowerInvariant(), result.Value.Hash);
        Assert.Equal("modules.fake", _fake.ValidateCalls.Single().Path);
    }

    [Fact]
    public void Missing_file_reports_FileNotFound()
    {
        var result = Loader().Load(Path.Combine(_dir, "nope.json"));

        Assert.Null(result.Value);
        Assert.Equal(ProfileErrorCode.FileNotFound, result.Errors[0].Code);
        Assert.Equal("", result.Errors[0].Path);
    }

    [Fact]
    public void Invalid_json_reports_InvalidJson()
    {
        var result = Loader().Parse("{ not json", "x.json");

        Assert.Equal(ProfileErrorCode.InvalidJson, result.Errors.Single().Code);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Missing_schemaVersion_reports_MissingField()
    {
        var result = Loader().Parse("""{"name":"PC","modules":{}}""", "x.json");

        var e = Assert.Single(result.Errors);
        Assert.Equal(ProfileErrorCode.MissingField, e.Code);
        Assert.Equal("schemaVersion", e.Path);
    }

    [Fact]
    public void Missing_name_reports_MissingField()
    {
        var result = Loader().Parse("""{"schemaVersion":1,"modules":{}}""", "x.json");

        var e = Assert.Single(result.Errors);
        Assert.Equal(ProfileErrorCode.MissingField, e.Code);
        Assert.Equal("name", e.Path);
    }

    [Fact]
    public void Wrong_type_reports_InvalidValue()
    {
        var result = Loader().Parse("""{"schemaVersion":"1","name":"PC","modules":{}}""", "x.json");

        var e = Assert.Single(result.Errors);
        Assert.Equal(ProfileErrorCode.InvalidValue, e.Code);
        Assert.Equal("schemaVersion", e.Path);
    }

    [Fact]
    public void Newer_schema_is_rejected()
    {
        var result = Loader().Parse("""{"schemaVersion":2,"name":"PC","modules":{}}""", "x.json");

        var e = Assert.Single(result.Errors);
        Assert.Equal(ProfileErrorCode.UnsupportedSchemaVersion, e.Code);
        Assert.Equal("schemaVersion", e.Path);
    }

    [Fact]
    public void Unknown_module_is_rejected()
    {
        var result = Loader().Parse("""{"schemaVersion":1,"name":"PC","modules":{"other":{}}}""", "x.json");

        var e = Assert.Single(result.Errors);
        Assert.Equal(ProfileErrorCode.UnknownModule, e.Code);
        Assert.Equal("modules.other", e.Path);
    }

    [Fact]
    public void Module_errors_are_collected_with_paths()
    {
        _fake.Errors.Add(new ProfileError(ProfileErrorCode.InvalidValue, "modules.fake.x", "bad"));

        var result = Loader().Parse(Valid, "x.json");

        Assert.Null(result.Value);
        Assert.Contains(result.Errors, e => e.Path == "modules.fake.x");
    }

    [Fact]
    public void All_errors_reported_together()
    {
        var result = Loader().Parse("""{"schemaVersion":1,"modules":{"other":{}}}""", "x.json");

        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Path == "name");
        Assert.Contains(result.Errors, e => e.Path == "modules.other");
    }

    [Fact]
    public void Hash_changes_when_file_changes()
    {
        var path = Write(Valid);
        var first = Loader().Load(path).Value!.Hash;
        File.WriteAllText(path, Valid.Replace("PC", "PC2"));
        var second = Loader().Load(path).Value!.Hash;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Non_object_root_reports_InvalidValue()
    {
        var result = Loader().Parse("[1]", "x.json");

        Assert.Equal(ProfileErrorCode.InvalidValue, result.Errors.Single().Code);
        Assert.Equal("", result.Errors.Single().Path);
    }
}
