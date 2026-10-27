using FormatKit.Core.Steps;
using FormatKit.Drivers.Catalog;
using FormatKit.Drivers.Localization;

namespace FormatKit.Drivers.Steps;

public sealed class LeftoverReportStep(VendorCatalog catalog) : IStep
{
    private const string CatalogSearchUrl = "https://www.catalog.update.microsoft.com/Search.aspx?q=";

    public string Id => "drivers.leftover-report";
    public string Title => Strings.Get("Step_LeftoverReport_Title");
    public bool RequiresAdmin => false;
    public bool IsCritical => false;

    public Task<bool> CheckAsync(StepContext ctx, CancellationToken ct) => Task.FromResult(false);

    public async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var hw = await ctx.Services.Hardware.ReadAsync(ct);
        if (hw.ProblemDevices.Count == 0)
        {
            return StepResult.Success(Strings.Get("Step_LeftoverReport_AllGood"));
        }

        var messages = hw.ProblemDevices.Select(d =>
        {
            var hardwareId = d.HardwareIds.FirstOrDefault();
            return hardwareId is null
                ? Strings.Format("Step_LeftoverReport_Device", d.DisplayName, d.ErrorCode, Strings.Get("Step_LeftoverReport_NoHardwareId"), CatalogSearchUrl)
                : Strings.Format("Step_LeftoverReport_Device", d.DisplayName, d.ErrorCode, hardwareId, CatalogSearchUrl + Uri.EscapeDataString(hardwareId));
        }).ToList();

        var board = MachineClassifier.Classify(hw, catalog).Board;
        if (board is not null)
        {
            messages.Add(Strings.Format("Step_LeftoverReport_BoardSupport", board.SupportUrl));
        }

        return StepResult.Success([.. messages]);
    }
}
