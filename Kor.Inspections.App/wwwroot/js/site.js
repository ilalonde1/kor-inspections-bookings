window.escapeHtml = function escapeHtml(str) {
    if (str == null) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
};

(function () {

    document.addEventListener("DOMContentLoaded", function () {

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
                disableMobile: true,

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

        if (highlightInput) {
            highlightInput.scrollIntoView({ behavior: "smooth", block: "center" });
            highlightInput.focus();
        }

    });

})();
