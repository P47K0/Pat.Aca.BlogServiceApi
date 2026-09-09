namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// Comment length caps, bound from configuration ("Comments" section of
    /// appsettings.json/ACA environment variables) the same way CosmosSettings
    /// is — a plain POCO registered as a singleton, not IOptions&lt;T&gt;,
    /// matching this project's existing convention. Deliberately not
    /// hardcoded constants in CommentWriteValidation: these are the kind of
    /// value a real deployment may want to tune (e.g. loosen MaxTextLength)
    /// without a redeploy. The defaults below are what a missing/incomplete
    /// "Comments" config section falls back to.
    /// </summary>
    public sealed class CommentSettings
    {
        public int MaxAuthorNameLength { get; set; } = 100;
        public int MaxTextLength { get; set; } = 2000;
        public int MaxEmailLength { get; set; } = 254;
    }
}
