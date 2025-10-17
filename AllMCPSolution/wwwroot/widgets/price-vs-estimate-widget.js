/*! artworks-widget (SPA/chat-safe) */
/* Robust, no-refresh widget that renders "price vs estimate" time series using Chart.js.
   - Waits for DOM nodes (#chartContainer, #emptyState, #trendChart)
   - Auto-loads Chart.js if missing
   - Subscribes to ChatGPT host tool-output events
   - No hard failures if things arrive out of order
*/

(function () {
    // ---------------- Global error guards ----------------
    window.onerror = (msg, src, line, col, err) => {
        console.error("[artworks-widget] window.onerror:", msg, "at", src, line + ":" + col, err);
    };
    window.addEventListener("unhandledrejection", (e) => {
        console.error("[artworks-widget] unhandledrejection:", e.reason);
    });

    // --------------- Singleton guard (avoid double init in SPA/chat) ---------------
    if (window.__artworksWidgetStarted__) {
        console.info("[artworks-widget] already started; skipping.");
        return;
    }
    window.__artworksWidgetStarted__ = true;

    console.log("[artworks-widget] module boot");
    console.log("[artworks-widget] waiting for DOM nodes… (#chartContainer, #emptyState, #trendChart)");

    // ---------------- Utilities ----------------
    function waitForElements(ids, { timeout = 8000 } = {}) {
        return new Promise((resolve) => {
            const getAll = () => ids.map((id) => document.getElementById(id));
            let nodes = getAll();
            if (nodes.every(Boolean)) return resolve(nodes);

            const timer = setTimeout(() => {
                obs.disconnect();
                nodes = getAll();
                if (!nodes.every(Boolean)) {
                    console.error("[artworks-widget] Timeout waiting for DOM nodes:", ids);
                }
                resolve(nodes); // resolve anyway; caller can show error state
            }, timeout);

            const obs = new MutationObserver(() => {
                nodes = getAll();
                if (nodes.every(Boolean)) {
                    clearTimeout(timer);
                    obs.disconnect();
                    resolve(nodes);
                }
            });

            obs.observe(document.documentElement, { childList: true, subtree: true });
        });
    }

    function ensureChartJs({ src = "https://cdn.jsdelivr.net/npm/chart.js", id = "chartjs" } = {}) {
        return new Promise((resolve) => {
            if (typeof window.Chart !== "undefined") {
                console.log("[artworks-widget] Chart.js already present");
                return resolve(true);
            }
            let tag = document.getElementById(id);
            if (!tag) {
                tag = document.createElement("script");
                tag.id = id;
                tag.src = src;
                tag.defer = true;
                document.head.appendChild(tag);
            } else {
                console.log("[artworks-widget] Reusing existing Chart.js script tag");
            }
            const done = () => resolve(typeof window.Chart !== "undefined");
            tag.addEventListener("load", done, { once: true });
            tag.addEventListener("error", () => {
                console.error("[artworks-widget] Failed to load Chart.js from", tag.src);
                resolve(false);
            }, { once: true });
            // Safety poll in case load event is swallowed by host
            const poll = setInterval(() => {
                if (typeof window.Chart !== "undefined") {
                    clearInterval(poll);
                    done();
                }
            }, 100);
            setTimeout(() => clearInterval(poll), 5000);
        });
    }

    // ---------------- Core widget logic ----------------
    function startWidget(container, emptyState, ctx) {
        console.log("[artworks-widget] starting widget…");

        let chart;
        let latestPayload = null;
        let chartReady = typeof window.Chart !== "undefined";

        const resolveOutputPayload = (payload) => {
            if (!payload || typeof payload !== "object") return null;
            if (payload.timeSeries || Array.isArray(payload)) return payload;
            const keys = [
                "toolOutput", "output", "detail", "data", "payload",
                "result", "structuredContent", "structured_output", "structured"
            ];
            for (const k of keys) {
                if (payload[k]) {
                    const r = resolveOutputPayload(payload[k]);
                    if (r) return r;
                }
            }
            return payload;
        };

        const normalizePoints = (output) => {
            const raw = output && output.timeSeries;
            const arr = Array.isArray(raw)
                ? raw
                : (raw && typeof raw === "object")
                    ? Object.values(raw.$values || raw)
                    : [];
            return arr.filter((p) => p && typeof p === "object");
        };

        const render = (output = {}) => {
            // If Chart.js not ready, stash and show info
            if (typeof window.Chart === "undefined") {
                latestPayload = output;
                container.hidden = true;
                emptyState.hidden = false;
                emptyState.textContent =
                    (output && output.description) || "Loading chart library…";
                return;
            }

            const points = normalizePoints(output);

            if (!points.length) {
                if (chart) {
                    chart.destroy();
                    chart = null;
                }
                container.hidden = true;
                emptyState.hidden = false;
                emptyState.textContent =
                    output.description || "No results available.";
                return;
            }

            const labels = points.map((p) =>
                new Date(p.Time).toLocaleDateString(undefined, {
                    year: "numeric",
                    month: "short",
                })
            );
            const values = points.map((p) =>
                typeof p.Value === "number" ? p.Value : null
            );

            container.hidden = false;
            emptyState.hidden = true;

            if (!chart) {
                chart = new window.Chart(ctx, {
                    type: "line",
                    data: {
                        labels,
                        datasets: [
                            {
                                label: "Position in estimate range",
                                data: values,
                                tension: 0.35,
                                borderColor: "#2563eb",
                                backgroundColor: "rgba(37,99,235,0.2)",
                                fill: true,
                                pointRadius: 2,
                                pointHoverRadius: 4,
                            },
                        ],
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        scales: {
                            y: {
                                title: { display: true, text: "Position in estimate range" },
                                suggestedMin: 0,
                                suggestedMax: 1,
                                ticks: { callback: (v) => Number(v).toFixed(2) },
                            },
                            x: { title: { display: true, text: "Month" } },
                        },
                    },
                });
            } else {
                chart.data.labels = labels;
                chart.data.datasets[0].data = values;
                chart.update();
            }
        };

        // Gate: when Chart.js becomes available, re-render with latest payload
        const onChartReady = () => {
            if (chartReady) return;
            if (typeof window.Chart !== "undefined") {
                chartReady = true;
                if (latestPayload) render(latestPayload);
            }
        };

        if (typeof window.Chart !== "undefined") {
            onChartReady();
        } else {
            const s = document.getElementById("chartjs");
            if (s) s.addEventListener("load", onChartReady, { once: true });
            const poll = setInterval(() => {
                if (typeof window.Chart !== "undefined") {
                    clearInterval(poll);
                    onChartReady();
                }
            }, 100);
            setTimeout(() => clearInterval(poll), 5000);
        }

        // ---- Tool output handling & host wiring ----
        const handlePayload = (payload) => {
            const resolved = resolveOutputPayload(payload) || {};
            latestPayload = resolved;
            render(resolved);
        };

        const attachListeners = () => {
            const openai = window.openai;
            if (!openai) return false;

            // Initial payload if present
            if (openai.toolOutput) handlePayload(openai.toolOutput);

            // Official subscription APIs (varies by host)
            if (typeof openai.subscribeToToolOutput === "function") {
                openai.subscribeToToolOutput(handlePayload);
            } else if (typeof openai.onToolOutput === "function") {
                openai.onToolOutput(handlePayload);
            }
            return true;
        };

        let attached = attachListeners();

        if (!attached) {
            // Fires when host injects globals (reliable across host versions)
            window.addEventListener("openai:set_globals", (evt) => {
                const payload = evt?.detail?.toolOutput ?? window.openai?.toolOutput;
                if (!attached) attached = attachListeners();
                if (payload) handlePayload(payload);
            });

            // Short poll for globals as a backup
            const t = setInterval(() => {
                if (window.openai && !attached) attached = attachListeners();
                if (attached) clearInterval(t);
            }, 150);
            setTimeout(() => clearInterval(t), 5000);
        }

        // Some hosts emit these events explicitly
        window.addEventListener("openai:tool-output", (e) => handlePayload(e?.detail));
        window.addEventListener("message", (e) => {
            const p = e?.data;
            if (p && (p.type === "openai-tool-output" || p.type === "tool-output")) {
                handlePayload(p.detail ?? p.payload ?? p.data ?? p);
            }
        });

        // In case everything was already ready before our listeners attached
        const initial = (window.openai && resolveOutputPayload(window.openai.toolOutput)) || null;
        if (initial) handlePayload(initial);
    }

    // ---------------- Bootstrap flow ----------------
    (async function bootstrap() {
        // 1) Wait for required DOM nodes
        const [container, emptyState, ctx] = await waitForElements([
            "chartContainer",
            "emptyState",
            "trendChart",
        ]);

        console.log("[artworks-widget] container?", !!container);
        console.log("[artworks-widget] emptyState?", !!emptyState);
        console.log("[artworks-widget] trendChart?", !!ctx);

        if (!container || !emptyState || !ctx) {
            console.error(
                "[artworks-widget] Missing DOM nodes. Check IDs in HTML: #chartContainer, #emptyState, #trendChart"
            );
            if (emptyState) {
                emptyState.hidden = false;
                emptyState.textContent =
                    "UI error: chart container not found. Check element IDs.";
            }
            return; // graceful bail (no throw)
        }

        // 2) Ensure Chart.js is loaded (auto-load if needed)
        const chartOk = await ensureChartJs();
        if (!chartOk) {
            // We’ll still start; render() will continue to show a helpful message
            console.warn("[artworks-widget] proceeding without Chart.js (will show 'Loading chart library…')");
        }

        // 3) Start widget
        startWidget(container, emptyState, ctx);
    })();
})();
