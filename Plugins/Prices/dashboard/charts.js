(function () {
  const { useState } = React;
  const html = htm.bind(React.createElement);
  let ids = 0;

  const W = 640;
  const H = 190;
  const PAD = { l: 8, r: 8, t: 14, b: 22 };

  const short = n => Math.abs(n) >= 1000 ? (n / 1000).toFixed(1) + 'k' : n.toFixed(0);

  function LineChart({ points, color, unit, format }) {
    const [id] = useState(() => 'g' + ++ids);
    const [hover, setHover] = useState(null);
    if (points.length < 2) return html`<div className="empty">Not enough runs yet</div>`;
    const t0 = points[0].t;
    const t1 = points[points.length - 1].t;
    const lo = Math.min(0, ...points.map(p => p.v));
    const hi = Math.max(0, ...points.map(p => p.v));
    const span = hi - lo || 1;
    const x = p => PAD.l + ((p.t - t0) / (t1 - t0 || 1)) * (W - PAD.l - PAD.r);
    const y = p => PAD.t + (1 - (p.v - lo) / span) * (H - PAD.t - PAD.b);
    const line = points.map(p => x(p).toFixed(1) + ',' + y(p).toFixed(1)).join(' ');
    const zero = PAD.t + (1 - (0 - lo) / span) * (H - PAD.t - PAD.b);
    const area = `${PAD.l},${zero} ${line} ${x(points[points.length - 1]).toFixed(1)},${zero}`;
    const move = event => {
      const box = event.currentTarget.getBoundingClientRect();
      const px = ((event.clientX - box.left) / box.width) * W;
      let best = 0;
      for (let i = 1; i < points.length; i++) if (Math.abs(x(points[i]) - px) < Math.abs(x(points[best]) - px)) best = i;
      setHover(best);
    };
    const p = hover === null ? null : points[hover];
    return html`
      <div className="chart">
        <svg viewBox=${'0 0 ' + W + ' ' + H} onPointerMove=${move} onPointerLeave=${() => setHover(null)}>
          <defs>
            <linearGradient id=${id} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor=${color} stopOpacity="0.35" />
              <stop offset="100%" stopColor=${color} stopOpacity="0" />
            </linearGradient>
          </defs>
          <line x1=${PAD.l} x2=${W - PAD.r} y1=${zero} y2=${zero} className="axis" />
          <polygon points=${area} fill=${'url(#' + id + ')'} />
          <polyline points=${line} fill="none" stroke=${color} strokeWidth="2" strokeLinejoin="round" />
          <text x=${PAD.l} y="10" className="tick">${short(hi)}</text>
          ${lo < 0 && html`<text x=${PAD.l} y=${H - PAD.b - 3} className="tick">${short(lo)}</text>`}
          <text x=${PAD.l} y=${H - 6} className="tick">${new Date(t0).toLocaleDateString()}</text>
          <text x=${W - PAD.r} y=${H - 6} className="tick" textAnchor="end">${new Date(t1).toLocaleDateString()}</text>
          ${p && html`
            <line x1=${x(p)} x2=${x(p)} y1=${PAD.t} y2=${H - PAD.b} className="cursor" />
            <circle cx=${x(p)} cy=${y(p)} r="4" fill=${color} />`}
        </svg>
        ${p && html`
          <div className="tip" style=${{ left: Math.min(Math.max((x(p) / W) * 100, 12), 88) + '%' }}>
            <b>${format ? format(p.v) : short(p.v)}${unit ? ' ' + unit : ''}</b>
            <span>${new Date(p.t).toLocaleString()}</span>
            <span>${p.label}</span>
          </div>`}
      </div>`;
  }

  function Sparkline({ pairs }) {
    if (!pairs || pairs.length < 2) return html`<span className="dim">-</span>`;
    const values = pairs.map(pair => pair[1]);
    const lo = Math.min(...values);
    const hi = Math.max(...values);
    const points = values.map((v, i) => (i / (values.length - 1)) * 60 + ',' + (18 - ((v - lo) / (hi - lo || 1)) * 16 - 1)).join(' ');
    const up = values[values.length - 1] >= values[0];
    return html`<svg className="spark" viewBox="0 0 60 18"><polyline points=${points} fill="none" stroke=${up ? 'var(--good)' : 'var(--bad)'} strokeWidth="1.5" /></svg>`;
  }

  const COLORS = ['#F9DD75', '#63E0F2', '#8ED969', '#EF681E', '#B48CF2', '#E0463C', '#4F8CD8', '#D9A25B', '#5FB0B8', '#C86FA8', '#6D7480'];

  function Doughnut({ slices }) {
    const total = slices.reduce((a, s) => a + s.value, 0);
    if (total <= 0) return html`<div className="empty">Nothing to show</div>`;
    let at = 25;
    return html`
      <div className="doughnut">
        <svg viewBox="0 0 42 42" width="150" height="150">
          <circle cx="21" cy="21" r="15.9155" fill="none" stroke="var(--row)" strokeWidth="6" />
          ${slices.map((s, i) => {
            const part = (s.value / total) * 100;
            const node = html`<circle key=${i} cx="21" cy="21" r="15.9155" fill="none" stroke=${COLORS[i % COLORS.length]} strokeWidth="6" strokeDasharray=${part + ' ' + (100 - part)} strokeDashoffset=${at} />`;
            at -= part;
            return node;
          })}
        </svg>
        <ul>${slices.map((s, i) => html`<li key=${i}><i style=${{ background: COLORS[i % COLORS.length] }}></i>${s.label}<b>${((s.value / total) * 100).toFixed(1)}%</b></li>`)}</ul>
      </div>`;
  }

  window.WealthCharts = { LineChart, Sparkline, Doughnut };
})();
