namespace ShowUp.Core.Marking;

/// <summary>Sound and haptics for marking. Toggles persist through <c>IAppPreferences</c>.</summary>
public interface IMarkingFeedback
{
    /// <summary>A marker stroke starts: squeak + light tick.</summary>
    void StrokeDrawn();

    /// <summary>A session of a 3+ session day was ticked without completing a stroke.</summary>
    void SessionTicked();

    /// <summary>The cross moment's last stroke landed: stronger haptic.</summary>
    void CrossLanded();

    bool SoundEnabled { get; set; }

    bool HapticsEnabled { get; set; }
}
