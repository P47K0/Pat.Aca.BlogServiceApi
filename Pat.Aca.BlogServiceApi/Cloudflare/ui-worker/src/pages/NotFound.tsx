import type { FC } from 'hono/jsx';

export const NotFoundPage: FC = () => (
  <div class="text-center py-16">
    <h1 class="text-2xl font-bold">Page not found</h1>
    <a href="/" class="mt-4 inline-block text-sm text-gray-500 transition hover:text-blue-600">
      ← Back home
    </a>
  </div>
);
