import type { Article } from '../types';

export interface SeriesNav {
  seriesName: string;
  /** 1-based position of the current article within the series. */
  position: number;
  total: number;
  prev: Article | null;
  next: Article | null;
}

/** Resolves an article's series prev/next nav from the full article list.
 * Returns null when the article isn't part of a series, or (defensively) if
 * it's somehow not found among its own series' siblings. Siblings missing a
 * seriesOrder sort first (treated as 0) rather than throwing — shouldn't
 * happen for real data, but avoids a crash if it ever does. */
export function resolveSeriesNav(article: Article, allArticles: Article[]): SeriesNav | null {
  if (!article.seriesName) {
    return null;
  }

  const siblings = allArticles
    .filter((a) => a.seriesName === article.seriesName)
    .sort((a, b) => (a.seriesOrder ?? 0) - (b.seriesOrder ?? 0));
  const index = siblings.findIndex((a) => a.slug === article.slug);
  if (index === -1) {
    return null;
  }

  return {
    seriesName: article.seriesName,
    position: index + 1,
    total: siblings.length,
    prev: index > 0 ? siblings[index - 1] : null,
    next: index < siblings.length - 1 ? siblings[index + 1] : null,
  };
}

/** Resolves an article's relatedSlugs (plain slug strings) to full Article
 * objects, so the detail page can show real titles/dates — the field alone
 * only carries slugs. A related slug with no matching article (deleted,
 * unpublished, or a typo) is silently dropped rather than erroring. */
export function resolveRelatedArticles(article: Article, allArticles: Article[]): Article[] {
  if (!article.relatedSlugs || article.relatedSlugs.length === 0) {
    return [];
  }

  const bySlug = new Map(allArticles.map((a) => [a.slug, a]));
  return article.relatedSlugs
    .map((slug) => bySlug.get(slug))
    .filter((a): a is Article => a !== undefined);
}
