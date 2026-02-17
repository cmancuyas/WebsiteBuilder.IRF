(function () {
    window.PageBuilder = window.PageBuilder || {};

    window.PageBuilder.initSectionList = function initSectionList(opts) {
        if (!opts) throw new Error("initSectionList: opts required.");

        const pageId = Number(opts.pageId);
        if (!Number.isFinite(pageId) || pageId <= 0) throw new Error("initSectionList: invalid pageId.");

        // keep one token variable, always base64 string
        let draftRevisionRowVersionBase64 = String(opts.draftRevisionRowVersionBase64 || "");

        let invalidSectionIds = Array.isArray(opts.invalidSectionIds) ? opts.invalidSectionIds.slice() : [];

        // Host exists even when no sections yet
        const sectionsHostEl = document.getElementById("sectionsHost") || document.body;

        // List might not exist if SectionCount == 0
        let sectionsListEl = document.getElementById("sectionsList");

        // ----------------------------
        // Publish validation refresh hook (optional)
        // ----------------------------
        function notifyValidationChanged() {
            try {
                const fn = window.PageBuilder?.refreshPublishValidity;
                if (typeof fn === "function") {
                    fn({ scrollIfInvalid: false });
                }
            } catch (e) {
                console.warn("refreshPublishValidity failed:", e);
            }
        }

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

            // Always read the response once
            const text = await res.text();

            // Try parse JSON, but keep raw text too
            let json = null;
            try { json = text ? JSON.parse(text) : null; }
            catch { json = { raw: text }; }

            // Handle optimistic concurrency cleanly
            if (res.status === 409) {
                const msg = json?.error || json?.message || "This draft was changed by someone else.";
                const newToken =
                    json?.draftRevisionRowVersionBase64 ||
                    json?.draftRevisionRowVersion ||
                    null;

                // Update token if server sent one (so reload isn't strictly required)
                if (newToken) draftRevisionRowVersionBase64 = String(newToken);

                alert(msg + " Reloading…");
                window.location.reload();
                return null;
            }

            // Non-OK or ok=false => show server detail
            if (!res.ok || json?.ok === false) {
                const msg =
                    json?.error ||
                    json?.message ||
                    ("Request failed: " + res.status);

                const detail = json?.detail || json?.raw || null;

                console.error("Request failed:", {
                    url,
                    status: res.status,
                    requestBody: body,
                    response: json,
                    detail
                });

                throw new Error(detail ? `${msg}\n\n${detail}` : msg);
            }

            // Token refresh: accept either field name
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

            return Array.from(new Set(ids));
        }

        async function persistOrder(list) {
            const orderedRevisionSectionIds = currentOrder(list);

            if (!orderedRevisionSectionIds.length) {
                console.error("Reorder aborted: could not compute order from DOM.");
                throw new Error("Reorder failed: section IDs not found in DOM.");
            }

            const url = `/Admin/Pages/Sections/${pageId}?handler=ReorderRevisionSections`;

            const result = await postJson(url, {
                pageId,
                orderedRevisionSectionIds,
                draftRevisionRowVersionBase64: draftRevisionRowVersionBase64
            });

            // ✅ after reorder: refresh publish validity (UX)
            notifyValidationChanged();

            return result;
        }

        // ----------------------------
        // SortableJS binding
        // ----------------------------
        let sortableInstance = null;

        function bindSortable(list) {
            if (!list) return;

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
                    }
                }
            });
        }

        // Bind sortable if list already exists
        if (sectionsListEl) bindSortable(sectionsListEl);

        // ----------------------------
        // ADD SECTION wiring (modal)
        // ----------------------------
        const addBtn = document.getElementById("confirmAddSectionBtn");
        const addTypeEl = document.getElementById("addSectionType");
        const insertAtTopEl = document.getElementById("insertAtTop");
        const insertAfterEl = document.getElementById("insertAfter");
        const noSectionsAlert = document.getElementById("noSectionsAlert");

        function ensureSectionsListExists() {
            sectionsListEl = document.getElementById("sectionsList");
            if (sectionsListEl) return sectionsListEl;

            // Remove "No sections yet" alert if present
            if (noSectionsAlert && noSectionsAlert.parentElement) {
                noSectionsAlert.parentElement.removeChild(noSectionsAlert);
            }

            // Create container identical to server-rendered
            const div = document.createElement("div");
            div.id = "sectionsList";
            div.className = "d-grid gap-2";

            sectionsHostEl.appendChild(div);
            sectionsListEl = div;

            // Bind sortable now that list exists
            bindSortable(sectionsListEl);

            return sectionsListEl;
        }

        function wrapRowHtml(revisionSectionId, innerHtml) {
            // Must match Edit.cshtml wrapper so highlighting + anchors work
            const wrapper = document.createElement("div");
            wrapper.className = "card section-card";
            wrapper.id = "section-" + revisionSectionId;
            wrapper.setAttribute("data-section-id", String(revisionSectionId));

            const body = document.createElement("div");
            body.className = "card-body p-0";
            body.innerHTML = innerHtml;

            wrapper.appendChild(body);
            return wrapper;
        }

        function insertOptionIntoInsertAfter(revisionSectionId, title) {
            if (!insertAfterEl) return;

            const opt = document.createElement("option");
            opt.value = String(revisionSectionId);
            opt.textContent = title || ("Section " + revisionSectionId);
            insertAfterEl.appendChild(opt);
        }

        async function addSection() {
            if (!addTypeEl) throw new Error("Add Section: #addSectionType not found.");
            const typeId = Number(addTypeEl.value || 0);
            if (!typeId) {
                alert("Please select a Section Type.");
                return;
            }

            const insertAtTop = !!insertAtTopEl?.checked;

            let insertAfterRevisionSectionId = null;
            if (!insertAtTop && insertAfterEl) {
                const raw = insertAfterEl.value;
                const n = raw ? Number(raw) : null;
                insertAfterRevisionSectionId = (Number.isFinite(n) && n > 0) ? n : null;
            }

            const url = `${window.location.pathname}?handler=AddRevisionSection`;

            const result = await postJson(url, {
                SectionTypeId: typeId,
                InsertAfterRevisionSectionId: insertAfterRevisionSectionId,
                InsertAtTop: insertAtTop
            });

            if (!result) return;

            const revisionSectionId = Number(result.revisionSectionId || 0);
            const title = result.title || "Section";
            const html = result.html || "";

            if (!revisionSectionId || !html) {
                throw new Error("Add Section failed: server did not return expected html/id.");
            }

            const list = ensureSectionsListExists();
            const wrapper = wrapRowHtml(revisionSectionId, html);

            // Decide insertion point in DOM
            if (insertAtTop) {
                list.insertBefore(wrapper, list.firstChild);
            } else if (insertAfterRevisionSectionId) {
                const afterEl = document.getElementById("section-" + insertAfterRevisionSectionId);
                if (afterEl && afterEl.parentElement === list) {
                    afterEl.insertAdjacentElement("afterend", wrapper);
                } else {
                    // fallback to bottom
                    list.appendChild(wrapper);
                }
            } else {
                // bottom
                list.appendChild(wrapper);
            }

            // Update the "Insert after" dropdown so the new section can be targeted next time
            insertOptionIntoInsertAfter(revisionSectionId, title);

            // Clear initial red border marking (we will re-validate and re-highlight properly)
            invalidSectionIds = [];

            // Refresh publish validity + highlights (UX)
            notifyValidationChanged();

            // Close modal (Bootstrap 5)
            const modalEl = document.getElementById("addSectionModal");
            if (modalEl && window.bootstrap?.Modal) {
                const inst = window.bootstrap.Modal.getInstance(modalEl) || new window.bootstrap.Modal(modalEl);
                inst.hide();
            }
        }

        if (addBtn) {
            addBtn.addEventListener("click", async function () {
                try {
                    addBtn.disabled = true;
                    await addSection();
                } catch (err) {
                    alert(err?.message || "Add Section failed.");
                } finally {
                    addBtn.disabled = false;
                }
            });
        }

        // expose for debugging if needed
        window.PageBuilder.__sectionList = {
            getToken: () => draftRevisionRowVersionBase64,
            getOrder: () => sectionsListEl ? currentOrder(sectionsListEl) : [],
            refreshValidity: () => notifyValidationChanged()
        };
    };
})();
