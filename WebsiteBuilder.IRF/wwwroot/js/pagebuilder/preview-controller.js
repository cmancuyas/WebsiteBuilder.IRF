(function () {
    "use strict";

    function cacheBust(url) {
        const u = new URL(url, window.location.origin);
        u.searchParams.set("v", String(Date.now()));
        return u.toString();
    }

    function initPreview(opts) {
        const frame = document.getElementById(opts.iframeId || "previewFrame");
        if (!frame) return;

        const statusEl = document.getElementById(opts.statusId || "previewStatus");
        const refreshBtn = document.getElementById(opts.refreshBtnId || "previewRefreshBtn");

        let pending = false;
        let lastRefreshAt = 0;

        function setStatus(text) {
            if (statusEl) statusEl.textContent = text || "";
        }

        function hardRefresh() {
            if (!frame.src) return;
            pending = true;
            setStatus("Refreshing…");
            frame.src = cacheBust(frame.src);
            lastRefreshAt = Date.now();
        }

        // Debounce refreshes so rapid autosaves don’t spam reloads
        let refreshTimer = null;
        function scheduleRefresh() {
            // throttle: max 1 refresh per 500ms
            const now = Date.now();
            const wait = Math.max(0, 500 - (now - lastRefreshAt));
            clearTimeout(refreshTimer);
            refreshTimer = setTimeout(() => hardRefresh(), wait);
        }

        // Manual refresh
        if (refreshBtn) refreshBtn.addEventListener("click", hardRefresh);

        // On autosave success
        window.addEventListener("pagebuilder:sectionSaved", () => {
            scheduleRefresh();
        });

        // Clear “Refreshing…” when iframe loads
        frame.addEventListener("load", () => {
            if (pending) {
                pending = false;
                setStatus("Up to date");
                setTimeout(() => setStatus(""), 1500);
            }
        });
    }

    window.PageBuilder = window.PageBuilder || {};
    window.PageBuilder.initPreview = initPreview;
})();
