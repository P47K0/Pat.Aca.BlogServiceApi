namespace Pat.Aca.BlogCommentsModerationFunction
{
    public interface IPublishedCommentsRepository
    {
        /// <summary>
        /// Every published comment, grouped by article slug, oldest first
        /// within an article (the order the API's GET .../comments uses).
        /// </summary>
        Task<Dictionary<string, List<PublishedComment>>> GetPublishedCommentsBySlugAsync(CancellationToken cancellationToken = default);
    }
}
