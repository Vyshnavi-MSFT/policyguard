// Controllers/ReportController.cs — Person E
// GET /api/report/{scanId} — audit report: scan metadata + every finding with its policy
// citation, proposed/applied fix, and who approved it. Pass ?download=true for a JSON file.
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PolicyGuard.Data;

namespace PolicyGuard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// GET /api/report/{scanId}
    /// Returns the full audit trail for a scan. Add ?download=true to download it as a file.
    /// </summary>
    [HttpGet("{scanId}")]
    public async Task<IActionResult> GetReport(string scanId, [FromQuery] bool download = false)
    {
        var scan = await _db.Scans
            .Include(s => s.Findings)
            .FirstOrDefaultAsync(s => s.Id == scanId);

        if (scan is null) return NotFound();

        var critical = scan.Findings.Count(f => f.Severity == "CRITICAL");
        var high = scan.Findings.Count(f => f.Severity == "HIGH");
        var complianceScore = Math.Max(0, 100 - (critical * 20) - (high * 10));

        var report = new
        {
            scanId = scan.Id,
            policyId = scan.PolicyId,
            status = scan.Status,
            createdAt = scan.CreatedAt,
            completedAt = scan.CompletedAt,
            inputFiles = scan.InputFileNames,
            complianceScore,
            summary = new
            {
                total = scan.Findings.Count,
                critical,
                high,
                approved = scan.Findings.Count(f => f.Status == "APPROVED"),
                rejected = scan.Findings.Count(f => f.Status == "REJECTED"),
                pending = scan.Findings.Count(f => f.Status == "PENDING_REVIEW"),
            },
            findings = scan.Findings.Select(f => new
            {
                f.Id,
                f.DataType,
                f.Severity,
                f.Location,
                f.Snippet,
                f.DetectedBy,
                f.PolicyClauseId,
                f.PolicyClauseText,
                f.Explanation,
                f.Status,
                f.FixTool,
                f.FixArgs,
                f.ApprovedBy,
                f.ApprovedAt,
            }).ToList(),
            generatedAt = DateTime.UtcNow,
        };

        if (download)
        {
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            var bytes = Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", $"policyguard-report-{scan.Id}.json");
        }

        return Ok(report);
    }
}
