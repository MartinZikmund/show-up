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

    /// <summary>A drawn stroke starts: light tick, and the scratch loop starts (silent until it moves).</summary>
    void ScratchStart();

    /// <summary>Pen speed in normalized square units per 16 ms frame; drives the scratch's volume and pitch.</summary>
    void ScratchSpeed(double normalizedSpeed);

    void ScratchStop();

    /// <summary>A drawn cross was accepted: short marker sound + 30 ms haptic.</summary>
    void DayTaken();

    bool SoundEnabled { get; set; }

    bool HapticsEnabled { get; set; }
}
