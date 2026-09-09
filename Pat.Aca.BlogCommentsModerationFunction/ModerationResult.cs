namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// What CommentModerationProcessor decided for one comment -- the
    /// caller (the actual Cosmos-trigger Function, a later commit) is
    /// responsible for patching Status/LlmScore back onto the Cosmos
    /// document; this type carries exactly what that patch needs and
    /// nothing about how to apply it, keeping the processor itself
    /// Cosmos-agnostic and testable with plain fakes.
    /// </summary>
    public record ModerationResult(string Status, int Score);
}
