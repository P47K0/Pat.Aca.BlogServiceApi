namespace Pat.Aca.BlogServiceApi
{
    public record Article(int Id, string Slug, string Title, string Summary, string Content, DateTime PublishedAt, List<string> Tags, int ViewCount = 0);
}
