using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolicyGuard.Agent;
using PolicyGuard.Data;
using PolicyGuard.Detection;
using PolicyGuard.Models;
using PolicyGuard.Storage;
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
    private readonly CodeScanner _codeScanner;
    private readonly IWebHostEnvironment _env;
    private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    public ScanBackgroundService(
        ILogger<ScanBackgroundService> logger,
        IServiceProvider serviceProvider,
        CodeScanner codeScanner,
        IWebHostEnvironment env)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _codeScanner = codeScanner;
        _env = env;
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

            // ===== Run detectors (Person C — code scanning) =====
            var findings = await RunCodeScanners(scan, ct);
            db.Findings.AddRange(findings);
            await db.SaveChangesAsync(ct);

            // ===== Call AI reasoning (Person F) =====
            // Retrieve the relevant policy clause and ask the LLM to reason about each finding,
            // attaching the citation + a human-approvable proposed fix. Offline-safe (mock mode).
            var orchestrator = _serviceProvider.GetRequiredService<ScanOrchestrator>();
            await orchestrator.ReasonAsync(scan, findings, ct);
            await db.SaveChangesAsync(ct);

            // Mark scan as done
            scan.Status = "DONE";
            scan.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Scan {ScanId} completed successfully with {Count} findings",
                scan.Id, findings.Count);
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
    /// Reads every uploaded file for the scan and runs the code scanners over it.
    /// Stamps the ScanId (which the scanners don't know) onto each resulting finding.
    /// </summary>
    private async Task<List<Finding>> RunCodeScanners(Scan scan, CancellationToken ct)
    {
        var findings = new List<Finding>();
        var scanFolder = UploadStorage.GetScanFolder(_env.ContentRootPath, scan.Id);

        if (!Directory.Exists(scanFolder))
        {
            _logger.LogWarning("No uploaded files found for scan {ScanId} at {Folder}", scan.Id, scanFolder);
            return findings;
        }

        foreach (var path in Directory.EnumerateFiles(scanFolder))
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);

            string content;
            try
            {
                content = await File.ReadAllTextAsync(path, ct);
            }
            catch (Exception ex)
            {
                // Likely a binary file (e.g. .xlsx) — skip text scanning for now.
                _logger.LogWarning(ex, "Skipping unreadable file {File} in scan {ScanId}", fileName, scan.Id);
                continue;
            }

            var input = new SourceInput(fileName, content, GuessLanguage(fileName));
            var fileFindings = await _codeScanner.ScanAsync(input, ct);

            foreach (var finding in fileFindings)
            {
                finding.ScanId = scan.Id;
            }

            findings.AddRange(fileFindings);
        }

        _logger.LogInformation("Detection produced {Count} findings for scan {ScanId}", findings.Count, scan.Id);
        return findings;
    }

    /// <summary>Maps a file extension to a language hint for the scanners.</summary>
    private static string? GuessLanguage(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".json" => "json",
            ".csv" => "csv",
            ".py" => "python",
            ".js" or ".ts" => "javascript",
            _ => null,
        };
    }
}
