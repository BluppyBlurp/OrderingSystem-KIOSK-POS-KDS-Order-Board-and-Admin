using Kiosk.Api.Auth;
using Kiosk.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kiosk.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/reports")]
[Authorize(Policy = Policies.BackOffice)]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    /// <summary>Sales by business date, both ends inclusive (default: today).</summary>
    [HttpGet("sales")]
    public Task<SalesReportDto> Sales([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        reports.GetSalesAsync(from, to, ct);

    /// <summary>Payments that need a manual refund: they landed on an expired or cancelled order, or the amount was wrong.</summary>
    [HttpGet("refunds-needed")]
    public Task<IReadOnlyList<RefundNeededDto>> RefundsNeeded(CancellationToken ct) => reports.GetRefundsNeededAsync(ct);
}
