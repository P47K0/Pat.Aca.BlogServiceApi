using Pat.Aca.BlogCommentsModerationFunction;
using Xunit;

namespace Pat.Aca.BlogCommentsModerationFunctionTests
{
    public class TrendingArticlePickerTests
    {
        [Fact]
        public void PickSlug_returns_null_when_there_are_no_views()
        {
            Assert.Null(TrendingArticlePicker.PickSlug(new Dictionary<string, long>(), new Random(1)));
        }

        [Fact]
        public void PickSlug_returns_the_slug_with_the_most_views()
        {
            var views = new Dictionary<string, long> { ["a"] = 3, ["b"] = 9, ["c"] = 5 };
            Assert.Equal("b", TrendingArticlePicker.PickSlug(views, new Random(1)));
        }

        [Fact]
        public void PickSlug_chooses_among_tied_leaders_only_and_reaches_each_of_them()
        {
            var views = new Dictionary<string, long> { ["docker"] = 9, ["rag"] = 9, ["other"] = 4 };
            var random = new Random(42);
            var picked = Enumerable.Range(0, 50).Select(_ => TrendingArticlePicker.PickSlug(views, random)).ToHashSet();
            Assert.Equal(new HashSet<string?> { "docker", "rag" }, picked);
        }
    }
}
