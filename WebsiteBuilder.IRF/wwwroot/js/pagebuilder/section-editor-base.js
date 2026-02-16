// wwwroot/js/pagebuilder/section-editor-base.js
(function () {
    "use strict";

    const DEFAULTS = {
        debounceMs: 300,
        // Your endpoint should accept { pageId, revisionSectionId, draftRevisionRowVersionBase64, sectionRowVersionBase64, settingsJson }
        // and return { ok, newDraftRevisionRowVersionBase64, newSectionRowVersionBase64, errors? }
        saveUrl: null,

        // Required: read/write the per-section settings object
        readSettings: null,   // (rootEl) => object
        writeSettings: null,  // (rootEl, settingsObj) => void

        // Optional: custom per-editor preflight validation
        validateClient: null, // (settingsObj) => { ok: true } | { ok:false, errors:[{ field?, message }] }

        // Optional: hook points
        onSaveStart: null,    // (ctx) => void
        onSaveSuccess: null,  // (ctx, result) => void
        onSaveError: null,    // (ctx, err) => void
    };

    function debounce(fn, ms) {
        let t = null;
        return function (...args) {
            clearTimeout(t);
            t = setTimeout(() => fn.apply(this, args), ms);
        };
    }

    function getAntiForgeryToken() {
        // Matches your pattern: <form id="antiForgeryForm"> @Html.AntiForgeryToken()
        const el = document.querySelector('#antiForgeryForm input[name="__RequestVerificationToken"]');
        return el ? el.value : null;
    }

    function normalizeError(err) {
        if (!err) return { message: "Unknown error." };
        if (typeof err === "string") return { message: err };
        if (err.message) return { message: err.message, ...err };
        return { message: "Request failed.", ...err };
    }

    function parseJsonSafe(text) {
        try { return JSON.parse(text); } catch { return null; }
    }

    function getInvalidIdsFromUrl() {
        const url = new URL(window.location.href);
        const raw = url.searchParams.get("invalidSectionIds");
        if (!raw) return [];
        return raw.split(",").map(x => Number(x.trim())).filter(n => Number.isFinite(n));
    }

    function setInvalidIdsInUrl(ids) {
        const url = new URL(window.location.href);
        if (!ids || ids.length === 0) url.searchParams.delete("invalidSectionIds");
        else url.searchParams.set("invalidSectionIds", ids.join(","));
        window.history.replaceState({}, "", url.toString());
    }

    function clearInvalidSectionFromUrl(sectionId) {
        const ids = getInvalidIdsFromUrl();
        const next = ids.filter(x => x !== Number(sectionId));
        setInvalidIdsInUrl(next);
    }

    function markInvalidUi(rootEl, isInvalid) {
        // Align with your current “red border” pattern
        rootEl.classList.toggle("is-invalid-section", !!isInvalid);
    }

    function setSavingUi(rootEl, saving) {
        rootEl.classList.toggle("is-saving", !!saving);
        // Optional: show tiny status label if present
        const badge = rootEl.querySelector("[data-save-status]");
        if (badge) badge.textContent = saving ? "Saving..." : "";
    }

    function setErrorUi(rootEl, errors) {
        // Generic area the editor can provide:
        // <div class="text-danger small mt-2" data-editor-errors></div>
        const box = rootEl.querySelector("[data-editor-errors]");
        if (!box) return;

        if (!errors || errors.length === 0) {
            box.innerHTML = "";
            box.classList.add("d-none");
            return;
        }

        const html = errors
            .map(e => `<div>${escapeHtml(e.message || String(e))}</div>`)
            .join("");
        box.innerHTML = html;
        box.classList.remove("d-none");
    }

    function escapeHtml(s) {
        return String(s)
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function getSectionMeta(rootEl) {
        const sectionId = Number(rootEl.getAttribute("data-section-id"));
        const revisionSectionId = Number(rootEl.getAttribute("data-revision-section-id"));
        const sectionRowVersionBase64 = rootEl.getAttribute("data-section-rowversion") || "";
        const draftRevisionRowVersionBase64 = window.__draftRevisionRowVersionBase64 || "";

        return {
            sectionId,
            revisionSectionId,
            sectionRowVersionBase64,
            draftRevisionRowVersionBase64,
        };
    }

    async function postJson(url, payload) {
        const token = getAntiForgeryToken();
        const headers = { "Content-Type": "application/json" };
        if (token) headers["RequestVerificationToken"] = token;

        const res = await fetch(url, {
            method: "POST",
            headers,
            body: JSON.stringify(payload),
            credentials: "same-origin",
        });

        const text = await res.text();
        const body = parseJsonSafe(text);

        if (!res.ok) {
            // Try to propagate structured backend info
            const err = { status: res.status, body, raw: text };
            throw err;
        }

        return body ?? {};
    }

    class SectionEditorBase {
        constructor(rootEl, options) {
            this.rootEl = rootEl;
            this.opts = { ...DEFAULTS, ...(options || {}) };

            if (!this.opts.saveUrl) throw new Error("SectionEditorBase: saveUrl is required.");
            if (typeof this.opts.readSettings !== "function") throw new Error("SectionEditorBase: readSettings(rootEl) is required.");

            this._dirty = false;
            this._saving = false;

            // Debounced save
            this.requestSave = debounce(() => this._saveNow(), this.opts.debounceMs);

            // “Input change” binding helper is optional; most editors will call bindInputs()
        }

        bindInputs(selector = "input, textarea, select") {
            const handler = () => {
                this._dirty = true;
                this.requestSave();
            };

            this.rootEl.querySelectorAll(selector).forEach(el => {
                el.addEventListener("input", handler);
                el.addEventListener("change", handler);
            });
        }

        async _saveNow() {
            if (this._saving) return;
            if (!this._dirty) return;

            const ctx = getSectionMeta(this.rootEl);
            const settings = this.opts.readSettings(this.rootEl);

            // Optional client-side validation
            if (typeof this.opts.validateClient === "function") {
                const v = this.opts.validateClient(settings);
                if (v && v.ok === false) {
                    setErrorUi(this.rootEl, v.errors || [{ message: "Validation failed." }]);
                    markInvalidUi(this.rootEl, true);
                    return;
                }
            }

            const payload = {
                revisionSectionId: ctx.revisionSectionId,
                draftRevisionRowVersionBase64: ctx.draftRevisionRowVersionBase64,
                sectionRowVersionBase64: ctx.sectionRowVersionBase64,
                settingsJson: JSON.stringify(settings),
            };

            this._saving = true;
            setSavingUi(this.rootEl, true);
            setErrorUi(this.rootEl, []);

            try {
                if (typeof this.opts.onSaveStart === "function") this.opts.onSaveStart(ctx);

                const result = await postJson(this.opts.saveUrl, payload);

                // Expected: server returns updated tokens
                if (result?.newDraftRevisionRowVersionBase64) {
                    window.__draftRevisionRowVersionBase64 = result.newDraftRevisionRowVersionBase64;
                }
                if (result?.newSectionRowVersionBase64) {
                    this.rootEl.setAttribute("data-section-rowversion", result.newSectionRowVersionBase64);
                }

                // Save OK: remove invalid state & URL marker
                markInvalidUi(this.rootEl, false);
                clearInvalidSectionFromUrl(ctx.sectionId);

                // If editor provided a writer, allow server-normalized values to be applied
                if (typeof this.opts.writeSettings === "function" && result?.normalizedSettingsJson) {
                    const normalized = parseJsonSafe(result.normalizedSettingsJson);
                    if (normalized) this.opts.writeSettings(this.rootEl, normalized);
                }

                this._dirty = false;

                if (typeof this.opts.onSaveSuccess === "function") this.opts.onSaveSuccess(ctx, result);
            } catch (err) {
                const e = normalizeError(err);

                // 409 concurrency: draft or section rowversion mismatch
                if (e.status === 409) {
                    setErrorUi(this.rootEl, [{ message: "This section (or the draft) was modified elsewhere. Reloading to avoid conflicts…" }]);
                    markInvalidUi(this.rootEl, true);

                    // Your preferred behavior: hard reload to rehydrate tokens & section DOM
                    window.location.reload();
                    return;
                }

                // 400/422 validation: show errors; keep red border
                const backendErrors =
                    e.body?.errors ||
                    e.body?.validationErrors ||
                    (Array.isArray(e.body) ? e.body : null);

                if (backendErrors) {
                    const list = Array.isArray(backendErrors)
                        ? backendErrors.map(x => (typeof x === "string" ? { message: x } : x))
                        : [{ message: "Validation failed." }];

                    setErrorUi(this.rootEl, list);
                    markInvalidUi(this.rootEl, true);
                } else {
                    setErrorUi(this.rootEl, [{ message: e.message || "Save failed." }]);
                    markInvalidUi(this.rootEl, true);
                }

                if (typeof this.opts.onSaveError === "function") this.opts.onSaveError(ctx, e);
            } finally {
                this._saving = false;
                setSavingUi(this.rootEl, false);
            }
        }
    }

    // Expose globally (works well with Razor + script tags)
    window.PageBuilder = window.PageBuilder || {};
    window.PageBuilder.SectionEditorBase = SectionEditorBase;
})();
