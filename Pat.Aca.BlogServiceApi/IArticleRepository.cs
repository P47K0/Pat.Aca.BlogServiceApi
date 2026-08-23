namespace Pat.Aca.BlogServiceApi
{
    public interface IArticleRepository
    {
        Task<List<Article>> GetArticlesAsync();
        Task<Article?> GetArticleBySlugAsync(string slug);
    }
}
