// Controllers/FindingsController.cs — Person E
// POST /api/findings/{id}/approve — mark APPROVED, record the approver, and apply the
//   deterministic fix (Person D's Actions) to the stored uploaded file.
// POST /api/findings/{id}/reject  — mark REJECTED; no files are changed.
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PolicyGuard.Actions;
using PolicyGuard.Data;
using PolicyGuard.Storage;

namespace PolicyGuard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FindingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<FindingsController> _logger;
    private readonly IWebHostEnvironment _env;

    public FindingsController(AppDbContext db, ILogger<FindingsController> logger, IWebHostEnvironment env)
    {
        _db = db;
        _logger = logger;
        _env = env;
    }

    public sealed class ApproveRequest
    {
        public string? ApprovedBy { get; set; }
    }

    /// <summary>
    /// POST /api/findings/{id}/approve
    /// Marks the finding APPROVED, records the approver, and applies its deterministic fix to
    /// the stored file. Idempotent: re-approving an already-approved finding is a no-op.
    /// </summary>
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id, [FromBody] ApproveRequest? request)
    {
        var finding = await _db.Findings.FirstOrDefaultAsync(f => f.Id == id);
        if (finding is null) return NotFound();

        if (finding.Status == "APPROVED")
        {
            return Ok(new { finding.Id, finding.Status, applied = false, detail = "Finding was already approved." });
        }

        // "The LLM reasons; deterministic code acts" — the fix runs only now that a human approved.
        var scanFolder = UploadStorage.GetScanFolder(_env.ContentRootPath, finding.ScanId);
        FixOutcome outcome;
        try
        {
            outcome = FixApplier.Apply(finding, scanFolder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply fix for finding {FindingId}", id);
            return StatusCode(500, new { error = "Failed to apply the fix.", detail = ex.Message });
        }

        finding.Status = "APPROVED";
        finding.ApprovedBy = request?.ApprovedBy ?? "system";
        finding.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Finding {FindingId} approved by {User}; fix applied={Applied} ({Detail})",
            id, finding.ApprovedBy, outcome.Applied, outcome.Detail);

        return Ok(new
        {
            finding.Id,
            finding.Status,
            finding.ApprovedBy,
            finding.ApprovedAt,
            applied = outcome.Applied,
            detail = outcome.Detail,
        });
    }

    /// <summary>
    /// POST /api/findings/{id}/reject
    /// Marks the finding REJECTED. No file changes are made.
    /// </summary>
    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(string id)
    {
        var finding = await _db.Findings.FirstOrDefaultAsync(f => f.Id == id);
        if (finding is null) return NotFound();

        finding.Status = "REJECTED";
        await _db.SaveChangesAsync();

        _logger.LogInformation("Finding {FindingId} rejected", id);
        return Ok(new { finding.Id, finding.Status });
    }
}
