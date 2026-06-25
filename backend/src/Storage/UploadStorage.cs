namespace PolicyGuard.Storage;

/// <summary>
/// Single source of truth for where a scan's uploaded files are persisted on disk.
/// The controller writes files here; the background worker reads them back to scan.
/// </summary>
public static class UploadStorage
{
    /// <summary>
    /// Returns the folder that holds the uploaded files for a given scan, namespaced by scan id.
    /// </summary>
    public static string GetScanFolder(string contentRootPath, string scanId) =>
        Path.Combine(contentRootPath, "uploads", scanId);
}
