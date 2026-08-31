namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// Validates POST/PUT /articles request bodies. Pulled out for the same
    /// reason as ApiSecurity — unit-testable without a running host. Per the
    /// BRD, only slug/title/content are enforced-required; summary/publishedAt/
    /// tags are pass-through with no extra validation.
    /// </summary>
    public static class ArticleWriteValidation
    {
        public static List<string> Validate(ArticleWriteRequest request)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(request.Slug))
            {
                errors.Add("slug is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                errors.Add("title is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Content))
            {
                errors.Add("content is required.");
            }

            return errors;
        }
    }
}
