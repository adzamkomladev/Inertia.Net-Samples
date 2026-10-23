// Read-only load profile for the Inkwell, CarParker and TechLandier samples.
//
// It mixes full HTML visits (server-rendered where the sample enables SSR) with Inertia JSON
// navigations, using routes and ids parsed from the app's own first response. No writes and no
// authentication, so runs are repeatable and never grow the database.
//
// Environment:
//   BASE_URL    target, e.g. http://127.0.0.1:5100
//   APP         inkwell | carparker | techlandier
//   PROFILE     rps (default) | vus | ramp
//   RPS         iterations/second for the rps profile (default 100)
//   VUS         virtual users for the vus/ramp profiles (default 16)
//   DURATION    measured duration (default 60s)
//   SUMMARY_PATH optional JSON output path

import http from 'k6/http';
import { check } from 'k6';
import { Counter, Trend } from 'k6/metrics';

const BASE_URL = (__ENV.BASE_URL || 'http://127.0.0.1:5100').replace(/\/$/, '');
const APP = (__ENV.APP || 'inkwell').toLowerCase();
const PROFILE = (__ENV.PROFILE || 'rps').toLowerCase();
const RPS = Number(__ENV.RPS || 100);
const VUS = Number(__ENV.VUS || 16);
const DURATION = __ENV.DURATION || '60s';

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
  : PROFILE === 'ramp'
    ? {
        load: {
          executor: 'ramping-vus',
          startVUs: 0,
          stages: [
            { duration: '15s', target: VUS },
            { duration: DURATION, target: VUS },
            { duration: '10s', target: 0 },
          ],
          gracefulRampDown: '5s',
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

const routeNames = {
  inkwell: ['feed_html', 'feed_page2_html', 'post_html', 'feed_json', 'post_json', 'feed_partial'],
  carparker: ['home_html', 'lots_html', 'lot_html', 'lots_json', 'lots_partial'],
  techlandier: ['home_html', 'products_html', 'product_html', 'home_json', 'products_json', 'products_partial'],
}[APP] || [];
const routeTrends = Object.fromEntries(routeNames.map((name) => [name, new Trend(`route_${name}`, true)]));
const routeCounts = Object.fromEntries(routeNames.map((name) => [name, new Counter(`route_${name}_requests`)]));

const ROUTES = {
  inkwell: [
    { name: 'feed_html', path: '/posts', weight: 25, html: true },
    { name: 'feed_page2_html', path: '/posts?page=2', weight: 10, html: true },
    { name: 'post_html', path: null, weight: 30, html: true },
    { name: 'feed_json', path: '/posts', weight: 15, html: false },
    { name: 'post_json', path: null, weight: 20, html: false },
    { name: 'feed_partial', path: '/posts', weight: 20, html: false, partial: ['posts'] },
  ],
  carparker: [
    { name: 'home_html', path: '/', weight: 20, html: true },
    { name: 'lots_html', path: '/lots', weight: 35, html: true },
    { name: 'lot_html', path: null, weight: 30, html: true },
    { name: 'lots_json', path: '/lots', weight: 15, html: false },
    { name: 'lots_partial', path: '/lots', weight: 20, html: false, partial: ['lots'] },
  ],
  techlandier: [
    { name: 'home_html', path: '/', weight: 25, html: true },
    { name: 'products_html', path: '/products', weight: 25, html: true },
    { name: 'product_html', path: null, weight: 30, html: true },
    { name: 'home_json', path: '/', weight: 10, html: false },
    { name: 'products_json', path: '/products', weight: 10, html: false },
    { name: 'products_partial', path: '/products', weight: 20, html: false, partial: ['products'] },
  ],
};

function decodePageJson(html) {
  const match = html.match(/<script data-page="app" type="application\/json">([\s\S]*?)<\/script>/);
  if (!match) throw new Error('data-page JSON not found');
  return JSON.parse(match[1]);
}

function get(path, headers) {
  return http.get(`${BASE_URL}${path}`, { headers, tags: { app: APP } });
}

export function setup() {
  const allRoutes = ROUTES[APP];
  if (!allRoutes) throw new Error(`Unknown APP '${APP}'`);
  const only = (__ENV.ROUTES || '').split(',').map((x) => x.trim()).filter(Boolean);
  const routes = only.length ? allRoutes.filter((route) => only.includes(route.name)) : allRoutes;
  if (routes.length === 0) throw new Error(`No routes match ROUTES='${__ENV.ROUTES}'`);

  const landingPath = APP === 'inkwell' ? '/posts' : APP === 'carparker' ? '/lots' : '/';
  const landing = get(landingPath, { Accept: 'text/html' });
  if (landing.status !== 200) throw new Error(`${landingPath} returned ${landing.status}`);
  const page = decodePageJson(landing.body);
  const version = page.version || '';

  // The detail id/slug comes from the app's list page; the storefront home only carries rails.
  const listPath = APP === 'techlandier' ? '/products' : landingPath;
  const list = listPath === landingPath ? page : decodePageJson(get(listPath, { Accept: 'text/html' }).body);
  const props = list.props || {};

  let detailPath = null;
  if (APP === 'inkwell') {
    const posts = props.posts && props.posts.data;
    if (posts && posts.length) detailPath = `/posts/${posts[0].id}`;
  } else if (APP === 'carparker') {
    const lots = props.lots && props.lots.data;
    if (lots && lots.length) detailPath = `/lots/${lots[0].id}`;
  } else {
    const products = props.products && props.products.data;
    if (products && products.length) detailPath = `/products/${products[0].slug}`;
  }
  if (!detailPath) throw new Error('could not derive a detail route from the landing page');

  const resolved = routes.map((route) => ({ ...route, path: route.path || detailPath }));
  // Fail fast on a broken route before the measured run.
  for (const route of resolved) {
    const res = get(route.path, route.html ? { Accept: 'text/html' } : inertiaHeaders(version, page.component, route.partial));
    if (res.status !== 200) throw new Error(`${route.name} ${route.path} returned ${res.status}`);
  }
  return { version, component: page.component, routes: resolved };
}

function inertiaHeaders(version, component, partial) {
  const headers = {
    Accept: 'application/json',
    'X-Requested-With': 'XMLHttpRequest',
    'X-Inertia': 'true',
    'X-Inertia-Version': version,
  };
  if (partial) {
    headers['X-Inertia-Partial-Component'] = component;
    headers['X-Inertia-Partial-Data'] = partial.join(',');
  }
  return headers;
}

export default function (data) {
  const total = data.routes.reduce((sum, route) => sum + route.weight, 0);
  let pick = Math.random() * total;
  let route = data.routes[data.routes.length - 1];
  for (const candidate of data.routes) {
    pick -= candidate.weight;
    if (pick <= 0) {
      route = candidate;
      break;
    }
  }

  const headers = route.html ? { Accept: 'text/html' } : inertiaHeaders(data.version, data.component, route.partial);
  const res = http.get(`${BASE_URL}${route.path}`, {
    headers,
    tags: { route: route.name, app: APP },
  });

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
    rpsTarget: PROFILE === 'rps' ? RPS : null,
    vus: PROFILE === 'vus' || PROFILE === 'ramp' ? VUS : null,
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
      Object.keys(routeTrends).map((name) => [
        name,
        {
          count: metrics[`route_${name}_requests`]?.values.count ?? null,
          p50: metrics[`route_${name}`]?.values.med ?? null,
          p95: metrics[`route_${name}`]?.values['p(95)'] ?? null,
          p99: metrics[`route_${name}`]?.values['p(99)'] ?? null,
          avg: metrics[`route_${name}`]?.values.avg ?? null,
        },
      ]),
    ),
  };
  return out ? { [out]: JSON.stringify(summary, null, 2) } : {};
}
