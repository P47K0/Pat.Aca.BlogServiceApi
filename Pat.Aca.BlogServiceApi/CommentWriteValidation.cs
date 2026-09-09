using System.Net.Mail;

namespace Pat.Aca.BlogServiceApi
{
    /// <summary>
    /// Validates POST /articles/{slug}/comments request bodies. Pulled out
    /// for the same reason as ArticleWriteValidation/ApiSecurity —
    /// unit-testable without a running host. AuthorName and Text are
    /// required and length-capped; Email is optional but, if present, is
    /// length-capped and format-checked (no confirmation/verification flow —
    /// just "does this look like a real address").
    /// </summary>
    public static class CommentWriteValidation
    {
        /// <summary>
        /// Trims leading/trailing whitespace from every field, and normalizes
        /// a whitespace-only Email to null — so a value that's only spaces
        /// correctly fails Validate's "required"/"absent" checks instead of
        /// sneaking through, and nothing stored has stray surrounding
        /// whitespace. Callers should always call this before Validate and
        /// before persisting the request.
        /// </summary>
        public static CommentWriteRequest Trim(CommentWriteRequest request) => request with
        {
            AuthorName = request.AuthorName?.Trim() ?? string.Empty,
            Text = request.Text?.Trim() ?? string.Empty,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim()
        };

        public static List<string> Validate(CommentWriteRequest request, CommentSettings settings)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(request.AuthorName))
            {
                errors.Add("authorName is required.");
            }
            else if (request.AuthorName.Length > settings.MaxAuthorNameLength)
            {
                errors.Add($"authorName must be {settings.MaxAuthorNameLength} characters or fewer.");
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                errors.Add("text is required.");
            }
            else if (request.Text.Length > settings.MaxTextLength)
            {
                errors.Add($"text must be {settings.MaxTextLength} characters or fewer.");
            }

            if (!string.IsNullOrEmpty(request.Email))
            {
                if (request.Email.Length > settings.MaxEmailLength)
                {
                    errors.Add($"email must be {settings.MaxEmailLength} characters or fewer.");
                }
                else if (!IsValidEmailFormat(request.Email))
                {
                    errors.Add("email is not a valid email address.");
                }
            }

            return errors;
        }

        // System.Net.Mail.MailAddress's own parser is a pragmatic stand-in for
        // a hand-rolled regex here — good enough for "does this look like a
        // real address" without writing/maintaining a fragile pattern.
        private static bool IsValidEmailFormat(string email)
        {
            try
            {
                _ = new MailAddress(email);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
