using ClaimSettlement.Api.Controllers;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ClaimSettlement.Api.Evaluation;

[Route("api/v1/evaluation-runs")]
public sealed class EvaluationRunsController : BaseApiController
{
    private readonly ClaimSettlementDbContext _dbContext;
    private readonly IProviderContextAccessor _providerContextAccessor;

    public EvaluationRunsController(ClaimSettlementDbContext dbContext, IProviderContextAccessor providerContextAccessor)
    {
        _dbContext = dbContext;
        _providerContextAccessor = providerContextAccessor;
    }

    [HttpPost]
    [Authorize(Roles = "EvaluationRunner")]
    public async Task<ActionResult<EvaluationRunResponse>> CreateRun(
        [FromBody] CreateEvaluationRunRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DatasetVersion))
        {
            return BadRequest(new { error = "Dataset version is required." });
        }

        var run = new EvaluationRun
        {
            RunId = Guid.NewGuid(),
            ProviderId = _providerContextAccessor.ProviderId,
            CreatedByUserId = _providerContextAccessor.UserId,
            DatasetVersion = request.DatasetVersion.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.EvaluationRuns.Add(run);
        await _dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetClaimResults), new { runId = run.RunId, claimId = Guid.Empty }, new EvaluationRunResponse
        {
            RunId = run.RunId,
            DatasetVersion = run.DatasetVersion,
            CreatedAtUtc = run.CreatedAtUtc
        });
    }

    [HttpGet("{runId:guid}/claims/{claimId:guid}/results")]
    [Authorize(Roles = "EvaluationRunner")]
    public async Task<ActionResult<EvaluationClaimResultsResponse>> GetClaimResults(Guid runId, Guid claimId, CancellationToken ct)
    {
        var isOwnedRun = await _dbContext.EvaluationRuns.AnyAsync(run =>
            run.RunId == runId &&
            run.ProviderId == _providerContextAccessor.ProviderId &&
            run.CreatedByUserId == _providerContextAccessor.UserId,
            ct);
        if (!isOwnedRun)
        {
            return NotFound();
        }

        var claim = await _dbContext.Claims
            .Include(item => item.AgentOutputs)
            .Include(item => item.ToolInvocations)
            .AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.ClaimId == claimId &&
                item.ProviderId == _providerContextAccessor.ProviderId &&
                item.EvaluationRunId == runId,
                ct);
        if (claim is null)
        {
            return NotFound();
        }

        return Ok(new EvaluationClaimResultsResponse
        {
            RunId = runId,
            ClaimId = claim.ClaimId,
            Status = claim.Status,
            AgentOutputs = claim.AgentOutputs.OrderBy(item => item.CreatedAt).Select(item => new EvaluationAgentOutputResponse
            {
                AgentId = item.AgentId,
                CreatedAtUtc = item.CreatedAt,
                Output = JsonDocument.Parse(item.OutputPayload).RootElement.Clone()
            }).ToList(),
            ToolInvocations = claim.ToolInvocations.OrderBy(item => item.InvokedAtUtc).Select(item => new EvaluationToolInvocationResponse
            {
                AgentId = item.AgentId,
                ToolName = item.ToolName,
                InvokedAtUtc = item.InvokedAtUtc,
                Outcome = item.Outcome
            }).ToList()
        });
    }
}