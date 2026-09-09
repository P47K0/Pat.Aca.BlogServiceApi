namespace Pat.Aca.BlogServiceApi
{
    // Unlike InMemoryArticleRepository's static SeedArticles list, this
    // storage is an instance field on purpose — ICommentRepository is still
    // registered as a DI singleton (so it behaves identically at runtime),
    // but keeping it instance-level sidesteps the cross-test-fixture state
    // sharing that made FakeArticleRepository's article counts flaky under
    // concurrent test runs (see ApiWriteIntegrationTests.cs's note on that).
    public class InMemoryCommentRepository : ICommentRepository
    {
        private readonly List<Comment> _comments = new();

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
}
