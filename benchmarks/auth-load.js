// Authenticated read-only load profile for Inkwell, CarParker and TechLandier.
//
// Logs in once per role in setup, then reuses the auth cookie for every VU. No writes.
// Environment: BASE_URL, APP, PROFILE (rps|vus), RPS, VUS, DURATION, SUMMARY_PATH.

import http from 'k6/http';
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const BASE_URL = (__ENV.BASE_URL || 'http://127.0.0.1:5100').replace(/\/$/, '');
const APP = (__ENV.APP || 'inkwell').toLowerCase();
const PROFILE = (__ENV.PROFILE || 'vus').toLowerCase();
const RPS = Number(__ENV.RPS || 100);
const VUS = Number(__ENV.VUS || 8);
const DURATION = __ENV.DURATION || '60s';
const PASSWORD = 'Password@12345';
// Read-only auth benchmark: the "bot" token makes TechLandier's view tracker skip ProductViews writes.
const UA = 'k6-benchmark-bot';

const scenario = PROFILE === 'rps'
  ? {
      load: {
        executor: 'constant-arrival-rate',
        rate: RPS,
        timeUnit: '1s',
        duration: DURATION,
        preAllocatedVUs: Math.max(20, Math.ceil(RPS / 2)),
        maxVUs: Math.max(100, RPS * 4),
      },
    }
  : {
      load: { executor: 'constant-vus', vus: VUS, duration: DURATION },
    };

export const options = {
  scenarios: scenario,
  discardResponseBodies: false,
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  thresholds: {
    http_req_failed: ['rate<0.05'],
    checks: ['rate>0.95'],
  },
};

const ACCOUNTS = {
  inkwell: {
    admin: 'inertia.demo42.00000@yopmail.com',
    customer: 'inertia.demo42.00001@yopmail.com',
  },
  carparker: {
    admin: 'inertia.demo42.carparker.admin@yopmail.com',
    customer: 'inertia.demo42.carparker.customer@yopmail.com',
  },
  techlandier: {
    admin: 'inertia.demo42.techlandier.admin@yopmail.com',
    customer: 'inertia.demo42.techlandier.customer@yopmail.com',
  },
};

const ROUTES = {
  inkwell: [
    { name: 'feed_html', path: '/posts', role: 'admin', html: true, weight: 20 },
    { name: 'post_html', path: '/posts/{postId}', role: 'admin', html: true, weight: 15 },
    { name: 'feed_json', path: '/posts', role: 'admin', html: false, weight: 10 },
    { name: 'analytics_json', path: '/analytics', role: 'admin', html: false, weight: 15 },
    { name: 'notifications_html', path: '/notifications', role: 'admin', html: true, weight: 10 },
    { name: 'settings_html', path: '/settings', role: 'admin', html: true, weight: 10 },
    { name: 'users_html', path: '/users', role: 'admin', html: true, weight: 10 },
    { name: 'feed_auth_json', path: '/feed', role: 'admin', html: false, weight: 5 },
    { name: 'admin_team_html', path: '/admin/team', role: 'admin', html: true, weight: 5 },
  ],
  carparker: [
    { name: 'account_html', path: '/account', role: 'customer', html: true, weight: 20 },
    { name: 'account_bookings_html', path: '/account/bookings', role: 'customer', html: true, weight: 10 },
    { name: 'lot_html', path: '/lots/{lotId}', role: 'customer', html: true, weight: 15 },
    { name: 'lots_html', path: '/lots', role: 'customer', html: true, weight: 10 },
    { name: 'guard_html', path: '/guard', role: 'admin', html: true, weight: 10 },
    { name: 'admin_html', path: '/admin', role: 'admin', html: true, weight: 10 },
    { name: 'admin_analytics_json', path: '/admin/analytics', role: 'admin', html: false, weight: 10 },
    { name: 'admin_bookings_html', path: '/admin/bookings', role: 'admin', html: true, weight: 10 },
    { name: 'account_json', path: '/account', role: 'customer', html: false, weight: 5 },
  ],
  techlandier: [
    { name: 'account_html', path: '/account', role: 'customer', html: true, weight: 20 },
    { name: 'account_orders_html', path: '/account/orders', role: 'customer', html: true, weight: 10 },
    { name: 'product_html', path: '/products/{productSlug}', role: 'customer', html: true, weight: 15 },
    { name: 'buy_html', path: '/buy/{productId}', role: 'customer', html: true, weight: 10 },
    { name: 'admin_html', path: '/admin', role: 'admin', html: true, weight: 10 },
    { name: 'admin_analytics_json', path: '/admin/analytics', role: 'admin', html: false, weight: 10 },
    { name: 'admin_orders_html', path: '/admin/orders', role: 'admin', html: true, weight: 10 },
    { name: 'admin_catalog_html', path: '/admin/catalog', role: 'admin', html: true, weight: 10 },
    { name: 'account_json', path: '/account', role: 'customer', html: false, weight: 5 },
  ],
}[APP];

