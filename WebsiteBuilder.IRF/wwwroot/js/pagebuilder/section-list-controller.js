(function () {
    window.PageBuilder = window.PageBuilder || {};

    window.PageBuilder.initSectionList = function initSectionList(opts) {
        if (!opts) throw new Error("initSectionList: opts required.");

        const pageId = Number(opts.pageId);
        if (!Number.isFinite(pageId) || pageId <= 0) throw new Error("initSectionList: invalid pageId.");

        // keep one token variable, always base64 string
        let draftRevisionRowVersionBase64 = String(opts.draftRevisionRowVersionBase64 || "");

        let invalidSectionIds = Array.isArray(opts.invalidSectionIds) ? opts.invalidSectionIds.slice() : [];

        const sectionsListEl = document.getElementById("sectionsList");
        // If no list yet, nothing to bind.
        if (!sectionsListEl) return;

        // ----------------------------
        // Anti-forgery
        // ----------------------------
        function getAntiForgeryToken() {
            const el = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]');
            return el ? el.value : null;
        }

        async function postJson(url, body) {
            const token = getAntiForgeryToken();

            const res = await fetch(url, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    ...(token ? { "RequestVerificationToken": token } : {})
                },
                body: JSON.stringify(body)
            });

            let json = null;
            try { json = await res.json(); } catch { /* ignore */ }

            if (res.status === 409) {
                const msg = json?.error || json?.message || "This draft was changed by someone else.";
                alert(msg + " Reloading…");
                window.location.reload();
                return null;
            }

            if (!res.ok || json?.ok === false) {
                const msg = json?.error || json?.message || ("Request failed: " + res.status);
                // Helpful debug
                console.error("Request failed:", { url, status: res.status, body, response: json });
                throw new Error(msg);
            }

            // token refresh: accept either field name
            const newToken =
                json?.draftRevisionRowVersionBase64 ||
                json?.draftRevisionRowVersion ||
                null;

            if (newToken) draftRevisionRowVersionBase64 = String(newToken);

            return json;
        }

        // ----------------------------
        // Invalid highlighting (optional)
        // ----------------------------
        function markInvalidSections() {
            if (!invalidSectionIds.length) return;
            invalidSectionIds.forEach(id => {
                const el = document.getElementById("section-" + id);
                if (el) el.classList.add("border", "border-danger");
            });
        }

        markInvalidSections();

        // ----------------------------
        // Robust order reading
        // ----------------------------
        function readRevisionSectionId(el) {
            // Prefer dataset (handles both data-revision-section-id and data-revisionSectionId)
            const ds = el.dataset || {};
            const raw =
                ds.revisionSectionId ||
                ds.revisionsectionid ||
                el.getAttribute("data-revision-section-id") ||
                el.getAttribute("data-revisionSectionId") ||
                "";

            const n = Number(raw);
            return Number.isFinite(n) && n > 0 ? n : null;
        }

        function currentOrder(list) {
            const items = Array.from(list.querySelectorAll(".section-item"));
            const ids = items
                .map(readRevisionSectionId)
                .filter(n => Number.isFinite(n) && n > 0);

            // Remove duplicates defensively
            const unique = Array.from(new Set(ids));

            return unique;
        }

        async function persistOrder(list) {
            const orderedRevisionSectionIds = currentOrder(list);

            if (!orderedRevisionSectionIds.length) {
                // If this happens, your DOM doesn't match the selector or dataset
                console.error("Reorder aborted: could not compute order from DOM.");
                throw new Error("Reorder failed: section IDs not found in DOM.");
            }

            const url = `/Admin/Pages/Sections/${pageId}?handler=ReorderRevisionSections`;

            // IMPORTANT: send token with the base64 name
            return await postJson(url, {
                pageId,
                orderedRevisionSectionIds,

                // send multiple aliases to satisfy server DTO naming
                draftRevisionRowVersionBase64: draftRevisionRowVersionBase64,
                draftRevisionRowVersion: draftRevisionRowVersionBase64,
                draftRevisionRowVersionToken: draftRevisionRowVersionBase64
            });

        }

        // ----------------------------
        // SortableJS binding
        // ----------------------------
        let sortableInstance = null;

        function bindSortable(list) {
            // You are using SortableJS (window.Sortable), not jquery-sortablejs
            if (!window.Sortable) {
                console.warn("SortableJS not found (window.Sortable). Reorder disabled.");
                return;
            }

            if (sortableInstance && typeof sortableInstance.destroy === "function") {
                try { sortableInstance.destroy(); } catch { /* ignore */ }
                sortableInstance = null;
            }

            sortableInstance = new Sortable(list, {
                animation: 150,
                handle: ".section-drag-handle",
                draggable: ".section-item",
                onEnd: async function () {
                    try {
                        await persistOrder(list);
                    } catch (err) {
                        alert(err?.message || "Reorder failed.");
                        window.location.reload();
                    }
                }
            });
        }

        bindSortable(sectionsListEl);

        // expose for debugging if needed
        window.PageBuilder.__sectionList = {
            getToken: () => draftRevisionRowVersionBase64,
            getOrder: () => currentOrder(sectionsListEl)
        };
    };
})();
