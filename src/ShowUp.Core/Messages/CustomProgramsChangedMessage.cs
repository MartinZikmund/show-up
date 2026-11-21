namespace ShowUp.Core.Messages;

/// <summary>Sent after a custom program is saved or deleted.</summary>
public sealed record CustomProgramsChangedMessage(string ProgramId);
