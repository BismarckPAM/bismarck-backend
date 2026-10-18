import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';
import { baseUrl, authorization } from './config.js';
export const apiLatency = new Trend('bismarck_api_latency_ms', true);
export const apiFailures = new Rate('bismarck_api_failures');
export const apiRequests = new Counter('bismarck_api_requests');
// Read-only APIs only. No access grants, approval decisions, user creation, SSH sessions, or database writes.
const routes = [
  {name:'resource_list', path:'/api/resources'},
  {name:'approval_list', path:'/api/approval/requests'},
  {name:'analytics_summary', path:'/api/analytics/summary'},
  {name:'notification_list', path:'/api/notifications'},
];
export function requestOne() {
  const i = (__ITER + __VU) % routes.length;
  const route = routes[i];
  const response = http.get(`${baseUrl}${route.path}`, {headers: authorization, tags:{endpoint:route.name}, timeout:'15s'});
  const valid = check(response, { 'API returned HTTP 200':r => r.status === 200 });
  apiLatency.add(response.timings.duration, {endpoint:route.name});
  apiFailures.add(!valid, {endpoint:route.name});
  apiRequests.add(1, {endpoint:route.name});
  sleep(1);
}
export function healthCheck() {
  const response = http.get(`${baseUrl}/health`, {tags:{endpoint:'gateway_health'},timeout:'15s'});
  check(response, {'gateway health HTTP 200':r=>r.status===200});
  sleep(1);
}
