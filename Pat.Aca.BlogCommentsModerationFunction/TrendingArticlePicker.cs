namespace Pat.Aca.BlogCommentsModerationFunction
{
    /// <summary>
    /// Picks the slug with the most external-referrer views. When several
    /// slugs share the top count, one of them is chosen at random, so the
    /// homepage tile does not always favour the same article.
    /// </summary>
    public static class TrendingArticlePicker
    {
        public static string? PickSlug(IReadOnlyDictionary<string, long> externalViews, Random random)
        {
            if (externalViews.Count == 0) return null;
            var top = externalViews.Values.Max();
            if (top <= 0) return null;
            var leaders = externalViews.Where(kv => kv.Value == top).Select(kv => kv.Key).OrderBy(s => s, StringComparer.Ordinal).ToList();
            return leaders[random.Next(leaders.Count)];
        }
    }
}
