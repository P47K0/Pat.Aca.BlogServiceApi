import type { FC } from 'hono/jsx';

export const ErrorPage: FC = () => (
  <div class="text-center py-16">
    <h1 class="text-2xl font-bold">Something went wrong</h1>
    <p class="mt-2 text-gray-500">
      Couldn't reach the blog service right now. Please try again shortly.
    </p>
    <a href="/" class="mt-4 inline-block text-sm text-gray-500 transition hover:text-blue-600">
      ← Back home
    </a>
  </div>
);
