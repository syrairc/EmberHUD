(function () {
  const { useState, useEffect, useMemo, useRef } = React;
  const html = htm.bind(React.createElement);
  const M = window.WealthMath;
  const { LineChart, Sparkline, Doughnut } = window.WealthCharts;

  const POLL_MS = 5000;
  const STALE_MS = 12 * 60 * 1000;
  const ECO_DEFAULT = { tradeTax: true, mapCosts: true, wallClock: false, hideLoot: false };

  function stored(name, fallback) {
    try {
      const raw = localStorage.getItem(name);
      return raw === null ? fallback : JSON.parse(raw);
    } catch (e) {
      return fallback;
    }
  }

  function store(name, value) {
    try {
      localStorage.setItem(name, JSON.stringify(value));
    } catch (e) {
      // storage can be blocked, the page still works
    }
  }

  const num = (n, digits = 1) => Number(n).toLocaleString(undefined, { minimumFractionDigits: digits, maximumFractionDigits: digits });
  const signed = n => (n > 0 ? '+' : '') + num(n);
  const tone = n => (n < 0 ? 'bad' : n > 0 ? 'good' : '');
  const clock = sec => {
    sec = Math.max(0, Math.round(sec));
    const h = Math.floor(sec / 3600);
    const m = Math.floor((sec % 3600) / 60);
    const s = sec % 60;
    return h > 0 ? `${h}h ${String(m).padStart(2, '0')}m` : `${m}m ${String(s).padStart(2, '0')}s`;
  };
  const asOf = at => {
    const date = new Date(at);
    return Date.now() - date.getTime() > 24 * 3600 * 1000 ? date.toLocaleString() : date.toLocaleTimeString();
  };
  const nameOf = (data, id) => (data.names && data.names[id]) || '#' + id;

  function iconSrc(data, id) {
    const dir = window.WEALTH_ICON_DIR;
    const icon = data.icons && data.icons[id];
    if (!dir || !icon) return null;
    const path = (window.WEALTH_ICON_INDEX || {})[icon] || 'Items/' + icon + '.png';
    const parts = dir.replace(/\\/g, '/').split('/').map((part, i) => (i === 0 && /^[A-Za-z]:$/.test(part) ? part : encodeURIComponent(part)));
    return 'file:///' + parts.join('/') + '/' + path.split('/').map(encodeURIComponent).join('/');
  }

  function Icon({ data, id }) {
    const [failed, setFailed] = useState(false);
    const src = iconSrc(data, id);
    return src && !failed ? html`<img className="icon" src=${src} onError=${() => setFailed(true)} />` : html`<span className="icon blank"></span>`;
  }

  function NameCell({ data, line }) {
    return html`<td className="name"><${Icon} data=${data} id=${line.baseId} />${nameOf(data, line.baseId)}${M.isUnpriced(line) && html`<em className="warn" title="No price for this item, valued 0">no price</em>`}</td>`;
  }

  class Boundary extends React.Component {
    constructor(props) {
      super(props);
      this.state = { error: null };
    }

    static getDerivedStateFromError(error) {
      return { error };
    }

    render() {
      return this.state.error ? html`<div className="banner">The page hit an error: ${String((this.state.error && this.state.error.message) || this.state.error)}</div>` : this.props.children;
    }
  }

  function useProfile(key) {
    const [data, setData] = useState(null);
    const [index, setIndex] = useState(window.WEALTH_INDEX || []);
    const last = useRef('');
    useEffect(() => {
      last.current = '';
      setData(null);
      let stop = false;
      const load = src => new Promise(resolve => {
        const script = document.createElement('script');
        script.onload = script.onerror = () => {
          script.remove();
          resolve();
        };
        script.src = src + '?t=' + Date.now();
        document.head.appendChild(script);
      });
      const tick = async () => {
        await load('wealth-index.js');
        if (stop) return;
        setIndex(window.WEALTH_INDEX || []);
        if (!key) return;
        await load('wealth-' + encodeURIComponent(key) + '.js');
        const next = window.WEALTH && window.WEALTH[key];
        if (stop || !next) return;
        const signature = JSON.stringify(next);
        if (signature !== last.current) {
          last.current = signature;
          setData(next);
        }
      };
      tick();
      const timer = setInterval(tick, POLL_MS);
      return () => {
        stop = true;
        clearInterval(timer);
      };
    }, [key]);
    return [data, index];
  }

  function useNow() {
    const [now, setNow] = useState(Date.now());
    useEffect(() => {
      const timer = setInterval(() => setNow(Date.now()), 1000);
      return () => clearInterval(timer);
    }, []);
    return now;
  }

  function Modal({ title, onClose, children, wide }) {
    return html`
      <div className="backdrop" onClick=${onClose}>
        <div className=${'modal' + (wide ? ' wide' : '')} onClick=${e => e.stopPropagation()}>
          <div className="modal-head"><h3>${title}</h3><button className="btn" onClick=${onClose}>Close</button></div>
          <div className="modal-body">${children}</div>
        </div>
      </div>`;
  }

  function Toggle({ label, value, onChange }) {
    return html`<label className="toggle"><input type="checkbox" checked=${value} onChange=${e => onChange(e.target.checked)} />${label}</label>`;
  }

  function Tile({ label, value, sub, tint }) {
    return html`<div className="tile"><span className="tile-label">${label}</span><b className=${tint || ''}>${value}</b>${sub && html`<span className="tile-sub">${sub}</span>`}</div>`;
  }

  function LinesTable({ data, lines, eco }) {
    if (lines.length === 0) return html`<div className="empty">None</div>`;
    return html`
      <table className="grid">
        <thead><tr><th>Item</th><th className="r">Qty</th><th className="r">Unit</th><th className="r">Value</th></tr></thead>
        <tbody>${lines.map(l => html`
          <tr key=${l.baseId}>
            <${NameCell} data=${data} line=${l} />
            <td className="r">${l.count}</td>
            <td className="r">${num(l.unit, 2)}</td>
            <td className=${'r ' + tone(M.lineValue(l, eco))}>${num(M.lineValue(l, eco))}</td>
          </tr>`)}</tbody>
      </table>`;
  }

  function RunDetails({ data, run, eco, onClose }) {
    const loot = [...run.loot].sort((a, b) => M.lineValue(b, eco) - M.lineValue(a, eco));
    return html`
      <${Modal} title=${run.areaName + ' - ' + clock(M.seconds(run))} onClose=${onClose}>
        <p className="dim">${new Date(run.start).toLocaleString()}${run.partial ? ' - partial run (started inside the map)' : ''}${run.unpriced ? ' - some items had no price' : ''}</p>
        <h4>Loot</h4>
        <${LinesTable} data=${data} lines=${loot} eco=${eco} />
        <h4>Map costs</h4>
        ${run.costs.some(M.isUnpriced) && html`<p className="warn-text">A consumed item has no price, so the map cost is understated.</p>`}
        <${LinesTable} data=${data} lines=${run.costs} eco=${eco} />
        <p>Run value <b className=${tone(M.runValue(run, eco))}>${num(M.runValue(run, eco))} FE</b></p>
      <//>`;
  }

  function csv(report, data) {
    const rows = [['Item', 'Quantity', 'Value (FE)', 'Share %']];
    for (const r of report.rows) rows.push([nameOf(data, r.baseId), r.count, r.value.toFixed(2), (r.share * 100).toFixed(2)]);
    const cell = c => {
      const text = typeof c === 'string' && /^[=+\-@]/.test(c) ? "'" + c : String(c);
      return '"' + text.replace(/"/g, '""') + '"';
    };
    return '﻿' + rows.map(r => r.map(cell).join(',')).join('\r\n');
  }

  function download(name, text) {
    const url = URL.createObjectURL(new Blob([text], { type: 'text/csv' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  function LootReport({ data, runs, eco, now, onClose }) {
    const report = useMemo(() => M.lootReport(runs, eco, now), [runs, eco, now]);
    const top = report.rows.filter(r => r.value > 0);
    const slices = top.slice(0, 10).map(r => ({ label: nameOf(data, r.baseId), value: r.value }));
    const rest = top.slice(10).reduce((a, r) => a + r.value, 0);
    if (rest > 0) slices.push({ label: 'Other', value: rest });
    return html`
      <${Modal} title="Loot Report" onClose=${onClose} wide=${true}>
        <div className="tiles small">
          <${Tile} label="Gross" value=${num(report.gross)} />
          <${Tile} label="Map costs" value=${num(-report.costs)} tint=${report.costs > 0 ? 'bad' : ''} />
          <${Tile} label="Profit" value=${num(report.profit)} tint=${tone(report.profit)} />
          <${Tile} label="Per hour" value=${num(report.perHour)} />
          <${Tile} label="Per map" value=${num(report.perMap)} />
          <${Tile} label="Unpriced" value=${report.unpriced} tint=${report.unpriced > 0 ? 'bad' : ''} />
        </div>
        ${report.costUnpriced > 0 && html`<p className="warn-text">A consumed item has no price in ${report.costUnpriced} map cost line(s), so map costs are understated.</p>`}
        <div className="report-body">
          <${Doughnut} slices=${slices} />
          <table className="grid">
            <thead><tr><th>Item</th><th className="r">Qty</th><th className="r">Value</th><th className="r">Share</th></tr></thead>
            <tbody>${report.rows.map(r => html`
              <tr key=${r.baseId}>
                <${NameCell} data=${data} line=${{ baseId: r.baseId, unit: r.unpriced ? 0 : 1 }} />
                <td className="r">${r.count}</td>
                <td className=${'r ' + tone(r.value)}>${num(r.value)}</td>
                <td className="r">${(r.share * 100).toFixed(1)}%</td>
              </tr>`)}</tbody>
          </table>
        </div>
        <button className="btn" onClick=${() => download('loot-report.csv', csv(report, data))}>Export CSV</button>
      <//>`;
  }

  function CurrentRun({ data, eco, setEco, now, last, stale }) {
    const open = data.open;
    const bag = data.bag;
    const current = data.live || bag;
    const live = open && current && bag && !bag.inTown;
    const lines = live ? M.diffLines(open.lines, current.lines).sort((a, b) => M.lineValue(b, eco) - M.lineValue(a, eco)) : [];
    const value = lines.reduce((a, l) => a + M.lineValue(l, eco), 0);
    return html`
      <section className="panel">
        <div className="panel-head">
          <h3>Current Run</h3>
          <${Toggle} label="Hide pickups" value=${eco.hideLoot} onChange=${v => setEco({ ...eco, hideLoot: v })} />
        </div>
        ${live ? html`
          <div className="run-line"><b>${open.areaName}</b><span className="dim">${stale ? 'Last seen ' + new Date(current.at).toLocaleString() : clock((now - Date.parse(open.at)) / 1000)}</span><b className=${tone(value)}>${signed(value)} FE</b></div>
          ${!eco.hideLoot && html`<table className="grid"><tbody>${lines.map(l => html`
            <tr key=${l.baseId}>
              <${NameCell} data=${data} line=${l} />
              <td className="r">${l.count > 0 ? '+' : ''}${l.count}</td>
              <td className=${'r ' + tone(M.lineValue(l, eco))}>${num(M.lineValue(l, eco))}</td>
            </tr>`)}</tbody></table>`}`
          : html`<div className="empty">Not in a map</div>`}
        ${last && html`
          <div className="prev">
            <span className="dim">Previous run</span>
            <div className="run-line"><b>${last.areaName}</b><span className="dim">${clock(M.seconds(last))}</span><b className=${tone(M.runValue(last, eco))}>${signed(M.runValue(last, eco))} FE</b></div>
          </div>`}
      </section>`;
  }

  function RecentRuns({ runs, eco, onDetails, onReport }) {
    const shown = runs.slice(-100).reverse();
    return html`
      <section className="panel">
        <div className="panel-head"><h3>Recent Runs</h3><button className="btn" onClick=${onReport}>Loot Report</button></div>
        ${shown.length === 0 ? html`<div className="empty">No runs yet</div>` : html`
          <ul className="runs">${shown.map(run => {
            const value = M.runValue(run, eco);
            return html`
              <li key=${run.start} className=${run.partial ? 'partial' : ''} onClick=${() => onDetails(run)}>
                <span className="zone">${run.areaName}${run.partial ? html` <em>partial</em>` : ''}${run.unpriced ? html` <em className="warn" title="Some items had no price">unpriced</em>` : ''}</span>
                <span className="dim">${clock(M.seconds(run))}</span>
                <b className=${tone(value)}>${signed(value)}</b>
              </li>`;
          })}</ul>`}
      </section>`;
  }

  function Inventory({ data }) {
    const [sort, setSort] = useState({ by: 'value', dir: -1 });
    const lines = useMemo(() => {
      const key = sort.by === 'qty' ? 'count' : 'value';
      return [...((data.bag && data.bag.lines) || [])].sort((a, b) => (a[key] - b[key]) * sort.dir);
    }, [data, sort]);
    const head = (by, label) => html`<th className="r sort" onClick=${() => setSort({ by, dir: sort.by === by ? -sort.dir : -1 })}>${label}${sort.by === by ? (sort.dir < 0 ? ' v' : ' ^') : ''}</th>`;
    return html`
      <section className="panel">
        <div className="panel-head"><h3>Inventory</h3><span className="dim">${data.bag ? (data.bag.unpriced > 0 ? data.bag.unpriced + ' unpriced - ' : '') + 'as of ' + asOf(data.bag.at) : ''}</span></div>
        ${lines.length === 0 ? html`<div className="empty">Bag is empty or not read yet</div>` : html`
          <table className="grid">
            <thead><tr><th>Item</th>${head('qty', 'Qty')}<th className="r">Unit</th>${head('value', 'Total')}<th className="r">Trend</th></tr></thead>
            <tbody>${lines.map(l => html`
              <tr key=${l.baseId}>
                <${NameCell} data=${data} line=${l} />
                <td className="r">${l.count}</td>
                <td className="r">${num(l.unit, 2)}</td>
                <td className="r">${num(l.value)}</td>
                <td className="r"><${Sparkline} pairs=${data.priceHistory && data.priceHistory[l.baseId]} /></td>
              </tr>`)}</tbody>
          </table>`}
      </section>`;
  }

  function RunsScreen({ data, eco, setEco, now, stale }) {
    const [details, setDetails] = useState(null);
    const [report, setReport] = useState(false);
    const runs = data.runs || [];
    const stash = (data.tabs || []).reduce((a, tab) => a + tab.lines.reduce((b, l) => b + l.value, 0), 0);
    const summary = useMemo(() => M.summarise(runs, eco, now), [runs, eco, eco.wallClock ? now : 0]);
    const cumulative = useMemo(() => M.cumulative(runs, eco), [runs, eco]);
    const rolling = useMemo(() => M.rolling(runs, eco, 10), [runs, eco]);
    return html`
      <div>
        <div className="tiles">
          <${Tile} label="Net Worth" value=${data.bag ? num(data.bag.total + stash, 0) + ' FE' : '-'} sub=${'bags ' + (data.bag ? num(data.bag.total, 0) : '0') + ' + stash ' + num(stash, 0)} />
          <${Tile} label="FE / Hour" value=${num(summary.fePerHour)} tint=${tone(summary.fePerHour)} />
          <${Tile} label="FE / Map" value=${num(summary.fePerMap)} tint=${tone(summary.fePerMap)} />
          <${Tile} label="Runs" value=${summary.runs} />
          <${Tile} label="Avg Run Time" value=${clock(summary.averageRunSec)} />
          <${Tile} label="Total Time" value=${clock(summary.totalSec)} sub=${eco.wallClock ? 'wall clock' : 'in map'} />
        </div>
        <div className="cols">
          <section className="panel"><div className="panel-head"><h3>Cumulative Value</h3></div><${LineChart} points=${cumulative} color="#F9DD75" unit="FE" /></section>
          <section className="panel"><div className="panel-head"><h3>FE / Hour</h3><span className="dim">last 10 runs</span></div><${LineChart} points=${rolling} color="#63E0F2" unit="FE/h" /></section>
        </div>
        <div className="cols">
          <${CurrentRun} data=${data} eco=${eco} setEco=${setEco} now=${now} last=${runs[runs.length - 1]} stale=${stale} />
          <${RecentRuns} runs=${runs} eco=${eco} onDetails=${setDetails} onReport=${() => setReport(true)} />
        </div>
        <${Inventory} data=${data} />
        ${details && html`<${RunDetails} data=${data} run=${details} eco=${eco} onClose=${() => setDetails(null)} />`}
        ${report && html`<${LootReport} data=${data} runs=${runs} eco=${eco} now=${now} onClose=${() => setReport(false)} />`}
      </div>`;
  }

  function Options({ eco, setEco, onClose }) {
    return html`
      <${Modal} title="Economy" onClose=${onClose}>
        <div className="options">
          <${Toggle} label="Trade tax (12.5% off everything but FE)" value=${eco.tradeTax} onChange=${v => setEco({ ...eco, tradeTax: v })} />
          <${Toggle} label="Subtract map costs (compasses, beacons)" value=${eco.mapCosts} onChange=${v => setEco({ ...eco, mapCosts: v })} />
          <${Toggle} label="Wall-clock time (town counts) instead of in-map time" value=${eco.wallClock} onChange=${v => setEco({ ...eco, wallClock: v })} />
          <${Toggle} label="Hide loot pickups on Current Run" value=${eco.hideLoot} onChange=${v => setEco({ ...eco, hideLoot: v })} />
        </div>
      <//>`;
  }

  function App() {
    const [key, setKey] = useState(() => stored('wealth.key', ''));
    const [screen, setScreen] = useState(() => stored('wealth.screen', 'runs'));
    const [eco, setEcoState] = useState(() => ({ ...ECO_DEFAULT, ...stored('wealth.eco', {}) }));
    const [gear, setGear] = useState(false);
    const now = useNow();
    const [data, index] = useProfile(key);
    useEffect(() => {
      if (index.length > 0 && !index.some(p => p.key === key)) setKey(index[0].key);
    }, [index, key]);
    const setEco = value => {
      setEcoState(value);
      store('wealth.eco', value);
    };
    const pick = (setter, name) => value => {
      setter(value);
      store(name, value);
    };
    const seen = data && data.bag ? Math.max(Date.parse(data.bag.at), data.live ? Date.parse(data.live.at) : 0) : 0;
    const stale = seen > 0 && now - seen > STALE_MS;
    const Wealth = window.WealthScreen;
    return html`
      <div className="app">
        <header>
          <h1>Ember Wealth</h1>
          <nav>
            ${['runs', 'wealth'].map(s => html`<button key=${s} className=${screen === s ? 'on' : ''} onClick=${() => pick(setScreen, 'wealth.screen')(s)}>${s === 'runs' ? 'Runs' : 'Wealth'}</button>`)}
          </nav>
          <select value=${key} onChange=${e => pick(setKey, 'wealth.key')(e.target.value)}>
            ${index.map(p => html`<option key=${p.key} value=${p.key}>${p.character} - season ${p.season}</option>`)}
          </select>
          <button className="btn" onClick=${() => setGear(true)}>Options</button>
        </header>
        ${stale && html`<div className="banner">No new snapshot for ${clock((now - seen) / 1000)} - tracking is paused or the game is closed.</div>`}
        ${index.length === 0 && html`<div className="empty big">No data yet. Start the game with the Prices plugin on and it appears here.</div>`}
        ${index.length > 0 && !data && html`<div className="empty big">Loading...</div>`}
        ${data && screen === 'runs' && html`<${RunsScreen} data=${data} eco=${eco} setEco=${setEco} now=${now} stale=${stale} />`}
        ${data && screen === 'wealth' && (Wealth ? html`<${Wealth} key=${key} data=${data} profile=${key} now=${now} ui=${{ NameCell, Tile, num, stored, store }} />` : html`<div className="empty big">Wealth screen is not installed yet.</div>`)}
        ${gear && html`<${Options} eco=${eco} setEco=${setEco} onClose=${() => setGear(false)} />`}
      </div>`;
  }

  ReactDOM.createRoot(document.getElementById('root')).render(html`<${Boundary}><${App} /><//>`);
})();