function decodePageJson(html) {
  const match = html.match(/<script data-page="app" type="application\/json">([\s\S]*?)<\/script>/);
  if (!match) throw new Error('data-page JSON not found');
  return JSON.parse(match[1]);
}

function login(email) {
  // A k6 VU has one cookie jar; clear it so a second role's GET /login is not redirected.
  http.cookieJar().clear(BASE_URL);
  const landing = http.get(`${BASE_URL}/login`, { headers: { Accept: 'text/html', 'User-Agent': UA } });
  if (landing.status !== 200) throw new Error(`GET /login returned ${landing.status}`);
  const page = decodePageJson(landing.body);
  const xsrfCookie = landing.cookies['XSRF-TOKEN'] && landing.cookies['XSRF-TOKEN'][0];
  if (!xsrfCookie) throw new Error('XSRF-TOKEN cookie missing');
  const xsrf = decodeURIComponent(xsrfCookie.value);
  const res = http.post(
    `${BASE_URL}/login`,
    JSON.stringify({ email, password: PASSWORD, remember: true }),
    {
      redirects: 0,
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json',
        'X-Inertia': 'true',
        'X-Inertia-Version': page.version,
        'X-XSRF-TOKEN': xsrf,
        'X-Requested-With': 'XMLHttpRequest',
        'User-Agent': UA,
      },
    }
  );
  if (res.status >= 400) throw new Error(`POST /login for ${email} returned ${res.status}`);
  const cookies = Object.entries(res.cookies || {})
    .map(([name, values]) => `${name}=${values[0].value}`)
    .join('; ');
  if (!cookies) throw new Error(`Login for ${email} did not set a cookie`);
  http.cookieJar().clear(BASE_URL);
  return { cookie: cookies, version: page.version };
}

function page(path, cookie, version) {
  const res = http.get(`${BASE_URL}${path}`, {
    headers: { Accept: 'application/json', 'X-Inertia': 'true', 'X-Inertia-Version': version, Cookie: cookie, 'User-Agent': UA },
  });
  if (res.status !== 200) throw new Error(`GET ${path} returned ${res.status}`);
  return JSON.parse(res.body);
}

