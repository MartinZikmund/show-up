namespace ShowUp.Services.Rating;

public interface IAppRatingService
{
    Task TryPromptForRatingAsync();
}
