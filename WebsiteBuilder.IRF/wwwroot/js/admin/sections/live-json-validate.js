// wwwroot/js/admin/sections/live-json-validate.js
// ------------------------------------------------
// Live JSON validation with debounce + visual feedback
// No dependencies other than fetch()

const SCHEMA_HINTS = {
    hero: {
        title: "Hero section JSON (example)",
        example: {
            heading: "Welcome to my site",
            subheading: "Short supporting message",
            ctaText: "Get Started",
            ctaHref: "/about",
            backgroundImageAssetId: null
        },
        required: ["heading"],
        notes: [
            "ctaHref should be a relative path like /about",
            "backgroundImageAssetId can be null or a valid MediaAsset Id"
        ]
    },
    text: {
        title: "Text section JSON (example)",
        example: {
            title: "Section title (optional)",
            bodyHtml: "<p>Your content here</p>"
        },
        required: ["bodyHtml"],
        notes: [
            "bodyHtml expects HTML string (sanitize on render if needed)."
        ]
    },
    gallery: {
        title: "Gallery section JSON (example)",
        example: {
            items: [
                { imageAssetId: 123, caption: "Caption (optional)" },
                { imageAssetId: 456, caption: "Another caption" }
            ]
        },
        required: ["items"],
        notes: [
            "items must be an array with at least 1 element.",
            "imageAssetId must be a valid MediaAsset Id."
        ]
    }
};


