window.escapeHtml = function escapeHtml(str) {
    if (str == null) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
};

window.KorPostbackFocus = (function () {
    const key = `kor:focus:${window.location.pathname}`;

    function buildSelector(element) {
        if (!element) return null;

        const focusable = resolvePersistentTarget(element);
        if (!focusable) return null;

        if (focusable.id) {
            return { type: "id", value: focusable.id };
        }

        if (focusable.name) {
            return { type: "name", value: focusable.name };
        }

        return null;
    }

    function resolvePersistentTarget(element) {
        if (!element) return null;

        if (element.id || element.name) return element;

        if (element.classList?.contains("flatpickr-input")) {
            const siblingInput = element.parentElement?.querySelector("input[id], input[name], select[id], select[name], textarea[id], textarea[name]");
            if (siblingInput) return siblingInput;
        }

        return null;
    }

    function findTarget(selector) {
        if (!selector) return null;

        if (selector.type === "id") {
            return document.getElementById(selector.value);
        }

        if (selector.type === "name") {
            return document.querySelector(`[name="${CSS.escape(selector.value)}"]`);
        }

        return null;
    }

    function save() {
        const active = document.activeElement;
        const selector = buildSelector(active);
        if (!selector) return;

        sessionStorage.setItem(key, JSON.stringify({
            selector,
            scrollX: window.scrollX,
            scrollY: window.scrollY
        }));
    }

    function restore() {
        const raw = sessionStorage.getItem(key);
        if (!raw) return false;

        try {
            const payload = JSON.parse(raw);
            const target = findTarget(payload.selector);
            if (!target) return false;

            const focusTarget = target._flatpickr?.altInput || target;
            const scrollTarget =
                focusTarget.closest?.(".form-group") ||
                focusTarget.closest?.("fieldset") ||
                focusTarget;

            sessionStorage.removeItem(key);

            window.requestAnimationFrame(() => {
                if (scrollTarget && typeof scrollTarget.scrollIntoView === "function") {
                    scrollTarget.scrollIntoView({
                        behavior: "auto",
                        block: "center",
                        inline: "nearest"
                    });
                } else {
                    window.scrollTo({
                        left: payload.scrollX || 0,
                        top: payload.scrollY || 0
                    });
                }

                window.requestAnimationFrame(() => {
                    if (typeof focusTarget.focus === "function") {
                        focusTarget.focus({ preventScroll: true });
                    }
                });
            });

            return true;
        } catch {
            sessionStorage.removeItem(key);
            return false;
        }
    }

    return {
        save,
        restore
    };
})();

(function () {

    document.addEventListener("DOMContentLoaded", function () {
        document.addEventListener("submit", function () {
            window.KorPostbackFocus.save();
        }, true);

        //---------------------------------------
        // Flatpickr
        //---------------------------------------

        const dateInput = document.getElementById("requestedDate");

        // ALWAYS guard this
        if (dateInput) {

            flatpickr(dateInput, {

                // CRITICAL FIXES
                defaultDate: dateInput.value || null,
                allowInput: true,        // prevents click lock
                clickOpens: true,

                altInput: true,
                altFormat: "F j, Y",
                dateFormat: "Y-m-d",

                minDate: dateInput.dataset.mindate,
                maxDate: dateInput.dataset.maxdate,

                //---------------------------------------
                // Fix Razor postback timing bug
                //---------------------------------------
                onReady: function (selectedDates, dateStr, instance) {

                    // If Razor populated AFTER init,
                    // force sync.
                    if (dateInput.value) {
                        instance.setDate(dateInput.value, false);
                    }
                },

                onChange: function () {
                    document.getElementById("autoRefreshTimesBtn")?.click();
                }
            });
        }


        //---------------------------------------
        // Phone formatting while typing
        //---------------------------------------

        document.addEventListener("input", function (e) {

            if (!e.target.name || !e.target.name.includes("ContactPhone"))
                return;

            let digits = e.target.value.replace(/\D/g, "").substring(0, 10);

            if (digits.length >= 6)
                e.target.value = `(${digits.slice(0, 3)})-${digits.slice(3, 6)}-${digits.slice(6)}`;
            else if (digits.length >= 3)
                e.target.value = `(${digits.slice(0, 3)})-${digits.slice(3)}`;
            else
                e.target.value = digits;
        });


        //---------------------------------------
        // Auto-scroll to new contact fields
        //---------------------------------------

        const highlightInput = document.querySelector(".new-contact-highlight input");

        if (window.KorPostbackFocus.restore()) {
            return;
        }

        if (highlightInput) {
            highlightInput.scrollIntoView({ behavior: "smooth", block: "center" });
            highlightInput.focus();
        }

    });

})();
