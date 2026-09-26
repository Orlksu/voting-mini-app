using System.Collections.Concurrent;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Requests;
using Backend.Contracts.Responses;

namespace Backend.Api.Services;

public class OptimizationJobStore
{
    private sealed class Job
    {
        public required Guid JobId { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public JobStatus Status { get; set; } = JobStatus.Queued;
        public string? ErrorMessage { get; set; }
        public OptimizationResultDto? Result { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, Job> _jobs = new();

    public List<EngineerInputDto>? LastEngineers { get; private set; }
    private volatile OptimizationResultDto? _lastResult;
    public OptimizationResultDto? LastResult => _lastResult;

    public JobStatusResponseDto Create()
    {
        var job = new Job { JobId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        _jobs[job.JobId] = job;
        return ToStatus(job);
    }

    public void SetLastEngineers(List<EngineerInputDto> engineers) => LastEngineers = engineers;

    public bool MoveToProcessing(Guid jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Processing;
            return true;
        }
        return false;
    }

    public bool Complete(Guid jobId, OptimizationResultDto result)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Completed;
            job.Result = result;
            _lastResult = result;
            return true;
        }
        return false;
    }

    public bool Fail(Guid jobId, string errorMessage)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = errorMessage;
            return true;
        }
        return false;
    }

    public JobStatusResponseDto? GetStatus(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? ToStatus(job) : null;

    public bool TryGetResult(Guid jobId, out OptimizationResultDto? result)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            result = job.Result;
            return true;
        }

        result = null;
        return false;
    }

    public void UpdateOrderStatus(string externalOrderId, OrderStatus newStatus)
    {
        if (string.IsNullOrWhiteSpace(externalOrderId))
            return;

        if (_lastResult != null)
        {
            var modifiedRoutes = _lastResult.Routes.Select(r =>
            {
                if (!r.Stops.Any(s => string.Equals(s.ExternalOrderId, externalOrderId, StringComparison.OrdinalIgnoreCase)))
                    return r;

                var updatedStops = r.Stops.Select(s =>
                    string.Equals(s.ExternalOrderId, externalOrderId, StringComparison.OrdinalIgnoreCase)
                        ? s with { Status = newStatus }
                        : s
                ).ToList();

                var activeCount = updatedStops.Count(s => s.Status is OrderStatus.Dispatched or OrderStatus.InProgress);
                var completedCount = updatedStops.Count(s => s.Status == OrderStatus.Completed);
                var cancelledCount = updatedStops.Count(s => s.Status == OrderStatus.Cancelled);

                return r with
                {
                    Stops = updatedStops,
                    ActiveOrdersCount = activeCount,
                    CompletedOrdersCount = completedCount,
                    CancelledOrdersCount = cancelledCount
                };
            }).ToList();

            _lastResult = _lastResult with { Routes = modifiedRoutes };
        }

        foreach (var job in _jobs.Values)
        {
            if (job.Result != null && job.Result.Routes.Any(r => r.Stops.Any(s => string.Equals(s.ExternalOrderId, externalOrderId, StringComparison.OrdinalIgnoreCase))))
            {
                var routes = job.Result.Routes.Select(r =>
                {
                    if (!r.Stops.Any(s => string.Equals(s.ExternalOrderId, externalOrderId, StringComparison.OrdinalIgnoreCase)))
                        return r;

                    var updatedStops = r.Stops.Select(s => string.Equals(s.ExternalOrderId, externalOrderId, StringComparison.OrdinalIgnoreCase)
                        ? s with { Status = newStatus }
                        : s).ToList();

                    var activeCount = updatedStops.Count(s => s.Status is OrderStatus.Dispatched or OrderStatus.InProgress);
                    var completedCount = updatedStops.Count(s => s.Status == OrderStatus.Completed);
                    var cancelledCount = updatedStops.Count(s => s.Status == OrderStatus.Cancelled);

                    return r with
                    {
                        Stops = updatedStops,
                        ActiveOrdersCount = activeCount,
                        CompletedOrdersCount = completedCount,
                        CancelledOrdersCount = cancelledCount
                    };
                }).ToList();

                job.Result = job.Result with { Routes = routes };
            }
        }
    }

    private static JobStatusResponseDto ToStatus(Job job) =>
        new(job.JobId, job.Status, job.ErrorMessage, job.CreatedAt);
}