export default {
  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === "/healthz") {
      return new Response("ok");
    }

    return new Response("Not found", { status: 404 });
  },
} satisfies ExportedHandler;
