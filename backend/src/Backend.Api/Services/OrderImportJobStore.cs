using System.Collections.Concurrent;
using Backend.Contracts.Domain.Enums;
using Backend.Contracts.Responses;

namespace Backend.Api.Services;

public class OrderImportJobStore
{
    private sealed class ImportJob
    {
        public required Guid JobId { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public JobStatus Status { get; set; } = JobStatus.Queued;
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public string CurrentStage { get; set; } = "В очереди";
        public OrderImportResultDto? Result { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, ImportJob> _jobs = new();

    public OrderImportJobStatusDto Create(int initialTotal = 0)
    {
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

    public void Complete(Guid jobId, OrderImportResultDto result)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Completed;
            job.ProcessedItems = job.TotalItems;
            job.CurrentStage = "Импорт успешно завершён";
            job.Result = result;
        }
    }

    public void Fail(Guid jobId, string errorMessage)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Failed;
            job.CurrentStage = "Ошибка импорта";
            job.ErrorMessage = errorMessage;
        }
    }

    public OrderImportJobStatusDto? GetStatus(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? ToDto(job) : null;

    private static OrderImportJobStatusDto ToDto(ImportJob job)
    {
        int percent = job.TotalItems > 0
            ? Math.Clamp((int)Math.Round((double)job.ProcessedItems / job.TotalItems * 100), 0, 100)
            : (job.Status == JobStatus.Completed ? 100 : 0);

        return new OrderImportJobStatusDto(
            job.JobId,
            job.Status,
            job.TotalItems,
            job.ProcessedItems,
            percent,
            job.CurrentStage,
            job.Result,
            job.ErrorMessage,
            job.CreatedAt);
    }
}
