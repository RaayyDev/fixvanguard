namespace FixVanguard;

internal enum ActionLevel
{
    Info,
    Success,
    Warning,
    Error
}

internal readonly record struct ActionLog(string Message, ActionLevel Level, DateTime Time)
{
    public ActionLog(string message, ActionLevel level)
        : this(message, level, DateTime.Now)
    {
    }
}
