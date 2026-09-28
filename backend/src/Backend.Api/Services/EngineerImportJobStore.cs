using System.Collections.Concurrent;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Responses;

namespace Backend.Api.Services;

public class EngineerImportJobStore
{
    private sealed class ImportJob
    {
        public required Guid JobId { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public JobStatus Status { get; set; } = JobStatus.Queued;
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public string CurrentStage { get; set; } = "В очереди";
        public EngineerImportResultDto? Result { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private const int MaxCapacity = 200;
    private readonly ConcurrentDictionary<Guid, ImportJob> _jobs = new();

    public EngineerImportJobStatusDto Create(int initialTotal = 0)
    {
        if (_jobs.Count >= MaxCapacity)
        {
            var oldJobs = _jobs.Values
                .Where(j => j.Status is JobStatus.Completed or JobStatus.Failed)
                .OrderBy(j => j.CreatedAt)
                .Take(_jobs.Count - MaxCapacity + 10)
                .ToList();

            foreach (var old in oldJobs)
            {
                _jobs.TryRemove(old.JobId, out _);
            }
        }

        var job = new ImportJob
        {
            JobId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            TotalItems = initialTotal,
            CurrentStage = "Инициализация импорта..."
        };
        _jobs[job.JobId] = job;
        return ToDto(job);
    }

    public void UpdateProgress(Guid jobId, int processed, int total, string stage)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Processing;
            job.ProcessedItems = processed;
            job.TotalItems = total;
            job.CurrentStage = stage;
        }
    }

    public void Complete(Guid jobId, EngineerImportResultDto result)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Completed;
            job.Result = result;
            job.CurrentStage = "Завершено";
            job.ProcessedItems = job.TotalItems;
        }
    }

    public void Fail(Guid jobId, string errorMessage)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = errorMessage;
            job.CurrentStage = "Ошибка импорта";
        }
    }

    public EngineerImportJobStatusDto? GetStatus(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? ToDto(job) : null;

    private static EngineerImportJobStatusDto ToDto(ImportJob job)
    {
        var progress = job.TotalItems > 0
            ? (int)Math.Clamp(Math.Round((double)job.ProcessedItems / job.TotalItems * 100), 0, 100)
            : (job.Status == JobStatus.Completed ? 100 : 0);

        return new EngineerImportJobStatusDto(
            job.JobId,
            job.Status,
            job.TotalItems,
            job.ProcessedItems,
            progress,
            job.CurrentStage,
            job.Result,
            job.ErrorMessage,
            job.CreatedAt
        );
    }
}
