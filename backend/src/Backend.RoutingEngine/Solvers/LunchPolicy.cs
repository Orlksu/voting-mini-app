using Backend.Contracts.Requests;

namespace Backend.RoutingEngine.Solvers;

/// <summary>Окно обеда бригады (старт выбрасывается солвером внутри окна).</summary>
public sealed record LunchWindow(DateTimeOffset Start, DateTimeOffset End, int DurationMinutes);

/// <summary>
/// Политика обедов: каскадные окна — пока одни бригады едят, другие работают.
/// Ядро обеда вычисляется динамически: центральные 4 часа смены.
/// </summary>
public static class LunchPolicy
{
    public const int DefaultDurationMinutes = 60;
    
    // Ширина "плавающего" ядра для обеда (4 часа)
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
            var shiftDuration = (engineer.ShiftEnd - engineer.ShiftStart).TotalMinutes;
            
            // Сужаем "плавающее" окно, чтобы не взрывать пространство поиска для OR-Tools.
            // Окно будет длиться ровно 2 часа в середине смены.
            double coreWidth = Math.Min(120, shiftDuration);
            double startOffset = (shiftDuration - coreWidth) / 2.0;
            
            var anchor = engineer.ShiftStart.AddMinutes(startOffset);

            // Снова включаем каскад (разброс бригад внутри этих 2 часов)
            int stepMinutes = totalEngineers > 1
                ? Math.Max(duration, (int)(coreWidth / totalEngineers))
                : 0;

            windowStart = anchor.AddMinutes(stepMinutes * engineerIndex);
            // Окно для конкретного инженера: ровно время на обед + 30 минут люфта
            windowEnd = windowStart.AddMinutes(duration + 30);
            
            var coreEnd = anchor.AddMinutes(coreWidth);
            if (windowEnd > coreEnd) windowEnd = coreEnd;
        }

        // Пересечение со сменой.
        DateTimeOffset lunchStart = windowStart < engineer.ShiftStart ? engineer.ShiftStart : windowStart;
        DateTimeOffset lunchEnd = windowEnd > engineer.ShiftEnd ? engineer.ShiftEnd : windowEnd;

        if (lunchEnd - lunchStart < TimeSpan.FromMinutes(duration))
            return null;

        return new LunchWindow(lunchStart, lunchEnd, duration);
    }
}