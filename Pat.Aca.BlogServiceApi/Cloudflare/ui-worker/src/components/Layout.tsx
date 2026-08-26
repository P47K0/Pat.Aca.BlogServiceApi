import type { FC, PropsWithChildren } from 'hono/jsx';

export const Layout: FC<PropsWithChildren<{ title: string }>> = ({ title, children }) => (
  <html lang="en">
    <head>
      <meta charSet="UTF-8" />
      <meta name="viewport" content="width=device-width, initial-scale=1.0" />
      <title>{title} · koorevaar.com blog</title>
      {/* Same favicon as koorevaar.com's other pages (e.g. /contact), for a
          consistent brand identity across the whole domain. */}
      <link
        rel="icon"
        type="image/svg+xml"
        href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'%3E%3Crect width='200' height='200' rx='40' fill='%231e3a8a'/%3E%3Cpath d='M50 70 L100 110 L150 70' stroke='%23dbeafe' stroke-width='18' fill='none' stroke-linecap='round'/%3E%3Crect x='40' y='65' width='120' height='85' rx='15' fill='none' stroke='%23dbeafe' stroke-width='18'/%3E%3C/svg%3E"
      />
      {/* Tailwind Play CDN, per the scaffolding decision — no build step,
          matches api-proxy's no-local-testing minimalism. The `?plugins=typography`
          query loads the prose classes used for rendered article content. */}
      <script src="https://cdn.tailwindcss.com?plugins=typography"></script>
    </head>
    <body class="min-h-screen flex flex-col bg-gray-100 text-gray-900">
      <header class="border-b border-gray-200 px-6 py-4">
        <div class="mx-auto flex max-w-2xl items-center justify-between">
          <a href="/" class="text-xl font-semibold transition hover:text-blue-600">
            koorevaar.com blog
          </a>
          {/* Same button/copy/icon as the koorevaar.com contact page's Home
              link, back to the main site (not this blog's own "/"). */}
          <a
            href="https://www.koorevaar.com"
            class="flex items-center gap-2 rounded-2xl border border-gray-300 bg-white px-5 py-2.5 font-medium text-gray-700 shadow-sm transition hover:bg-gray-50 hover:text-blue-600"
          >
            <svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth="2"
                d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1v-5m10-10l2 2m-2-2v10a1 1 0 01-1 1v-5m-6 0a1 1 0 001-1v5"
              />
            </svg>
            Home
          </a>
        </div>
      </header>
      <main class="flex-1 w-full max-w-2xl mx-auto px-6 py-10">{children}</main>
      <footer class="border-t border-gray-200 px-6 py-4 text-center text-sm text-gray-500">
        Served by ui-worker via api-proxy · Pat.Aca.BlogServiceApi
      </footer>
    </body>
  </html>
);
