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


        //---------------------------------------
        // Auto-load saved contacts
        //---------------------------------------

        const projectInput = document.querySelector("input[name='ProjectNumber']");
        const emailInput = document.querySelector("input[name='ContactEmail']");
        const loadBtn = document.getElementById("loadSavedBtn");

        // DO NOT return the whole script if missing.
        // This was a hidden bug in your previous version.
        if (projectInput && emailInput && loadBtn) {

            let timeout = null;
            let hasAutoLoaded = false;

            function tryAutoLoad() {

                if (hasAutoLoaded) return;

                const project = projectInput.value.trim();
                const email = emailInput.value.trim();

                if (project.length >= 5 && email.includes("@")) {

                    clearTimeout(timeout);

                    timeout = setTimeout(() => {

                        if (!document.querySelector(".saved-box")) {
                            hasAutoLoaded = true;
                            loadBtn.click();
                        }

                    }, 600);
                }
            }

            projectInput.addEventListener("input", tryAutoLoad);
            emailInput.addEventListener("input", tryAutoLoad);
        }


    });

})();
