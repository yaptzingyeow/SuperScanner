import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer, request as proxyRequest } from 'node:http';
import { extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../dist/web/browser/', import.meta.url));
const host = '127.0.0.1';
const port = 4300;
const types = new Map([
  ['.css', 'text/css; charset=utf-8'],
  ['.html', 'text/html; charset=utf-8'],
  ['.ico', 'image/x-icon'],
  ['.js', 'text/javascript; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
]);

if (!existsSync(join(root, 'index.html'))) {
  throw new Error('Build the Angular E2E bundle before starting its server.');
}

const server = createServer((incoming, outgoing) => {
  const requestUrl = new URL(incoming.url ?? '/', `http://${host}:${port}`);
  if (requestUrl.pathname.startsWith('/api/')) {
    const proxy = proxyRequest(
      {
        hostname: '127.0.0.1',
        port: 5080,
        path: `${requestUrl.pathname}${requestUrl.search}`,
        method: incoming.method,
        headers: incoming.headers,
      },
      (response) => {
        outgoing.writeHead(response.statusCode ?? 502, response.headers);
        response.pipe(outgoing);
      },
    );
    proxy.on('error', () => {
      outgoing.writeHead(502).end();
    });
    incoming.pipe(proxy);
    return;
  }

  const relativePath = normalize(decodeURIComponent(requestUrl.pathname)).replace(
    /^(\.\.(\\|\/|$))+/,
    '',
  );
  let filePath = join(root, relativePath);
  if (!existsSync(filePath) || !statSync(filePath).isFile()) filePath = join(root, 'index.html');

  outgoing.writeHead(200, {
    'Content-Type': types.get(extname(filePath)) ?? 'application/octet-stream',
    'Cache-Control': filePath.endsWith('index.html') ? 'no-store' : 'public, max-age=31536000',
    'X-Content-Type-Options': 'nosniff',
  });
  createReadStream(filePath).pipe(outgoing);
});

server.listen(port, host);
for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => {
    server.closeAllConnections();
    server.close();
    process.exit(0);
  });
}
