using Backend.Contracts.Requests;

namespace Backend.RoutingEngine.Solvers;

/// <summary>Окно обеда бригады (старт выбрасывается солвером внутри окна).</summary>
public sealed record LunchWindow(DateTimeOffset Start, DateTimeOffset End, int DurationMinutes);

/// <summary>
/// Политика обедов: каскадные окна — пока одни бригады едят, другие работают.
/// Если у бригады нет собственного окна, окно генерируется из ядра 11:00–15:00
/// с равномерным сдвигом по числу бригад. Окно пересекается со сменой;
/// если пересечения меньше длительности обеда — обед бригаде не назначается.
/// </summary>
public static class LunchPolicy
{
    public const int DefaultDurationMinutes = 60;

    private static readonly TimeSpan CoreStart = new(11, 0, 0);
    private static readonly TimeSpan CoreEnd = new(15, 0, 0);
    private const int CoreWidthMinutes = 240;

    /// <summary>Возвращает окно обеда для инженера либо null, если обед неприменим.</summary>
    public static LunchWindow? Resolve(EngineerInputDto engineer, int engineerIndex, int totalEngineers)
    {
        int duration = Math.Max(1, engineer.LunchDurationMinutes);

        DateTimeOffset windowStart, windowEnd;
        if (engineer.LunchStart.HasValue && engineer.LunchEnd.HasValue)
        {
            windowStart = engineer.LunchStart.Value;
            windowEnd = engineer.LunchEnd.Value;
        }
        else
        {
            // Каскад: окно_i = [11:00 + i·step, +D]; для одной бригады — центр ядра.
            var anchor = new DateTimeOffset(
                engineer.ShiftStart.Year, engineer.ShiftStart.Month, engineer.ShiftStart.Day,
                CoreStart.Hours, CoreStart.Minutes, CoreStart.Seconds, engineer.ShiftStart.Offset);

            if (totalEngineers <= 1)
            {
                windowStart = anchor.AddMinutes((CoreWidthMinutes - duration) / 2.0);
                windowEnd = windowStart.AddMinutes(duration);
            }
            else
            {
                double step = (CoreWidthMinutes - duration) / (double)(totalEngineers - 1);
                windowStart = anchor.AddMinutes(engineerIndex * step);
                windowEnd = windowStart.AddMinutes(duration);
            }
        }

        // Пересечение со сменой.
        DateTimeOffset lunchStart = windowStart < engineer.ShiftStart ? engineer.ShiftStart : windowStart;
        DateTimeOffset lunchEnd = windowEnd > engineer.ShiftEnd ? engineer.ShiftEnd : windowEnd;

        if (lunchEnd - lunchStart < TimeSpan.FromMinutes(duration))
            return null;

        return new LunchWindow(lunchStart, lunchEnd, duration);
    }
}