window.escapeHtml = function escapeHtml(str) {
    if (str == null) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
};

window.formatKorPhoneInput = function formatKorPhoneInput(str, options) {
    const preserveTrailingSeparator = options && options.preserveTrailingSeparator === true;
    const digits = String(str || "").replace(/\D/g, "").slice(0, 10);

    if (digits.length >= 7) {
        return `(${digits.slice(0, 3)})-${digits.slice(3, 6)}-${digits.slice(6)}`;
    }

    if (digits.length >= 4) {
        return `(${digits.slice(0, 3)})-${digits.slice(3)}`;
    }

    if (digits.length === 3 && preserveTrailingSeparator) {
        return `(${digits})-`;
    }

    if (digits.length > 0) {
        return `(${digits}`;
    }

    return "";
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

// Generic confirm-on-click for buttons/forms with data-confirm.
// Replaces inline onclick="return confirm(...)" patterns so we stay CSP-friendly.
document.addEventListener("click", function (e) {
    const target = e.target?.closest?.("[data-confirm]");
    if (!target) return;
    const message = target.getAttribute("data-confirm");
    if (message && !window.confirm(message)) {
        e.preventDefault();
        e.stopImmediatePropagation();
    }
}, true);

(function () {

    document.addEventListener("DOMContentLoaded", function () {
        document.addEventListener("submit", function () {
            window.KorPostbackFocus.save();
        }, true);

        function updateModes() {
            const isMobile = window.matchMedia("(max-width: 1000px)").matches;

            if (isMobile) {
                document.body.classList.add("mobile-mode");
            } else {
                document.body.classList.remove("mobile-mode");
            }

            const inspectorFlag = document.body.dataset.inspector === "true";

            if (isMobile && inspectorFlag) {
                document.body.classList.add("inspector-mode");
            } else {
                document.body.classList.remove("inspector-mode");
            }
        }

        updateModes();
        window.addEventListener("resize", updateModes);

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
                    const btn = document.getElementById("autoRefreshTimesBtn");
                    if (!btn) return;
                    const form = btn.closest("form");
                    if (!form) return;
                    const handler = btn.getAttribute("data-refresh-handler");
                    const url = new URL(form.action || window.location.href, window.location.origin);
                    if (handler) url.searchParams.set("handler", handler);
                    const tempForm = form.cloneNode(true);
                    tempForm.action = url.toString();
                    tempForm.setAttribute("novalidate", "novalidate");
                    document.body.appendChild(tempForm);
                    tempForm.submit();
                }
            });
        }


        //---------------------------------------
        // Phone formatting while typing
        //---------------------------------------

        document.addEventListener("input", function (e) {

            if (!e.target.name || !e.target.name.includes("ContactPhone"))
                return;

            e.target.value = window.formatKorPhoneInput(e.target.value, {
                preserveTrailingSeparator: true
            });
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
