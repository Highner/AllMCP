window.onerror = (msg, src, line, col, err) => {
    console.error("[artworks-widget] window.onerror:", msg, "at", src, line+":"+col, err);
};
window.addEventListener("unhandledrejection", e => {
    console.error("[artworks-widget] unhandledrejection:", e.reason);
});



console.log("[artworks-widget] module boot");
console.log("[artworks-widget] finding DOM…");
console.log("[artworks-widget] container?", !!document.getElementById('chartContainer'));
console.log("[artworks-widget] emptyState?", !!document.getElementById('emptyState'));
console.log("[artworks-widget] trendChart?", !!document.getElementById('trendChart'));


const container = document.getElementById('chartContainer');
const emptyState = document.getElementById('emptyState');
const ctx = document.getElementById('trendChart');

if (!container || !emptyState || !ctx) {
    console.error("[artworks-widget] Missing DOM nodes. Check IDs in HTML: #chartContainer, #emptyState, #trendChart");
    // Show a helpful message to user instead of a blank card
    if (emptyState) {
        emptyState.hidden = false;
        emptyState.textContent = "UI error: chart container not found. Check element IDs.";
    }
    // Bail early to avoid TypeErrors later
    throw new Error("Required DOM nodes not found");
}


let chart;
let latestPayload = null;
let chartReady = false;

// --- resolve + normalize helpers (unchanged) ---
const resolveOutputPayload = (payload) => {
  if (!payload || typeof payload !== 'object') return null;
  if (payload.timeSeries || Array.isArray(payload)) return payload;
  const keys = ['toolOutput','output','detail','data','payload','result','structuredContent','structured_output','structured'];
  for (const k of keys) if (payload[k]) {
    const r = resolveOutputPayload(payload[k]); if (r) return r;
  }
  return payload;
};

const normalizePoints = (output) => {
  const raw = output && output.timeSeries;
  const arr = Array.isArray(raw) ? raw
            : (raw && typeof raw === 'object') ? Object.values(raw.$values || raw)
            : [];
  return arr.filter(p => p && typeof p === 'object');
};

const render = (output = {}) => {
  // If Chart.js still isn't ready, just stash the payload and bail; we'll re-run once ready.
  if (typeof window.Chart === 'undefined') {
    latestPayload = output;
    container.hidden = true;
    emptyState.hidden = false;
    emptyState.textContent = (output && output.description) || 'Loading chart library…';
    return;
  }

  const points = normalizePoints(output);

  if (!points.length) {
    if (chart) { chart.destroy(); chart = null; }
    container.hidden = true;
    emptyState.hidden = false;
    emptyState.textContent = output.description || 'No results available.';
    return;
  }

  const labels = points.map(p => new Date(p.Time).toLocaleDateString(undefined, { year: 'numeric', month: 'short' }));
  const values = points.map(p => (typeof p.Value === 'number' ? p.Value : null));

  container.hidden = false;
  emptyState.hidden = true;

  if (!chart) {
    chart = new window.Chart(ctx, {
      type: 'line',
      data: {
        labels,
        datasets: [{
          label: 'Position in estimate range',
          data: values,
          tension: 0.35,
          borderColor: '#2563eb',
          backgroundColor: 'rgba(37,99,235,0.2)',
          fill: true,
          pointRadius: 2,
          pointHoverRadius: 4
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          y: {
            title: { display: true, text: 'Position in estimate range' },
            suggestedMin: 0, suggestedMax: 1,
            ticks: { callback: v => Number(v).toFixed(2) }
          },
          x: { title: { display: true, text: 'Month' } }
        }
      }
    });
  } else {
    chart.data.labels = labels;
    chart.data.datasets[0].data = values;
    chart.update();
  }
};

// ---- Gate B: when Chart.js becomes ready, re-render with the latest payload ----
const onChartReady = () => {
  if (chartReady) return;
  if (typeof window.Chart !== 'undefined') {
    chartReady = true;
    if (latestPayload) render(latestPayload);
  }
};

// If <script defer> executed already, Chart will be present now; otherwise wait for load
if (typeof window.Chart !== 'undefined') {
  onChartReady();
} else {
  const s = document.getElementById('chartjs');
  if (s) s.addEventListener('load', onChartReady, { once: true });
  // Safety: periodic poll in case the load event is swallowed
  const poll = setInterval(() => {
    if (typeof window.Chart !== 'undefined') { clearInterval(poll); onChartReady(); }
  }, 100);
  setTimeout(() => clearInterval(poll), 5000);
}

// ---- Gate A: capture tool output as soon as the host provides it ----
const handlePayload = (payload) => {
  const resolved = resolveOutputPayload(payload) || {};
  latestPayload = resolved;
  render(resolved); // If Chart isn't ready yet, render() will stash and show "Loading…"
};

const attachListeners = () => {
  const openai = window.openai;
  if (!openai) return false;

  // initial payload if present
  if (openai.toolOutput) handlePayload(openai.toolOutput);

  // official subscription APIs (varies by host version)
  if (typeof openai.subscribeToToolOutput === 'function') {
    openai.subscribeToToolOutput(handlePayload);
  } else if (typeof openai.onToolOutput === 'function') {
    openai.onToolOutput(handlePayload);
  }

  return true;
};

// Try immediately; if window.openai not injected yet, wait for host events
let attached = attachListeners();

if (!attached) {
  // Fires when host injects globals (most reliable)
  window.addEventListener('openai:set_globals', (evt) => {
    // this event usually carries toolOutput on first tool completion
    const payload = evt?.detail?.toolOutput ?? window.openai?.toolOutput;
    if (!attached) attached = attachListeners();
    if (payload) handlePayload(payload);
  });

  // As a backup, poll briefly for window.openai to appear
  const t = setInterval(() => {
    if (window.openai && !attached) attached = attachListeners();
    if (attached) clearInterval(t);
  }, 150);
  setTimeout(() => clearInterval(t), 5000);
}

// Also handle explicit tool-output events some hosts emit
window.addEventListener('openai:tool-output', e => handlePayload(e?.detail));
window.addEventListener('message', e => {
  const p = e?.data;
  if (p && (p.type === 'openai-tool-output' || p.type === 'tool-output')) {
    handlePayload(p.detail ?? p.payload ?? p.data ?? p);
  }
});

// In case everything was already ready before our listeners attached:
const initial = resolveOutputPayload(window.openai?.toolOutput) || null;
if (initial) handlePayload(initial);
