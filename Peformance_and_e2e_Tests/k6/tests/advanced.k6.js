import {validate, execute, workload, thresholds} from '../lib/advanced.js';
export const options={stages:workload(),thresholds:thresholds(),tags:{suite:'bismarck-v2'}};
export function setup(){validate();}
export default function(){execute();}
