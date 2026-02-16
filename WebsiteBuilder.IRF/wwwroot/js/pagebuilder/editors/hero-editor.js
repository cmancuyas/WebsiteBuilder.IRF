// wwwroot/js/pagebuilder/editors/hero-editor.js
(function () {
    "use strict";

    function initHeroEditors() {
        document.querySelectorAll('[data-section-type="hero"]').forEach(rootEl => {
            const editor = new window.PageBuilder.SectionEditorBase(rootEl, {
                saveUrl: "/Admin/Pages/Sections/SaveSectionSettings", // update to your actual endpoint

                readSettings: (root) => {
                    return {
                        heading: root.querySelector('[data-hero-heading]')?.value?.trim() || "",
                        subheading: root.querySelector('[data-hero-subheading]')?.value?.trim() || "",
                        ctaText: root.querySelector('[data-hero-cta-text]')?.value?.trim() || "",
                        ctaUrl: root.querySelector('[data-hero-cta-url]')?.value?.trim() || "",
                    };
                },

                validateClient: (s) => {
                    const errors = [];
                    if (!s.heading) errors.push({ message: "Heading is required." });
                    if (s.ctaUrl && !isValidUrlOrPath(s.ctaUrl)) errors.push({ message: "CTA URL must be a valid URL or path." });
                    return errors.length ? { ok: false, errors } : { ok: true };
                }
            });

            // Bind inputs so any change triggers autosave
            editor.bindInputs("[data-hero-heading],[data-hero-subheading],[data-hero-cta-text],[data-hero-cta-url]");
        });
    }

    function isValidUrlOrPath(value) {
        // allow /relative paths or absolute URLs
        if (value.startsWith("/")) return true;
        try { new URL(value); return true; } catch { return false; }
    }

    // Expose single init
    window.PageBuilder = window.PageBuilder || {};
    window.PageBuilder.initHeroEditors = initHeroEditors;
})();
