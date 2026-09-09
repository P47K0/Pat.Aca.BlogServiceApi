using Microsoft.Azure.Cosmos;

namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Patches a comment's status/llmScore back onto its Cosmos document
    /// once CommentModerationProcessor has decided them -- the one write
    /// this Function makes to an actual comment document (as opposed to
    /// CosmosModerationQuotaStore's synthetic counter document). Takes the
    /// same shared Comments Container as CosmosModerationQuotaStore (built
    /// once in Program.cs) rather than its own CosmosClient -- see that
    /// class's own doc comment for why.
    ///
    /// Uses Replace, not Set, for both paths -- both already exist on every
    /// comment document from creation (status is always set at write time;
    /// llmScore is written as an explicit JSON null by CommentDocument in
    /// the sibling API project, not omitted like Email is), so Replace's
    /// stricter "path must already exist" requirement is a correct
    /// assumption here, not an oversight -- it'd surface loudly if that
    /// ever stopped being true, rather than silently creating an unexpected
    /// field shape.
    /// </summary>
    public sealed class CommentStatusWriter
    {
        private readonly Container _container;

        public CommentStatusWriter(Container commentsContainer)
        {
            _container = commentsContainer;
        }

        public Task ApplyResultAsync(string articleSlug, string commentId, ModerationResult result) =>
            _container.PatchItemAsync<object>(
                commentId,
                new PartitionKey(articleSlug),
                new[]
                {
                    PatchOperation.Replace("/status", result.Status),
                    PatchOperation.Replace("/llmScore", result.Score)
                });
    }
}
