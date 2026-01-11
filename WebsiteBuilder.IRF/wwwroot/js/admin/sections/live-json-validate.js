// wwwroot/js/admin/sections/live-json-validate.js
// ------------------------------------------------
// Live JSON validation with debounce + visual feedback
// No dependencies other than fetch()

(function () {
    const DEBOUNCE_MS = 500;

    function debounce(fn, delay) {
        let timer = null;
        return function (...args) {
            clearTimeout(timer);
            timer = setTimeout(() => fn.apply(this, args), delay);
        };
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
            } else {
                setState(textarea, "invalid");
                setSaveEnabled(false);
                if (errorContainer) renderErrors(errorContainer, result.errors);
            }

        } catch (err) {
            // Network or server failure → neutral state
            setState(textarea, null);
            if (errorContainer) {
                renderErrors(errorContainer, [
                    "Unable to validate JSON. Please try again."
                ]);
            }
            console.error(err);
        }
    }

    function wire() {
        const textareas = document.querySelectorAll("textarea[data-live-json-validate]");

        if (textareas.length > 0) {
            disableSaveInitially();
        }

        textareas.forEach(textarea => {
            const handler = debounce(() => validateJson(textarea), DEBOUNCE_MS);
            textarea.addEventListener("input", handler);

            // OPTIONAL but recommended:
            // validate immediately if textarea already has content
            if (textarea.value && textarea.value.trim().length > 0) {
                validateJson(textarea);
            }
        });
    }

})();
