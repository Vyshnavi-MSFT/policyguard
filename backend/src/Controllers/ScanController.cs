using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PolicyGuard.Data;
using PolicyGuard.Models;
using PolicyGuard.Storage;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PolicyGuard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScanController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<ScanController> _logger;
    private readonly IWebHostEnvironment _env;

    public ScanController(AppDbContext db, ILogger<ScanController> logger, IWebHostEnvironment env)
    {
        _db = db;
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// POST /api/scan
    /// Start a new scan. Frontend uploads files, picks a policy, and gets back a scanId.
    /// The actual scanning happens in the background.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> StartScan([FromForm] string policyId, [FromForm] List<IFormFile> files)
    {
        if (string.IsNullOrEmpty(policyId))
            return BadRequest("policyId is required");

        if (files == null || files.Count == 0)
            return BadRequest("At least one file is required");

        // Verify the policy exists (stub for now — Person F will load real policies)
        // TODO: var policy = await _db.Policies.FirstOrDefaultAsync(p => p.Name == policyId);

        // Create a Scan entry
        var scan = new Scan
        {
            Id = Guid.NewGuid().ToString(),
            PolicyId = policyId,
            Status = "PENDING",
            InputFileNames = string.Join(", ", files.Select(f => f.FileName))
        };

        _db.Scans.Add(scan);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Scan {ScanId} created with policy {PolicyId}", scan.Id, policyId);

        // Persist the uploaded file contents to disk so the background worker can scan them.
        var scanFolder = UploadStorage.GetScanFolder(_env.ContentRootPath, scan.Id);
        Directory.CreateDirectory(scanFolder);
        foreach (var file in files)
        {
            // Strip any directory components from the client-supplied name to prevent path traversal.
            var safeName = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(safeName))
                continue;

            var destination = Path.Combine(scanFolder, safeName);
            await using var stream = System.IO.File.Create(destination);
            await file.CopyToAsync(stream);
        }

        // Return the scanId immediately — actual scanning happens in background worker
        return Ok(new { scanId = scan.Id });
    }

    /// <summary>
    /// GET /api/scan/{id}
    /// Poll to get the status and findings of a scan.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetScan(string id)
    {
        var scan = await _db.Scans
            .Include(s => s.Findings)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (scan == null)
            return NotFound();

        // Calculate compliance score (stub: 100 - number of critical findings * 20)
        var criticalCount = scan.Findings.Count(f => f.Severity == "CRITICAL");
        var highCount = scan.Findings.Count(f => f.Severity == "HIGH");
        var complianceScore = Math.Max(0, 100 - (criticalCount * 20) - (highCount * 10));

        return Ok(new
        {
            scan.Id,
            scan.PolicyId,
            scan.Status,
            scan.CreatedAt,
            scan.CompletedAt,
            ComplianceScore = complianceScore,
            Findings = scan.Findings.Select(f => new
            {
                f.Id,
                f.DataType,
                f.Severity,
                f.Location,
                f.Snippet,
                f.PolicyClauseId,
                f.PolicyClauseText,
                f.Explanation,
                f.Status,
                f.FixTool,
                f.FixArgs
            }).ToList()
        });
    }

    /// <summary>
    /// PATCH /api/findings/{id}
    /// User approves or rejects a finding.
    /// If approved, the fix gets queued to run (Person D will execute it).
    /// </summary>
    [HttpPatch("findings/{id}")]
    public async Task<IActionResult> ApproveFinding(string id, [FromBody] ApproveFindingRequest request)
    {
        var finding = await _db.Findings.FirstOrDefaultAsync(f => f.Id == id);
        if (finding == null)
            return NotFound();

        if (request.Action == "APPROVE")
        {
            finding.Status = "APPROVED";
            finding.ApprovedBy = request.ApprovedBy ?? "system";
            finding.ApprovedAt = DateTime.UtcNow;
            _logger.LogInformation("Finding {FindingId} approved", id);
        }
        else if (request.Action == "REJECT")
        {
            finding.Status = "REJECTED";
            _logger.LogInformation("Finding {FindingId} rejected", id);
        }
        else
        {
            return BadRequest("Action must be APPROVE or REJECT");
        }

        await _db.SaveChangesAsync();

        // TODO: If approved, queue the fix to be executed by Person D's fix functions

        return Ok(new { status = finding.Status });
    }
}

public class ApproveFindingRequest
{
    public string? Action { get; set; }
    public string? ApprovedBy { get; set; }
}
