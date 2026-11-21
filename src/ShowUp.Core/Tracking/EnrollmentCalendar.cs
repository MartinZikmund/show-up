namespace ShowUp.Core.Tracking;

public sealed class EnrollmentCalendar
{
    private readonly DateOnly[] _dates;

    public EnrollmentCalendar(DateOnly startDate, int dayCount, IReadOnlyList<ScheduleShift> shifts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dayCount, 1);
        ArgumentNullException.ThrowIfNull(shifts);

        _dates = new DateOnly[dayCount];
        for (int i = 0; i < dayCount; i++)
        {
            _dates[i] = startDate.AddDays(i);
        }

        foreach (ScheduleShift shift in shifts)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(shift.Days, 1, nameof(shifts));

            for (int i = 0; i < _dates.Length; i++)
            {
                if (_dates[i] >= shift.FromDate)
                {
                    _dates[i] = _dates[i].AddDays(shift.Days);
                }
            }
        }
    }

    public DateOnly EndDate => _dates[^1];

    public DateOnly DateOf(int dayIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(dayIndex, _dates.Length);
        return _dates[dayIndex];
    }

    public int? DayIndexOn(DateOnly date)
    {
        int index = Array.IndexOf(_dates, date);
        return index < 0 ? null : index;
    }
}
