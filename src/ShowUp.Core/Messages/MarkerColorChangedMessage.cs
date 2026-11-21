using ShowUp.Core.Marking;

namespace ShowUp.Core.Messages;

/// <summary>Sent when the user picks another marker color; the head repaints the marker brushes.</summary>
public sealed record MarkerColorChangedMessage(MarkerColor Color);