(function () {
    const DEBOUNCE_MS = 500;

    function debounce(fn, delay) {
        let timer = null;
        return function (...args) {
            clearTimeout(timer);
            timer = setTimeout(() => fn.apply(this, args), delay);
        };
    }
    function tryBeautifyJson(textarea) {
        const raw = textarea.value;
        if (!raw || raw.trim().length === 0) return false;

        // First, attempt strict parse
        try {
            const obj = JSON.parse(raw);
            const pretty = JSON.stringify(obj, null, 2);
            if (pretty !== raw.trim()) textarea.value = pretty;
            return true;
        } catch {
            // Fallback: attempt relaxed parse by stripping trailing commas
            try {
                const cleaned = stripTrailingCommas(raw);
                const obj = JSON.parse(cleaned);
                const pretty = JSON.stringify(obj, null, 2);
                textarea.value = pretty;
                return true;
            } catch {
                // Still invalid → do not modify user input
                return false;
            }
        }
    }


    function renderStatus(textarea, isValid, errorsCount) {
        const id = textarea.dataset.statusContainer;
        if (!id) return;

        const el = document.getElementById(id);
        if (!el) return;

        if (isValid === true) {
            el.textContent = "Valid JSON.";
            el.className = "small mt-2 text-success";
        } else if (isValid === false) {
            el.textContent = `Invalid JSON (${errorsCount || 0} error${(errorsCount || 0) === 1 ? "" : "s"}).`;
            el.className = "small mt-2 text-danger";
        } else {
            el.textContent = "Validation status unavailable.";
            el.className = "small mt-2 text-muted";
        }
    }

    function renderHints(textarea) {
        const id = textarea.dataset.hintsContainer;
        if (!id) return;

        const el = document.getElementById(id);
        if (!el) return;

        const typeKey = (textarea.dataset.sectionType || "").toLowerCase();
        const hint = SCHEMA_HINTS[typeKey];

        if (!hint) {
            el.innerHTML = `<div class="text-muted">No schema hints available for <strong>${typeKey}</strong>.</div>`;
            return;
        }

        const exampleJson = JSON.stringify(hint.example, null, 2)
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;");

        const required = hint.required?.length
            ? `<div class="mt-2"><strong>Required:</strong> ${hint.required.join(", ")}</div>`
            : "";

        const notes = hint.notes?.length
            ? `<ul class="mt-2 mb-0">${hint.notes.map(n => `<li>${n}</li>`).join("")}</ul>`
            : "";

        el.innerHTML = `
        <div class="border rounded p-2 bg-light">
        <div class="fw-semibold">${hint.title}</div>
        ${required}
        <div class="mt-2"><strong>Example:</strong></div>
        <pre class="mb-0"><code>${exampleJson}</code></pre>
        ${notes}
            </div>
        `;
        }


    function disableSaveInitially() {
        const btn = document.querySelector("[data-save-button]");
        if (btn) btn.disabled = true;
    }

    function setSaveEnabled(enabled) {
        const btn = document.querySelector("[data-save-button]");
        if (!btn) return;

        btn.disabled = !enabled;
    }

    function setState(textarea, state) {
        textarea.classList.remove("is-valid", "is-invalid");

        if (state === "valid") textarea.classList.add("is-valid");
        if (state === "invalid") textarea.classList.add("is-invalid");
    }

    function renderErrors(container, errors) {
        container.innerHTML = "";

        if (!errors || errors.length === 0) return;

        const ul = document.createElement("ul");
        ul.className = "text-danger small mb-0";

        errors.forEach(e => {
            const li = document.createElement("li");
            li.textContent = e;
            ul.appendChild(li);
        });

        container.appendChild(ul);
    }

    async function validateJson(textarea) {
        const validateUrl = textarea.dataset.validateUrl;
        const sectionType = textarea.dataset.sectionType;
        const errorContainerId = textarea.dataset.errorContainer;

        if (!validateUrl || !sectionType) return;

        const errorContainer = errorContainerId
            ? document.getElementById(errorContainerId)
            : null;

        const payload = {
            typeKey: sectionType,
            contentJson: textarea.value
        };

        try {
            const response = await fetch(validateUrl, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken":
                        document.querySelector('input[name="__RequestVerificationToken"]')?.value
                },
                body: JSON.stringify(payload)
            });

            if (!response.ok) throw new Error("Validation request failed.");

            const result = await response.json();

            if (result.isValid) {
                setState(textarea, "valid");
                setSaveEnabled(true);
                if (errorContainer) renderErrors(errorContainer, []);
                renderStatus(textarea, true, 0);

            } else {
                setState(textarea, "invalid");
                setSaveEnabled(false);
                if (errorContainer) renderErrors(errorContainer, result.errors);
                renderStatus(textarea, false, result.errors ? result.errors.length : 0);

            }

        } catch (err) {
            // Network or server failure → neutral state
            setState(textarea, null);
            if (errorContainer) {
                renderErrors(errorContainer, [
                    "Unable to validate JSON. Please try again."
                ]);
            }
            renderStatus(textarea, null, 0);

            console.error(err);
        }
    }
    function maskJsonStrings(input) {
        // Replaces characters inside JSON string literals with spaces so regex ops
        // won’t accidentally modify content within strings.
        // Returns { masked, map } where map lets us restore original strings.
        const map = [];
        let masked = "";
        let i = 0;

        while (i < input.length) {
            const ch = input[i];

            if (ch === '"') {
                let start = i;
                i++; // consume opening quote
                let escaped = false;

                while (i < input.length) {
                    const c = input[i];
                    if (!escaped && c === '"') {
                        i++; // consume closing quote
                        break;
                    }
                    if (!escaped && c === "\\") {
                        escaped = true;
                    } else {
                        escaped = false;
                    }
                    i++;
                }

                const str = input.slice(start, i);
                const token = `__STR_${map.length}__`;
                map.push(str);

                // Keep token length similar-ish for indexing stability; token is fine.
                masked += token;
            } else {
                masked += ch;
                i++;
            }
        }

        return { masked, map };
    }

    function unmaskJsonStrings(input, map) {
        let output = input;
        for (let idx = 0; idx < map.length; idx++) {
            output = output.replace(`__STR_${idx}__`, map[idx]);
        }
        return output;
    }

    function stripTrailingCommas(jsonText) {
        // Remove trailing commas before } or ] (outside strings).
        // Example: { "a": 1, } -> { "a": 1 }
        //          [1,2,]      -> [1,2]
        const { masked, map } = maskJsonStrings(jsonText);

        // Remove comma followed by optional whitespace/newlines then a closing bracket/brace
        const cleanedMasked = masked.replace(/,\s*([}\]])/g, "$1");

        return unmaskJsonStrings(cleanedMasked, map);
    }

    function wire() {
        const textareas = document.querySelectorAll("textarea[data-live-json-validate]");

        if (textareas.length > 0) {
            disableSaveInitially();
        }

        textareas.forEach(textarea => {

            renderHints(textarea);

            const handler = debounce(() => validateJson(textarea), DEBOUNCE_MS);
            textarea.addEventListener("input", handler);

            textarea.addEventListener("blur", () => {
                const beautified = tryBeautifyJson(textarea);
                if (beautified) {
                    // Immediately re-validate so UI reflects formatted JSON
                    validateJson(textarea);
                }
            });

            // OPTIONAL but recommended:
            // validate immediately if textarea already has content
            if (textarea.value && textarea.value.trim().length > 0) {
                validateJson(textarea);
            }
        });
    }

})();
