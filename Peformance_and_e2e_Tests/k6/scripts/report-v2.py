#!/usr/bin/env python3
"""Offline, standard-library HTML report for k6 summary and JSONL raw metrics."""
import json,sys,html,collections,pathlib,datetime,statistics

def read_records(path):
    with open(path,encoding='utf-8') as f:
        for line in f:
            try: yield json.loads(line)
            except ValueError: continue

def main():
    if len(sys.argv)<3: raise SystemExit('Usage: python report-v2.py summary.json raw.json')
    summary_path,raw_path=map(pathlib.Path,sys.argv[1:3]); out=summary_path.with_name(summary_path.name.replace('-summary.json','-report.html'))
    summ=json.loads(summary_path.read_text(encoding='utf-8')); m=summ.get('metrics',{})
    buckets=collections.defaultdict(lambda: {'durations':[],'statuses':collections.Counter(),'timestamps':[]})
    for rec in read_records(raw_path):
        if rec.get('type')!='Point': continue
        datum=rec.get('data',{});tags=datum.get('tags') or {};endpoint=tags.get('endpoint','unknown')
        if rec.get('metric')=='http_req_duration':
            buckets[endpoint]['durations'].append(float(datum.get('value',0)))
            buckets[endpoint]['statuses'][str(tags.get('status','unknown'))]+=1
            buckets[endpoint]['timestamps'].append(datum.get('time',''))
    def pct(a,p):
        if not a:return 0
        s=sorted(a);k=(len(s)-1)*p/100;lower=int(k);higher=min(lower+1,len(s)-1)
        return s[lower]+(s[higher]-s[lower])*(k-lower)
    rows=[]
    for name,d in sorted(buckets.items()):
        values=d['durations'];failed=sum(n for code,n in d['statuses'].items() if code!='200')
        rows.append((name,len(values),statistics.mean(values),pct(values,95),pct(values,99),max(values),failed))
    overall=m.get('http_req_duration',{});fail=m.get('http_req_failed',{});requests=m.get('http_reqs',{})
    title=html.escape(summary_path.stem)
    htmlrows=''.join(f'<tr><td>{html.escape(name)}</td><td>{n}</td><td>{avg:.1f}</td><td>{p95:.1f}</td><td>{p99:.1f}</td><td>{maxi:.1f}</td><td>{bad}</td></tr>' for name,n,avg,p95,p99,maxi,bad in rows)
    limit=max((r[3] for r in rows),default=1) or 1
    bars=''.join(f'<p>{html.escape(r[0])} — p95 {r[3]:.1f}ms</p><div class="track"><div class="bar" style="width:{min(100,100*r[3]/limit):.1f}%"></div></div>' for r in rows)
    doc=f"""<!doctype html><html lang="en"><meta charset="utf-8"><title>{title}</title>
<style>body{{font:16px Arial,sans-serif;margin:40px auto;max-width:1000px;color:#17253b;padding:0 20px}}h1{{color:#172c54}}.cards{{display:flex;gap:16px;flex-wrap:wrap}}.card{{border:1px solid #ddd;border-radius:12px;padding:18px;min-width:160px}}.value{{font-size:26px;font-weight:700}}table{{border-collapse:collapse;width:100%;margin:20px 0}}th,td{{border:1px solid #dce3ed;padding:10px;text-align:left}}th{{background:#eef2f7}}.track{{height:20px;background:#e7edf6;border-radius:5px}}.bar{{height:100%;background:#4369ad;border-radius:5px}}small{{color:#52627a}}</style>
<h1>Bismarck k6 Performance — {title}</h1><p>Source: actual k6 exported measurements. Generated {datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%d %H:%M UTC')}</p>
<div class="cards"><div class="card">Requests<div class="value">{requests.get('count','—')}</div></div><div class="card">Average latency<div class="value">{overall.get('avg',0):.1f} ms</div></div><div class="card">Overall p95<div class="value">{overall.get('p(95)',0):.1f} ms</div></div><div class="card">Error rate<div class="value">{fail.get('value',0)*100:.2f}%</div></div></div>
<h2>Endpoint metrics</h2><table><thead><tr><th>Endpoint tag</th><th>Requests</th><th>Avg ms</th><th>p95 ms</th><th>p99 ms</th><th>Max ms</th><th>Non-200</th></tr></thead><tbody>{htmlrows}</tbody></table><h2>Endpoint p95 comparison</h2>{bars}
<p><small>Thresholds: http_req_failed &lt; 5%, http_req_duration p95 &lt; configured limit (default 2000ms). For V2 per-endpoint thresholds inspect terminal output. Lower average latency does not prove better scaling across different runs. Results cover only selected read-only routes and load profiles. Failures count in table is based on status != 200.</small></p></html>"""
    out.write_text(doc,encoding='utf-8');print('HTML report:',out)
    return 0
if __name__=='__main__':raise SystemExit(main())
