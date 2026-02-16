(function () {
    "use strict";

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
            body: JSON.stringify(body),
            credentials: "same-origin"
        });

        let json = null;
        try { json = await res.json(); } catch { }

        if (res.status === 409) {
            alert(json?.message || "This draft was modified elsewhere. Reloading…");
            window.location.reload();
            return null;
        }

        if (!res.ok || json?.ok === false) {
            throw new Error(json?.message || "Request failed.");
        }

        if (json?.newDraftRevisionRowVersionBase64) {
            window.__draftRevisionRowVersionBase64 = json.newDraftRevisionRowVersionBase64;
        }

        return json;
    }

    function parseInvalidIdsFromQuery() {
        const params = new URLSearchParams(window.location.search);
        const csv = params.get("invalidSectionIds");
        if (!csv) return [];

        return csv
            .split(",")
            .map(x => Number(x.trim()))
            .filter(n => Number.isFinite(n) && n > 0);
    }

    function highlightInvalid(ids, scrollFirst = false) {
        if (!ids?.length) return;

        ids.forEach(id => {
            const row = document.getElementById("section-" + id);
            if (row) row.classList.add("border", "border-danger");
        });

        if (scrollFirst) {
            const first = document.getElementById("section-" + ids[0]);
            if (first) first.scrollIntoView({ behavior: "smooth", block: "center" });
        }
    }

    function bindSortable(list, pageId) {
        if (!window.Sortable) return;

        new Sortable(list, {
            animation: 150,
            handle: ".section-drag-handle",
            draggable: ".section-item",
            onEnd: async function () {

                const orderedRevisionSectionIds = Array.from(
                    list.querySelectorAll('.section-item[data-revision-section-id]')
                )
                    .map(el => Number(el.getAttribute("data-revision-section-id")))
                    .filter(n => Number.isFinite(n) && n > 0);

                try {
                    await postJson(`/Admin/Pages/Sections/${pageId}?handler=ReorderRevisionSections`, {
                        pageId,
                        orderedRevisionSectionIds,
                        draftRevisionRowVersionBase64: window.__draftRevisionRowVersionBase64
                    });
                }
                catch (err) {
                    alert(err.message || "Reorder failed.");
                    window.location.reload();
                }
            }
        });
    }

    function bindDelete(list, pageId) {
        list.addEventListener("click", async (e) => {
            const btn = e.target.closest("[data-delete-revision-section]");
            if (!btn) return;

            const revisionSectionId = Number(btn.getAttribute("data-delete-revision-section"));
            if (!revisionSectionId) return;

            if (!confirm("Delete this section?")) return;

            btn.disabled = true;

            try {
                await postJson(`/Admin/Pages/Sections/${pageId}?handler=DeleteRevisionSection`, {
                    revisionSectionId,
                    draftRevisionRowVersionBase64: window.__draftRevisionRowVersionBase64
                });

                const row = document.getElementById("section-" + revisionSectionId);
                if (row) row.remove();
            }
            catch (err) {
                alert(err.message || "Delete failed.");
                btn.disabled = false;
            }
        });
    }

    function bindAddSection(pageId) {
        const btn = document.getElementById("confirmAddSectionBtn");
        if (!btn) return;

        btn.addEventListener("click", async () => {

            const sectionTypeId = Number(document.getElementById("addSectionType")?.value || 0);
            const insertAtTop = !!document.getElementById("insertAtTop")?.checked;
            const afterRaw = document.getElementById("insertAfter")?.value || "";
            const insertAfterRevisionSectionId = afterRaw ? Number(afterRaw) : null;

            if (!sectionTypeId) {
                alert("Select a section type.");
                return;
            }

            btn.disabled = true;

            try {
                const res = await postJson(`/Admin/Pages/Sections/${pageId}?handler=AddRevisionSection`, {
                    pageId,
                    sectionTypeId,
                    insertAtTop,
                    insertAfterRevisionSectionId,
                    draftRevisionRowVersionBase64: window.__draftRevisionRowVersionBase64
                });

                if (!res?.html) {
                    window.location.reload();
                    return;
                }

                const list = document.getElementById("sectionsList");
                if (!list) {
                    window.location.reload();
                    return;
                }

                if (insertAtTop) {
                    list.insertAdjacentHTML("afterbegin", res.html);
                }
                else if (insertAfterRevisionSectionId) {
                    const afterRow = document.getElementById("section-" + insertAfterRevisionSectionId);
                    if (afterRow)
                        afterRow.insertAdjacentHTML("afterend", res.html);
                    else
                        list.insertAdjacentHTML("beforeend", res.html);
                }
                else {
                    list.insertAdjacentHTML("beforeend", res.html);
                }

                if (window.PageBuilder?.initHeroEditors) {
                    window.PageBuilder.initHeroEditors();
                }

                bootstrap.Modal.getInstance(document.getElementById("addSectionModal"))?.hide();

                btn.disabled = false;
            }
            catch (err) {
                alert(err.message || "Add failed.");
                btn.disabled = false;
            }
        });
    }

    function init(options) {

        if (!options?.pageId)
            throw new Error("SectionListController requires pageId.");

        window.__draftRevisionRowVersionBase64 =
            options.draftRevisionRowVersionBase64 || "";

        const invalidIds =
            options.invalidSectionIds?.length
                ? options.invalidSectionIds
                : parseInvalidIdsFromQuery();

        highlightInvalid(invalidIds, true);

        const list = document.getElementById("sectionsList");
        if (list) {
            bindSortable(list, options.pageId);
            bindDelete(list, options.pageId);
        }

        bindAddSection(options.pageId);
    }

    window.PageBuilder = window.PageBuilder || {};
    window.PageBuilder.initSectionList = init;

})();
