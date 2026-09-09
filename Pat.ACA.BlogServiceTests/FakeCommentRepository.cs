using Pat.Aca.BlogServiceApi;

// A fake ICommentRepository, swapped in for CosmosCommentRepository in tests
// via WithWebHostBuilder/ConfigureServices. Storage is an instance field
// (not static) — see InMemoryCommentRepository's own note on why, to avoid
// FakeArticleRepository's known cross-test-fixture state-sharing flakiness.
public sealed class FakeCommentRepository : ICommentRepository
{
    private readonly List<Comment> _comments = new()
    {
        new Comment("published-comment-1", "first-article", "Alice", "Great read!", DateTime.UtcNow.AddDays(-1), CommentStatus.Published),
        new Comment("unpublished-comment-1", "first-article", "Bob", "Needs review.", DateTime.UtcNow.AddHours(-2), CommentStatus.Unpublished, LlmScore: 2, Email: "bob@example.com"),
        new Comment("queued-comment-1", "first-article", "Carol", "Awaiting moderation.", DateTime.UtcNow.AddMinutes(-5), CommentStatus.Queued)
    };

    public Task<Comment> CreateCommentAsync(string articleSlug, CommentWriteRequest request)
    {
        var comment = new Comment(
            Guid.NewGuid().ToString(),
            articleSlug,
            request.AuthorName,
            request.Text,
            DateTime.UtcNow,
            CommentStatus.Queued,
            Email: request.Email);

        _comments.Add(comment);
        return Task.FromResult(comment);
    }

    public Task<List<Comment>> GetPublishedCommentsAsync(string articleSlug) =>
        Task.FromResult(_comments
            .Where(c => c.ArticleSlug == articleSlug && c.Status == CommentStatus.Published)
            .OrderBy(c => c.CreatedAt)
            .ToList());

    public Task<List<Comment>> GetAllCommentsAsync(string articleSlug) =>
        Task.FromResult(_comments
            .Where(c => c.ArticleSlug == articleSlug)
            .OrderByDescending(c => c.CreatedAt)
            .ToList());

    public Task<Comment?> UpdateCommentStatusAsync(string articleSlug, string commentId, string status)
    {
        var index = _comments.FindIndex(c => c.ArticleSlug == articleSlug && c.Id == commentId);
        if (index < 0)
        {
            return Task.FromResult<Comment?>(null);
        }

        var updated = _comments[index] with { Status = status };
        _comments[index] = updated;
        return Task.FromResult<Comment?>(updated);
    }

    public Task<bool> DeleteCommentAsync(string articleSlug, string commentId)
    {
        var index = _comments.FindIndex(c => c.ArticleSlug == articleSlug && c.Id == commentId);
        if (index < 0)
        {
            return Task.FromResult(false);
        }

        _comments.RemoveAt(index);
        return Task.FromResult(true);
    }
}
