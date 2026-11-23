namespace ShowUp.Core.Services;

/// <summary>The system's "show animations" setting: off means draw the end state, no motion.</summary>
public interface IMotionPreference
{
    bool AnimationsEnabled { get; }
}
