(function (root) {
  const M = typeof module !== 'undefined' ? require('./stats.js') : root.WealthMath;
  const BAGS = -1;
  const DAY = 24 * 3600 * 1000;
  const WEEK = 7 * DAY;
  const DIVINE_NAME = 'divine orb';

  const tabName = tab => (tab === BAGS ? 'Bags' : 'Stash ' + (tab + 1));
  const isStale = (seenAt, now) => now - Date.parse(seenAt) > DAY;

  function ago(ms) {
    if (ms < 60000) return 'just now';
    if (ms < 3600000) return Math.floor(ms / 60000) + 'm ago';
    if (ms < DAY) return Math.floor(ms / 3600000) + 'h ago';
    return Math.floor(ms / DAY) + 'd ago';
  }

  function buildRows(data) {
    const merged = new Map();
    const add = (tab, seenAt, lines) => {
      for (const line of lines) {
        const key = tab + ':' + line.baseId;
        const row = merged.get(key) || { key, baseId: line.baseId, tab, seenAt, count: 0, value: 0 };
        row.count += line.count;
        row.value += line.value;
        merged.set(key, row);
      }
    };
    if (data.bag) add(BAGS, data.bag.at, data.bag.lines);
    for (const tab of data.tabs || []) add(tab.index, tab.seenAt, tab.lines);
    return [...merged.values()].map(row => {
      const unit = row.count !== 0 ? row.value / row.count : 0;
      return { ...row, unit, unpriced: M.isUnpriced({ baseId: row.baseId, unit }) };
    });
  }

  function summarise(rows, excluded) {
    const sum = { total: 0, bags: 0, stash: 0, included: 0, unpriced: 0, excluded: 0 };
    for (const row of rows) {
      if (excluded.has(row.key)) {
        sum.excluded++;
        continue;
      }
      sum.included++;
      sum.total += row.value;
      if (row.tab === BAGS) sum.bags += row.value;
      else sum.stash += row.value;
      if (row.unpriced) sum.unpriced++;
    }
    return sum;
  }

  function breakdown(rows, excluded, limit) {
    const byItem = new Map();
    for (const row of rows) if (!excluded.has(row.key)) byItem.set(row.baseId, (byItem.get(row.baseId) || 0) + row.value);
    const items = [...byItem].map(([baseId, value]) => ({ baseId, value })).sort((a, b) => b.value - a.value);
    const slices = items.slice(0, limit);
    const rest = items.slice(limit).reduce((a, i) => a + i.value, 0);
    if (items.length > limit) slices.push({ baseId: null, value: rest });
    return slices;
  }

  function findDivine(data) {
    const id = Object.keys(data.names || {}).find(key => String(data.names[key]).toLowerCase() === DIVINE_NAME);
    if (id === undefined) return 0;
    const lines = [...((data.bag && data.bag.lines) || []), ...(data.tabs || []).flatMap(tab => tab.lines)];
    const line = lines.find(l => String(l.baseId) === id && l.unit > 0);
    if (line) return line.unit;
    const history = data.priceHistory && data.priceHistory[id];
    return history && history.length > 0 ? history[history.length - 1][1] : 0;
  }

  const recent = (totals, now) => totals
    .filter(p => Date.parse(p.at) >= now - WEEK)
    .map(p => ({ t: Date.parse(p.at), v: p.total, label: 'Bags ' + Math.round(p.bags) + ' / Stash ' + Math.round(p.stash) }));

  function sortRows(rows, by, dir, nameOf) {
    const key = { name: r => nameOf(r.baseId).toLowerCase(), tab: r => r.tab, qty: r => r.count, price: r => r.unit, total: r => r.value }[by];
    return [...rows].sort((a, b) => (key(a) < key(b) ? -1 : key(a) > key(b) ? 1 : 0) * dir);
  }

  const excludedKey = profile => 'wealth.excluded.' + profile;

  const pruneExcluded = (saved, rows) => {
    const present = new Set(rows.map(r => r.key));
    return new Set((Array.isArray(saved) ? saved : []).filter(key => present.has(key)));
  };

  function filterRows(rows, tab, search, nameOf) {
    const needle = search.trim().toLowerCase();
    return rows.filter(r => (tab === null || r.tab === tab) && (!needle || nameOf(r.baseId).toLowerCase().includes(needle)));
  }

  const divines = (total, divine) => (divine > 0 ? total / divine : null);
  const unitMode = (divine, mode) => (divine > 0 && mode === 'value' ? { unit: 'Div', scale: 1 / divine } : { unit: 'FE', scale: 1 });

  function tabList(data) {
    const list = [];
    if (data.bag) list.push({ index: BAGS, seenAt: data.bag.at, empty: false, never: false });
    const seen = new Map((data.tabs || []).map(t => [t.index, t]));
    const last = Math.max(-1, ...seen.keys());
    for (let i = 0; i <= last; i++) {
      const t = seen.get(i);
      list.push(t ? { index: i, seenAt: t.seenAt, empty: t.lines.length === 0, never: false } : { index: i, seenAt: null, empty: false, never: true });
    }
    return list;
  }

  const api = { BAGS, tabName, isStale, ago, buildRows, summarise, breakdown, findDivine, recent, sortRows, excludedKey, pruneExcluded, filterRows, divines, unitMode, tabList };
  if (typeof module !== 'undefined') {
    module.exports = api;
    return;
  }

  const { useState, useMemo } = React;
  const html = htm.bind(React.createElement);
  const { LineChart, Doughnut } = root.WealthCharts;
  const SNAPSHOT_ROWS = 500;

  function Switch({ options, value, onChange }) {
    return html`<span className="switch">${options.map(([id, label]) => html`<button key=${id} className=${'btn' + (value === id ? ' on' : '')} onClick=${() => onChange(id)}>${label}</button>`)}</span>`;
  }

  function WealthScreen({ data, profile, now, ui }) {
    const { NameCell, Tile, num, stored, store } = ui;
    const nameOf = id => (data.names && data.names[id]) || '#' + id;
    const rows = useMemo(() => buildRows(data), [data]);
    const [saved, setSaved] = useState(() => stored(excludedKey(profile), []));
    const excluded = useMemo(() => pruneExcluded(saved, rows), [saved, rows]);
    const [view, setView] = useState('tabs');
    const [mode, setMode] = useState('value');
    const [tab, setTab] = useState(null);
    const [search, setSearch] = useState('');
    const [sort, setSort] = useState({ by: 'total', dir: -1 });
    const divine = useMemo(() => findDivine(data), [data]);
    const sum = useMemo(() => summarise(rows, excluded), [rows, excluded]);
    const slices = useMemo(() => breakdown(rows, excluded, 8), [rows, excluded]);
    const points = useMemo(() => recent(data.totals || [], now), [data, Math.floor(now / 60000)]);
    const shown = useMemo(() => {
      return sortRows(filterRows(rows, tab, search, nameOf), sort.by, sort.dir, nameOf);
    }, [rows, tab, search, sort, data]);
    const tabs = tabList(data);
    const staleTabs = tabs.filter(t => t.index !== BAGS && !t.never && isStale(t.seenAt, now));
    const toggle = key => {
      const next = new Set(excluded);
      if (!next.delete(key)) next.add(key);
      setSaved([...next]);
      store(excludedKey(profile), [...next]);
    };
    const head = (by, label, right) => html`<th className=${'sort' + (right ? ' r' : '')} onClick=${() => setSort({ by, dir: sort.by === by ? -sort.dir : by === 'name' || by === 'tab' ? 1 : -1 })}>${label}${sort.by === by ? (sort.dir < 0 ? ' v' : ' ^') : ''}</th>`;
    const { unit, scale } = unitMode(divine, mode);
    const sliceLabel = s => (s.baseId === null ? 'Other' : nameOf(s.baseId)) + (mode === 'percent' ? '' : ' - ' + num(s.value * scale, divine > 0 ? 2 : 0) + ' ' + unit);
    return html`
      <div>
        <div className="tiles">
          <${Tile} label="Wealth" value=${num(sum.total, 0) + ' FE'} sub=${sum.excluded > 0 ? sum.excluded + ' row(s) left out' : 'everything counted'} />
          <${Tile} label="Divines" value=${divines(sum.total, divine) !== null ? num(divines(sum.total, divine), 2) : '-'} sub=${divine > 0 ? '1 Div = ' + num(divine, 1) + ' FE' : 'no Divine Orb price, FE only'} />
          <${Tile} label="Bags" value=${num(sum.bags, 0) + ' FE'} />
          <${Tile} label="Stash" value=${num(sum.stash, 0) + ' FE'} sub=${staleTabs.length > 0 ? staleTabs.length + ' tab(s) stale' : ''} tint=${staleTabs.length > 0 ? 'bad' : ''} />
          <${Tile} label="Rows" value=${sum.included} sub="item rows counted" />
          <${Tile} label="Unpriced" value=${sum.unpriced} tint=${sum.unpriced > 0 ? 'bad' : ''} sub=${sum.unpriced > 0 ? 'valued 0 FE' : ''} />
        </div>
        <div className="cols">
          <section className="panel">
            <div className="panel-head"><h3>Wealth</h3><span className="dim">last 7 days - chart ignores exclusions</span></div>
            ${points.length === 0 ? html`<div className="empty">no snapshots in the last 7 days</div>` : points.length < 2 ? html`<div className="empty">one snapshot so far</div>` : html`<${LineChart} points=${points} color="#F9DD75" unit="FE" />`}
            ${staleTabs.length > 0 && html`<p className="warn-text">${staleTabs.length} stash tab(s) not seen for over a day, their part of the total is old.</p>`}
          </section>
          <section className="panel">
            <div className="panel-head">
              <h3>Breakdown</h3>
              <${Switch} options=${[['value', divine > 0 ? 'Divines' : 'FE'], ['percent', 'Percentage']]} value=${mode} onChange=${setMode} />
            </div>
            <${Doughnut} slices=${slices.map(s => ({ label: sliceLabel(s), value: Math.max(0, s.value) }))} />
          </section>
        </div>
        <section className="panel">
          <div className="panel-head">
            <${Switch} options=${[['tabs', 'Tabs'], ['snapshots', 'Snapshots']]} value=${view} onChange=${setView} />
            ${view === 'tabs' && html`<input className="search" type="search" placeholder="Search items" value=${search} onChange=${e => setSearch(e.target.value)} />`}
          </div>
          ${view === 'snapshots' ? html`
            ${(data.totals || []).length === 0 ? html`<div className="empty">No snapshots yet</div>` : html`
              <table className="grid">
                <thead><tr><th>Taken</th><th className="r">Bags</th><th className="r">Stash</th><th className="r">Total</th></tr></thead>
                <tbody>${[...data.totals].reverse().slice(0, SNAPSHOT_ROWS).map(p => html`
                  <tr key=${p.at}>
                    <td>${new Date(p.at).toLocaleString()}</td>
                    <td className="r">${num(p.bags, 0)}</td>
                    <td className="r">${num(p.stash, 0)}</td>
                    <td className="r">${num(p.total, 0)}</td>
                  </tr>`)}</tbody>
              </table>
              ${data.totals.length > SNAPSHOT_ROWS && html`<p className="dim">Newest ${SNAPSHOT_ROWS} of ${data.totals.length}</p>`}`}`
          : html`
            <div className="wealth-body">
              <ul className="side">
                <li className=${tab === null ? 'on' : ''} onClick=${() => setTab(null)}><b>All</b></li>
                ${tabs.map(t => {
                  if (t.never) return html`<li key=${t.index} className="never"><b>${tabName(t.index)}</b><span className="dim">never seen - open it in game</span></li>`;
                  const stale = t.index !== BAGS && isStale(t.seenAt, now);
                  return html`
                    <li key=${t.index} className=${(tab === t.index ? 'on ' : '') + (stale ? 'stale' : '')} onClick=${() => setTab(t.index)}>
                      <b>${tabName(t.index)}</b>
                      <span className="dim">synced ${ago(now - Date.parse(t.seenAt))}${t.empty ? ' - empty' : ''}</span>
                      ${stale && html`<em className="warn" title=${"Last seen " + new Date(t.seenAt).toLocaleString()}>stale - seen ${new Date(t.seenAt).toLocaleDateString()}</em>`}
                    </li>`;
                })}
              </ul>
              ${shown.length === 0 ? html`<div className="empty">Nothing here</div>` : html`
                <table className="grid">
                  <thead><tr><th></th>${head('name', 'Item')}${head('tab', 'Tab')}${head('qty', 'Qty', true)}${head('price', 'Price', true)}${head('total', 'Total', true)}</tr></thead>
                  <tbody>${shown.map(r => html`
                    <tr key=${r.key} className=${excluded.has(r.key) ? 'off' : ''}>
                      <td><input type="checkbox" checked=${!excluded.has(r.key)} onChange=${() => toggle(r.key)} title="Count in the total" /></td>
                      <${NameCell} data=${data} line=${r} />
                      <td>${tabName(r.tab)}${r.tab !== BAGS && isStale(r.seenAt, now) ? html` <em className="warn" title=${'Last seen ' + new Date(r.seenAt).toLocaleString()}>stale</em>` : ''}</td>
                      <td className="r">${r.count}</td>
                      <td className="r">${num(r.unit, 2)}</td>
                      <td className="r">${num(r.value)}</td>
                    </tr>`)}</tbody>
                </table>`}
            </div>`}
        </section>
      </div>`;
  }

  window.WealthScreen = WealthScreen;
})(typeof window !== 'undefined' ? window : globalThis);