export function setup() {
  const accounts = ACCOUNTS[APP];
  if (!accounts || !ROUTES) throw new Error(`Unknown APP '${APP}'`);
  const admin = login(accounts.admin);
  const customer = accounts.customer === accounts.admin ? admin : login(accounts.customer);
  const ids = {};

  if (APP === 'inkwell') {
    const feed = page('/posts', admin.cookie, admin.version);
    ids.postId = feed.props.posts.data[0].id;
    const users = page('/users', admin.cookie, admin.version);
    ids.userId = users.props.users.data[0].id;
  } else if (APP === 'carparker') {
    const lots = page('/lots', customer.cookie, customer.version);
    ids.lotId = lots.props.lots.data[0].id;
    const bookings = page('/account/bookings', customer.cookie, customer.version);
    ids.bookingId = bookings.props.bookings[0].id;
  } else {
    const products = page('/products', customer.cookie, customer.version);
    ids.productSlug = products.props.products.data[0].slug;
    ids.productId = products.props.products.data[0].id;
    const orders = page('/account/orders', customer.cookie, customer.version);
    ids.orderNumber = orders.props.orders[0].number;
  }

  const routes = [];
  for (const route of ROUTES) {
    const account = route.role === 'admin' ? admin : customer;
    const path = route.path.replace(/\{(\w+)\}/g, (_, key) => encodeURIComponent(ids[key] ?? ''));
    if (!path || /\{/.test(path)) continue;
    const headers = route.html
      ? { Accept: 'text/html', Cookie: account.cookie, 'User-Agent': UA }
      : { Accept: 'application/json', 'X-Inertia': 'true', 'X-Inertia-Version': account.version, Cookie: account.cookie, 'User-Agent': UA };
    const probe = http.get(`${BASE_URL}${path}`, { headers });
    if (probe.status !== 200) continue;
    routes.push({ ...route, path, cookie: account.cookie, version: account.version });
  }
  if (routes.length === 0) throw new Error('No authenticated routes resolved');
  return { routes };
}

const routeNames = ROUTES.map((route) => route.name);
const routeTrends = Object.fromEntries(routeNames.map((name) => [name, new Trend(`route_${name}`, true)]));
const routeCounts = Object.fromEntries(routeNames.map((name) => [name, new Counter(`route_${name}_requests`)]));

export default function (data) {
  const routes = data.routes;
  const total = routes.reduce((sum, route) => sum + route.weight, 0);
  let pick = Math.random() * total;
  let route = routes[routes.length - 1];
  for (const candidate of routes) {
    pick -= candidate.weight;
    if (pick <= 0) { route = candidate; break; }
  }
  const headers = route.html
    ? { Accept: 'text/html', Cookie: route.cookie, 'User-Agent': UA }
    : { Accept: 'application/json', 'X-Inertia': 'true', 'X-Inertia-Version': route.version, Cookie: route.cookie, 'User-Agent': UA };
  const res = http.get(`${BASE_URL}${route.path}`, { headers, tags: { route: route.name, app: APP } });
  routeTrends[route.name].add(res.timings.duration);
  routeCounts[route.name].add(1);
  check(res, {
    [`${route.name} 200`]: (r) => r.status === 200,
    [`${route.name} has content`]: (r) => r.body && r.body.length > 500,
  });
}

export function handleSummary(data) {
  const out = __ENV.SUMMARY_PATH;
  const metrics = data.metrics;
  const summary = {
    app: APP,
    profile: PROFILE,
    authenticated: true,
    rpsTarget: PROFILE === 'rps' ? RPS : null,
    vus: PROFILE === 'vus' ? VUS : null,
    duration: DURATION,
    requests: metrics.http_reqs ? metrics.http_reqs.values.count : 0,
    iterations: metrics.iterations ? metrics.iterations.values.count : 0,
    droppedIterations: metrics.dropped_iterations ? metrics.dropped_iterations.values.count : 0,
    rps: metrics.http_reqs ? metrics.http_reqs.values.rate : 0,
    failed: metrics.http_req_failed ? metrics.http_req_failed.values.rate : 0,
    checks: metrics.checks ? metrics.checks.values.rate : 0,
    latencyMs: metrics.http_req_duration ? {
      avg: metrics.http_req_duration.values.avg,
      min: metrics.http_req_duration.values.min,
      p50: metrics.http_req_duration.values.med,
      p90: metrics.http_req_duration.values['p(90)'],
      p95: metrics.http_req_duration.values['p(95)'],
      p99: metrics.http_req_duration.values['p(99)'],
      max: metrics.http_req_duration.values.max,
    } : null,
    dataReceived: metrics.data_received ? metrics.data_received.values.count : 0,
    routes: Object.fromEntries(
      routeNames.map((name) => [
        name,
        {
          count: metrics[`route_${name}_requests`]?.values.count ?? null,
          p50: metrics[`route_${name}`]?.values.med ?? null,
          p95: metrics[`route_${name}`]?.values['p(95)'] ?? null,
          p99: metrics[`route_${name}`]?.values['p(99)'] ?? null,
          avg: metrics[`route_${name}`]?.values.avg ?? null,
        },
      ])
    ),
  };
  return out ? { [out]: JSON.stringify(summary, null, 2) } : {};
}
