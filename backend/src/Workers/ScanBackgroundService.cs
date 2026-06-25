using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolicyGuard.Agent;
using PolicyGuard.Data;
using PolicyGuard.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PolicyGuard.Workers;

/// <summary>
/// Background worker that processes pending scans.
/// It wakes up periodically, finds scans in PENDING status,
/// runs the detectors, calls the AI reasoning, and saves findings.
/// </summary>
public class ScanBackgroundService : BackgroundService
{
    private readonly ILogger<ScanBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    public ScanBackgroundService(ILogger<ScanBackgroundService> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScanBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Find all pending scans
                    var pendingScans = await db.Scans
                        .Where(s => s.Status == "PENDING")
                        .ToListAsync(stoppingToken);

                    foreach (var scan in pendingScans)
                    {
                        _logger.LogInformation("Processing scan {ScanId}", scan.Id);
                        await ProcessScan(scan, db, stoppingToken);
                    }
                }

                // Sleep before next poll
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ScanBackgroundService stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ScanBackgroundService");
            }
        }
    }

    private async Task ProcessScan(Scan scan, AppDbContext db, CancellationToken ct)
    {
        try
        {
            scan.Status = "SCANNING";
            await db.SaveChangesAsync(ct);

            // ===== STUB: Run detectors =====
            // In real implementation, call Person C & D's detectors
            // For now, create mock findings for demo purposes
            var mockFindings = CreateMockFindings(scan.Id);
            db.Findings.AddRange(mockFindings);
            await db.SaveChangesAsync(ct);

            // ===== Call AI reasoning (Person F) =====
            // Retrieve the relevant policy clause and ask the LLM to reason about each finding,
            // attaching the citation + a human-approvable proposed fix. Offline-safe (mock mode).
            var orchestrator = _serviceProvider.GetRequiredService<ScanOrchestrator>();
            await orchestrator.ReasonAsync(scan, mockFindings, ct);
            await db.SaveChangesAsync(ct);

            // Mark scan as done
            scan.Status = "DONE";
            scan.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Scan {ScanId} completed successfully with {Count} findings", 
                scan.Id, mockFindings.Count);
        }
        catch (Exception ex)
        {
            scan.Status = "ERROR";
            scan.ErrorMessage = ex.Message;
            await db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Error processing scan {ScanId}", scan.Id);
        }
    }

    /// <summary>
    /// STUB: Create mock findings for demonstration.
    /// In real implementation, detectors (Person C & D) would create these.
    /// </summary>
    private List<Finding> CreateMockFindings(string scanId)
    {
        return new List<Finding>
        {
            new Finding
            {
                Id = Guid.NewGuid().ToString(),
                ScanId = scanId,
                DataType = "EMAIL",
                Severity = "HIGH",
                Location = "customers.csv:column=2",
                Snippet = "john@gmail.com",
                DetectedBy = "AZURE_LANGUAGE",
                Status = "PENDING_REVIEW",
                FixTool = "MASK_COLUMN",
                FixArgs = """{"column":"email","style":"partial"}"""
            },
            new Finding
            {
                Id = Guid.NewGuid().ToString(),
                ScanId = scanId,
                DataType = "SSN",
                Severity = "CRITICAL",
                Location = "customers.csv:column=4",
                Snippet = "123-45-6789",
                DetectedBy = "REGEX",
                Status = "PENDING_REVIEW",
                FixTool = "DROP_COLUMN",
                FixArgs = """{"column":"ssn"}"""
            }
        };
    }
}
