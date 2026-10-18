import { requireConfig } from '../lib/config.js';
import { healthCheck } from '../lib/requests.js';
export const options = {vus:1, iterations:3, thresholds:{http_req_failed:['rate<0.05'],http_req_duration:['p(95)<2000']}};
export function setup(){requireConfig();}
export default function(){healthCheck();}
