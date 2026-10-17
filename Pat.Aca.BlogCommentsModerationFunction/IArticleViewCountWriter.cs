namespace Pat.Aca.BlogCommentsModerationFunction
{
    public interface IArticleViewCountWriter
    {
        /// <summary>Adds <paramref name="views"/> to the article's viewCount. False when no article has this slug.</summary>
        Task<bool> AddViewsAsync(string slug, long views, CancellationToken cancellationToken = default);
    }
}
