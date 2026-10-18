import http from 'k6/http';
import {check, sleep} from 'k6';
import {Rate, Trend, Counter} from 'k6/metrics';
import {baseUrl, authorization, token, requireConfig, latencyLimit} from './config.js';

// All endpoint calls are read-only. Avoid testing mutation/admin actions on live deployments.
export const ENDPOINTS = Object.freeze({
  resources: {path:'/api/resources', tag:'resource_list'},
  approvals: {path:'/api/approval/requests', tag:'approval_list'},
  analytics: {path:'/api/analytics/summary', tag:'analytics_summary'},
  notifications: {path:'/api/notifications', tag:'notification_list'},
});
const failures = new Rate('v2_endpoint_failures');
const latency = new Trend('v2_endpoint_latency_ms', true);
const calls = new Counter('v2_endpoint_requests');
const numeric = (value, fallback) => Number.isFinite(Number(value)) && Number(value)>0 ? Number(value) : fallback;
// Hard cap of 10 VUs on the shared live assignment environment.
export const vus = Math.min(10, Math.max(1, Math.floor(numeric(__ENV.MAX_VUS, 5))));
export const thinkSeconds = Math.min(5, numeric(__ENV.THINK_TIME_SECONDS, 1));
export const focus = __ENV.ENDPOINT || 'resources';
export const selected = focus === 'all' ? Object.keys(ENDPOINTS) : [focus];
export const profile = __ENV.V2_PROFILE || 'baseline';
export function validate() {
  requireConfig();
  if (!token) throw new Error('ACCESS_TOKEN is required. Keep it private.');
  if (!['all',...Object.keys(ENDPOINTS)].includes(focus)) throw new Error('Invalid ENDPOINT: '+focus);
  if (!['baseline','ramp','burst','soak'].includes(profile)) throw new Error('Invalid V2_PROFILE: '+profile);
}
export function workload() {
  if (profile==='baseline') return [{duration:'10s',target:1},{duration:'30s',target:vus},{duration:'10s',target:0}];
  if (profile==='ramp') return [{duration:'10s',target:1},{duration:'20s',target:Math.max(1,Math.ceil(vus/2))},{duration:'20s',target:vus},{duration:'10s',target:0}];
  if (profile==='burst') return [{duration:'10s',target:1},{duration:'5s',target:vus},{duration:'20s',target:vus},{duration:'10s',target:0}];
  return [{duration:'15s',target:vus},{duration:'2m',target:vus},{duration:'15s',target:0}];
}
export function thresholds() {
  const checks = {http_req_failed:['rate<0.05'], http_req_duration:[`p(95)<${latencyLimit}`], v2_endpoint_failures:['rate<0.05']};
  // Sub-metric thresholds only for requests actually included in this run.
  selected.forEach(k => {checks[`http_req_duration{endpoint:${ENDPOINTS[k].tag}}`] = [`p(95)<${latencyLimit}`];});
  return checks;
}
export function execute() {
  const key=selected[(__ITER+__VU-1)%selected.length];
  const item=ENDPOINTS[key];
  const r=http.get(`${baseUrl}${item.path}`,{headers:authorization,tags:{endpoint:item.tag},timeout:'15s'});
  const ok=check(r,{'selected API returns HTTP 200':res=>res.status===200});
  failures.add(!ok,{endpoint:item.tag});
  latency.add(r.timings.duration,{endpoint:item.tag});
  calls.add(1,{endpoint:item.tag});
  sleep(thinkSeconds);
}
