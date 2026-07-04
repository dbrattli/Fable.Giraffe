// Hermetic smoke test for the Fable.Giraffe JS/Node backend.
//
// Imports the compiled example app's `webApp` handler and drives it through the
// Connect middleware (`Server.toMiddleware`) with in-process fake req/res
// objects — no real socket, no port, no flakiness. Verifies routing, the text
// and json handlers, request-body reading, and 404 fall-through (next()).

// The middleware is built inside F# (Program.fs) and exported as a plain
// Connect `(req, res, next)` function — exactly what `app.use(...)` receives.
import { middleware } from "../../build/test-js/Program.js";

// Minimal fake of a Node http.IncomingMessage stream + ServerResponse.
function invoke(method, url, body = "") {
  return new Promise((resolve) => {
    const req = {
      method,
      url,
      headers: { "content-type": "text/plain" },
      on(event, cb) {
        if (event === "data" && body) cb(Buffer.from(body));
        if (event === "end") cb();
      },
    };
    let statusCode = 200;
    const headers = {};
    const res = {
      get statusCode() { return statusCode; },
      set statusCode(v) { statusCode = v; },
      setHeader(k, v) { headers[k.toLowerCase()] = v; },
      end(chunk) {
        const text = chunk == null ? "" : Buffer.from(chunk).toString();
        resolve({ status: statusCode, headers, body: text });
      },
    };
    // next() → route did not match; represent as a 404 sentinel.
    middleware(req, res, () => resolve({ status: 404, headers, body: "<next>" }));
  });
}

const cases = [
  { name: "GET /ping", call: () => invoke("GET", "/ping"), status: 200, body: "pong" },
  { name: "GET /json", call: () => invoke("GET", "/json"), status: 200, body: '{"Name":"Dag","Age":53}' },
  { name: "POST /echo", call: () => invoke("POST", "/echo", "hello node"), status: 200, body: "hello node" },
  { name: "GET /missing → next()", call: () => invoke("GET", "/missing"), status: 404 },
];

let failed = 0;
for (const c of cases) {
  const r = await c.call();
  const okStatus = r.status === c.status;
  const okBody = c.body === undefined || r.body === c.body;
  if (okStatus && okBody) {
    console.log(`  ok   ${c.name} -> ${r.status} ${JSON.stringify(r.body)}`);
  } else {
    failed++;
    console.error(`  FAIL ${c.name} -> got ${r.status} ${JSON.stringify(r.body)}, expected ${c.status} ${JSON.stringify(c.body)}`);
  }
}

if (failed > 0) {
  console.error(`\n${failed} JS smoke test(s) failed`);
  process.exit(1);
}
console.log(`\nAll ${cases.length} JS smoke tests passed`);
