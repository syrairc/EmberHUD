(function (root) {
  const FE = 100300;
  const KEPT = 0.875;
  const time = s => Date.parse(s);
  const seconds = run => (time(run.end) - time(run.start)) / 1000;

  const isUnpriced = line => line.unit === 0 && line.baseId !== FE;

  function lineValue(line, eco) {
    return eco.tradeTax && line.baseId !== FE ? line.value * KEPT : line.value;
  }

  function runValue(run, eco) {
    let value = 0;
    for (const line of run.loot) value += lineValue(line, eco);
    if (eco.mapCosts) for (const line of run.costs) value -= Math.abs(line.value);
    return value;
  }

  function summarise(runs, eco, nowMs) {
    if (runs.length === 0) return { netWorth: 0, fePerHour: 0, fePerMap: 0, runs: 0, averageRunSec: 0, totalSec: 0 };
    let net = 0;
    let active = 0;
    let first = Infinity;
    for (const run of runs) {
      net += runValue(run, eco);
      active += seconds(run);
      first = Math.min(first, time(run.start));
    }
    const total = eco.wallClock ? Math.max(0, (nowMs - first) / 1000) : active;
    return {
      netWorth: net,
      fePerHour: total > 0 ? net / (total / 3600) : 0,
      fePerMap: net / runs.length,
      runs: runs.length,
      averageRunSec: active / runs.length,
      totalSec: total,
    };
  }

  function cumulative(runs, eco) {
    let sum = 0;
    return runs.map(run => {
      sum += runValue(run, eco);
      return { t: time(run.end), v: sum, label: run.areaName };
    });
  }

  function rolling(runs, eco, size) {
    return runs.map((run, i) => {
      const window = runs.slice(Math.max(0, i - size + 1), i + 1);
      const span = eco.wallClock ? (time(run.end) - time(window[0].start)) / 1000 : window.reduce((a, r) => a + seconds(r), 0);
      const net = window.reduce((a, r) => a + runValue(r, eco), 0);
      return { t: time(run.end), v: span > 0 ? net / (span / 3600) : 0, label: run.areaName };
    });
  }

  function diffLines(before, after) {
    const counts = new Map();
    const units = new Map();
    for (const line of before) {
      counts.set(line.baseId, (counts.get(line.baseId) || 0) - line.count);
      units.set(line.baseId, line.unit);
    }
    for (const line of after) {
      counts.set(line.baseId, (counts.get(line.baseId) || 0) + line.count);
      units.set(line.baseId, line.unit);
    }
    const lines = [];
    for (const [baseId, count] of counts) {
      if (count === 0) continue;
      const unit = baseId === FE ? 1 : units.get(baseId) || 0;
      lines.push({ baseId, count, unit, value: count * unit });
    }
    return lines;
  }

  function lootReport(runs, eco, nowMs) {
    const items = new Map();
    let gross = 0;
    let costs = 0;
    let costUnpriced = 0;
    for (const run of runs) {
      for (const line of run.loot) {
        const value = lineValue(line, eco);
        gross += value;
        const item = items.get(line.baseId) || { baseId: line.baseId, count: 0, value: 0 };
        item.count += line.count;
        item.value += value;
        if (isUnpriced(line)) item.unpriced = true;
        items.set(line.baseId, item);
      }
      for (const line of run.costs) if (isUnpriced(line)) costUnpriced++;
      if (eco.mapCosts) for (const line of run.costs) costs += Math.abs(line.value);
    }
    const summary = summarise(runs, eco, nowMs);
    const rows = [...items.values()].sort((a, b) => b.value - a.value).map(item => ({ ...item, share: gross > 0 ? item.value / gross : 0 }));
    return { gross, costs, profit: gross - costs, perHour: summary.fePerHour, perMap: summary.fePerMap, rows, unpriced: rows.filter(r => r.unpriced).length, costUnpriced };
  }

  const api = { FE, isUnpriced, lineValue, runValue, summarise, cumulative, rolling, diffLines, lootReport, seconds };
  if (typeof module !== 'undefined') module.exports = api;
  else root.WealthMath = api;
})(typeof window !== 'undefined' ? window : globalThis);
