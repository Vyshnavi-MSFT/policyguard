// Controllers/PoliciesController.cs — Person E
// GET /api/policies — lists the policy sets available to scan against (populates the
// frontend policy dropdown). Backed by PolicyStore (Person F), which loads src/Policies/*.json.
using Microsoft.AspNetCore.Mvc;
using PolicyGuard.Agent;

namespace PolicyGuard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PoliciesController : ControllerBase
{
    private readonly PolicyStore _policyStore;

    public PoliciesController(PolicyStore policyStore)
    {
        _policyStore = policyStore;
    }

    // Friendly descriptions for the known policy sets; unknown policies fall back to null.
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GDPR"] = "EU General Data Protection Regulation",
        ["HIPAA"] = "US Health Insurance Portability and Accountability Act",
        ["SECRETS"] = "Hardcoded secrets, API keys, and credentials",
    };

    /// <summary>
    /// GET /api/policies
    /// Returns each available policy set with its clause count, for the scan dropdown.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPolicies(CancellationToken ct)
    {
        var policies = await _policyStore.GetPoliciesAsync(ct);

        var result = policies.Select(p => new
        {
            id = p.Name,
            name = p.Name,
            description = Descriptions.TryGetValue(p.Name, out var d) ? d : null,
            clauseCount = p.ClauseCount,
        });

        return Ok(result);
    }
}
