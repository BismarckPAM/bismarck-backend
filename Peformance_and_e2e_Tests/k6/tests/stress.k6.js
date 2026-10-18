import {requireConfig,token,scenarioStages,latencyLimit} from '../lib/config.js';
import {requestOne} from '../lib/requests.js';
export const options = {stages:scenarioStages('stress'), thresholds:{http_req_failed:['rate<0.05'],http_req_duration:[`p(95)<${latencyLimit}`],bismarck_api_failures:['rate<0.05']}};
export function setup(){requireConfig();if(!token)throw new Error('ACCESS_TOKEN required for authenticated read-only endpoint tests. See README.');}
export default function runScenario(){requestOne();}
