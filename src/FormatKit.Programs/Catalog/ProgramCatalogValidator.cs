using System.Text.Json;
using System.Text.RegularExpressions;
using FormatKit.Core.SystemAccess;

namespace FormatKit.Programs.Catalog;

public static partial class ProgramCatalogValidator
{
    [GeneratedRegex(@"\A[a-z]+\.[a-z0-9-]+\z")]
    private static partial Regex IdPattern();

    public static IReadOnlyList<string> Validate(ProgramCatalog catalog)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var program in catalog.Programs)
        {
            var id = program.Id ?? "";
            var prefix = JsonNamingPolicy.CamelCase.ConvertName(program.Category.ToString());
            if (!IdPattern().IsMatch(id))
                errors.Add($"{id}: id must match ^[a-z]+\\.[a-z0-9-]+$.");
            else if (id[..id.IndexOf('.')] != prefix)
                errors.Add($"{id}: id prefix must equal the category '{prefix}'.");

            if (!ids.Add(id))
                errors.Add($"{id}: duplicate id.");

            try
            {
                WingetClient.Validate(program.WingetId, "wingetId");
            }
            catch (ArgumentException ex)
            {
                errors.Add($"{id}: {ex.Message}");
            }

            if (program.Source is not ("winget" or "msstore"))
                errors.Add($"{id}: source must be 'winget' or 'msstore', not '{program.Source}'.");

            if (program.Scope is not (null or "machine" or "user"))
                errors.Add($"{id}: scope must be 'machine' or 'user', not '{program.Scope}'.");
            else if (program.Scope == "machine" && program.Context == InstallContext.User)
                errors.Add($"{id}: scope 'machine' on a user-context entry.");

            foreach (var language in new[] { "en", "pt-BR" })
            {
                if (program.Text is null || !program.Text.TryGetValue(language, out var text))
                    errors.Add($"{id}: missing '{language}' text.");
                else if (string.IsNullOrWhiteSpace(text?.Name) || string.IsNullOrWhiteSpace(text.Description))
                    errors.Add($"{id}: '{language}' name and description must not be blank.");
            }

            if (!packages.Add($"{program.WingetId}|{program.Source}"))
                errors.Add($"{id}: duplicate winget package '{program.WingetId}' for source '{program.Source}'.");
        }

        return errors;
    }
}
