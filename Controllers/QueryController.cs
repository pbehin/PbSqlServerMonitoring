using Microsoft.AspNetCore.Mvc;
using PbSqlServerMonitoring.Models;
using PbSqlServerMonitoring.Services;

namespace PbSqlServerMonitoring.Controllers;

/// <summary>
/// Controller for query-related API endpoints including execution plan retrieval.
/// </summary>
[ApiController]
[Route("api/queries")]
public class QueryController : ControllerBase
{
    private readonly IExecutionPlanService _executionPlanService;
    private readonly ILogger<QueryController> _logger;

    public QueryController(
        IExecutionPlanService executionPlanService,
        ILogger<QueryController> logger)
    {
        _executionPlanService = executionPlanService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the execution plan for a specific query hash.
    /// </summary>
    /// <param name="queryHash">The query hash from Prometheus metrics (e.g., 0x1234ABCD...)</param>
    /// <param name="connectionId">Optional connection ID to filter by specific connection</param>
    /// <returns>Execution plan details with decompressed XML</returns>
    [HttpGet("execution-plan/{queryHash}")]
    [ProducesResponseType(typeof(ApiResponse<ExecutionPlanResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetExecutionPlan(
        [FromRoute] string queryHash,
        [FromQuery] string? connectionId = null)
    {
        _logger.LogDebug("Getting execution plan for hash: {QueryHash}, connectionId: {ConnectionId}", queryHash, connectionId);

        ExecutionPlanResponse? plan;
        
        if (!string.IsNullOrEmpty(connectionId))
        {
            plan = await _executionPlanService.GetPlanByHashAsync(queryHash, connectionId);
        }
        else
        {
            plan = await _executionPlanService.GetPlanByHashAsync(queryHash);
        }

        if (plan == null)
        {
            return NotFound(ApiResponse<object>.Error($"No execution plan found for query hash: {queryHash}"));
        }

        return Ok(ApiResponse<ExecutionPlanResponse>.Ok(plan));
    }

    /// <summary>
    /// Gets the execution plan XML directly (for download/SSMS import).
    /// </summary>
    [HttpGet("execution-plan/{queryHash}/xml")]
    [Produces("application/xml")]
    [ProducesResponseType(typeof(string), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetExecutionPlanXml(
        [FromRoute] string queryHash,
        [FromQuery] string? connectionId = null)
    {
        ExecutionPlanResponse? plan;
        
        if (!string.IsNullOrEmpty(connectionId))
        {
            plan = await _executionPlanService.GetPlanByHashAsync(queryHash, connectionId);
        }
        else
        {
            plan = await _executionPlanService.GetPlanByHashAsync(queryHash);
        }

        if (plan == null)
        {
            return NotFound();
        }

        return Content(plan.ExecutionPlanXml, "application/xml");
    }

    /// <summary>
    /// Manually triggers pruning of old execution plans.
    /// </summary>
    [HttpPost("execution-plans/prune")]
    [ProducesResponseType(typeof(ApiResponse<object>), 200)]
    public async Task<IActionResult> PruneExecutionPlans()
    {
        var count = await _executionPlanService.PruneOldPlansAsync();
        await _executionPlanService.EnforceStorageLimitAsync();
        
        return Ok(ApiResponse<object>.Ok(new { prunedCount = count }));
    }
}
