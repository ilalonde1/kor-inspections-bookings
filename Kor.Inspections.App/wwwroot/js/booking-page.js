
    const modal = document.getElementById("contactModal");
    const step2Wrapper = document.querySelector(".step2-wrapper");
    const step2 = document.getElementById("step2Fieldset");
    const projectInput = document.getElementById("ProjectNumber");
    const contactEmailField = document.getElementById("ContactEmail");
    const projectSearchBtn = document.getElementById("projectSearchBtn");
    const projectSearchStatus = document.getElementById("projectSearchStatus");
    const projectSuggestions = document.getElementById("projectSuggestions");
    const projectEmailVerificationBox = document.getElementById("projectEmailVerificationBox");
    const projectEmailCodeInput = document.getElementById("projectEmailCodeInput");
    const projectEmailVerificationStatus = document.getElementById("projectEmailVerificationStatus");
    const sendProjectEmailCodeBtn = document.getElementById("sendProjectEmailCodeBtn");
    const verifyProjectEmailCodeBtn = document.getElementById("verifyProjectEmailCodeBtn");

    // NEW: inspections area (optional - only renders if element exists)
    const inspectionLookupArea =
        document.querySelector("#contactModal .modal-inspections-panel #inspectionLookupArea");
    const inspectionDetailsPanel = document.getElementById("inspectionDetailsPanel");
    const inspectionDetailsBody = document.getElementById("inspectionDetailsBody");
    const inspectionDetailsEmpty = document.getElementById("inspectionDetailsEmpty");
    const closeInspectionDetailsBtn = document.getElementById("closeInspectionDetailsBtn");
    const cancelInspectionBtn = document.getElementById("cancelInspectionBtn");
    const inspectionFilterActiveBtn = document.getElementById("inspectionFilterActiveBtn");
    const inspectionFilterAllBtn = document.getElementById("inspectionFilterAllBtn");
    const inspectionListTitle = document.getElementById("inspectionListTitle");
    const inspectionCountLabel = document.getElementById("inspectionCountLabel");
    const inspectionDetailStatus = document.getElementById("inspectionDetailStatus");
    const inspectionDetailStart = document.getElementById("inspectionDetailStart");
    const inspectionDetailEnd = document.getElementById("inspectionDetailEnd");
    const inspectionDetailAddress = document.getElementById("inspectionDetailAddress");
    const inspectionDetailComments = document.getElementById("inspectionDetailComments");

    let currentContacts = [];
    let currentInspections = [];
    let selectedInspectionId = null;
    let inspectionFilterMode = "active";
    let projectSuggestionItems = [];
    let activeProjectSuggestionIndex = -1;
    let projectSuggestRequestCounter = 0;

    const contactFormTitle = document.getElementById("contactFormTitle");
    const editIdInput = document.getElementById("modalEditContactId");
    const contactNameInput = document.getElementById("modalContactName");
    const contactPhoneInput = document.getElementById("modalContactPhone");
    const contactAddressInput = document.getElementById("modalContactAddress");
    const contactEmailInput = document.getElementById("modalContactEmail");
    const cancelEditBtn = document.getElementById("cancelEditBtn");
    let lastModalTrigger = null;
    let modalBackdropPointerDown = false;

    document.getElementById("openContactModalBtn")
        ?.addEventListener("click", e => {
            lastModalTrigger = e.currentTarget;
            openModal();
        });

    document.getElementById("closeModalBtn")
        ?.addEventListener("click", closeModal);

    cancelEditBtn
        .addEventListener("click", resetContactForm);

    function getRequestVerificationToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]').value;
    }

    function describeHttpError(response) {
        if (!response) return "Request failed.";
        if (response.status === 429) return "Too many requests. Please wait a moment and try again.";
        if (response.status === 403) return "Access denied (verification required).";
        if (response.status === 400) return "Your session or security token may have expired. Please refresh the page and try again.";
        if (response.status >= 500) return "Server error. Please try again.";
        return `Request failed (${response.status}).`;
    }

    async function readJsonSafe(response) {
        const text = await response.text();
        if (!text) return null;
        try {
            return JSON.parse(text);
        } catch {
            return null;
        }
    }

    function setProjectEmailVerificationStatus(message) {
        if (!projectEmailVerificationStatus) return;
        projectEmailVerificationStatus.textContent = message || "";
    }

    function hideProjectEmailVerification() {
        if (projectEmailVerificationBox) {
            projectEmailVerificationBox.hidden = true;
        }
        if (projectEmailCodeInput) {
            projectEmailCodeInput.value = "";
        }
    }

    function showProjectEmailVerification() {
        if (projectEmailVerificationBox) {
            projectEmailVerificationBox.hidden = false;
        }
    }

    function getModalFocusableElements() {
        return Array.from(modal.querySelectorAll(
            'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'))
            .filter(el => !el.hidden && el.offsetParent !== null);
    }

    function handleModalKeydown(e) {
        if (modal.style.display !== "flex" || e.key !== "Tab") return;

        const focusable = getModalFocusableElements();
        if (focusable.length === 0) return;

        const first = focusable[0];
        const last = focusable[focusable.length - 1];

        if (e.shiftKey && document.activeElement === first) {
            e.preventDefault();
            last.focus();
            return;
        }

        if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault();
            first.focus();
        }
    }

    async function getProjectEmailVerificationStatus(project, email) {
        const response = await fetch("?handler=ProjectEmailVerificationStatus", {
            method: "POST",
            headers: {
                "RequestVerificationToken": getRequestVerificationToken(),
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ projectNumber: project, email: email })
        });

        if (!response.ok) {
            const data = await readJsonSafe(response);
            setProjectEmailVerificationStatus((data && data.error) || describeHttpError(response));
            return { requiresVerification: true, isVerified: false };
        }

        const data = await readJsonSafe(response);
        if (!data) {
            setProjectEmailVerificationStatus("Unable to check verification status. Please try again.");
            return { requiresVerification: true, isVerified: false };
        }

        return data;
    }

    async function sendVerificationCode(project, email) {
        if (!project || !email) {
            setProjectEmailVerificationStatus("Enter project number and email first.");
            return false;
        }

        if (sendProjectEmailCodeBtn) {
            sendProjectEmailCodeBtn.disabled = true;
        }
        const originalText = sendProjectEmailCodeBtn?.textContent || "";
        if (sendProjectEmailCodeBtn) {
            sendProjectEmailCodeBtn.textContent = "Sending...";
        }

        setProjectEmailVerificationStatus("Sending verification code...");

        try {
            const response = await fetch("?handler=SendProjectEmailCode", {
                method: "POST",
                headers: {
                    "RequestVerificationToken": getRequestVerificationToken(),
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({ projectNumber: project, email: email })
            });

            const data = await readJsonSafe(response);
            if (!response.ok || (data && data.error)) {
                setProjectEmailVerificationStatus((data && data.error) || describeHttpError(response));
                return false;
            }

            setProjectEmailVerificationStatus("Code sent. Check your email and enter the 6-digit code.");
            return true;
        } catch {
            setProjectEmailVerificationStatus("Unable to send verification code.");
            return false;
        } finally {
            if (sendProjectEmailCodeBtn) {
                sendProjectEmailCodeBtn.disabled = false;
                sendProjectEmailCodeBtn.textContent = originalText;
            }
        }
    }

    // Deterministic behavior:
    // - If already verified => return true
    // - If not verified => show verification UI and automatically send a code
    async function ensureProjectEmailVerified(project, email) {
        const status = await getProjectEmailVerificationStatus(project, email);

        if (!status.requiresVerification || status.isVerified) {
            hideProjectEmailVerification();
            setProjectEmailVerificationStatus("");
            return true;
        }

        showProjectEmailVerification();
        await sendVerificationCode(project, email);

        return false;
    }

    async function loadContacts(project, email) {
        const response = await fetch("?handler=LookupContacts", {
            method: "POST",
            headers: {
                "RequestVerificationToken": getRequestVerificationToken(),
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ projectNumber: project, email: email })
        });

        const data = await readJsonSafe(response);
        if (!response.ok || (data && data.error)) {
            throw new Error((data && data.error) || describeHttpError(response));
        }

        if (!Array.isArray(data)) {
            throw new Error("Unexpected server response while loading contacts.");
        }

        renderContacts(data);
    }

    // NEW: load inspections (optional UI)
    async function loadInspections(project, email) {
        const response = await fetch("?handler=LookupInspections", {
            method: "POST",
            headers: {
                "RequestVerificationToken": getRequestVerificationToken(),
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ projectNumber: project, email: email })
        });

        const data = await readJsonSafe(response);
        if (!response.ok || (data && data.error)) {
            throw new Error((data && data.error) || describeHttpError(response));
        }

        if (!Array.isArray(data)) {
            throw new Error("Unexpected server response while loading inspections.");
        }

        renderInspections(data);
    }

    function renderInspections(items) {
        if (!inspectionLookupArea) return;

        currentInspections = Array.isArray(items) ? items : [];
        inspectionLookupArea.innerHTML = "";

        const filtered = currentInspections.filter(i => {
            if (inspectionFilterMode === "all") return true;
            const status = `${i.status ?? ""}`.trim().toLowerCase();
            return status !== "cancelled" && status !== "completed";
        });
        if (inspectionCountLabel) {
            inspectionCountLabel.textContent = `Showing ${filtered.length}`;
        }

        if (filtered.length === 0) {
            inspectionLookupArea.innerHTML = "<p>No inspections found for this filter.</p>";
            hideInspectionDetails();
            return;
        }

        const rows = filtered.map(i => {
            const start = i.startUtc ? new Date(i.startUtc).toLocaleString() : "";
            const status = escapeHtml(`${i.status ?? ""}`.trim());
            const contact = escapeHtml(`${i.contactName ?? ""}`.trim());
            const startText = escapeHtml(start);
            const inspectionId = escapeHtml(i.id);
            const isSelected = selectedInspectionId === i.id;

            return `
                <button type="button" class="inspection-row${isSelected ? " selected" : ""}" data-inspection-id="${inspectionId}">
                    <span class="inspection-row-status">${status || "Unknown"}</span>
                    <span class="inspection-row-time">${startText || "No time"}</span>
                    <span class="inspection-row-contact">${contact || "No contact"}</span>
                    <span class="inspection-row-cta" aria-hidden="true">Details</span>
                </button>
            `;
        }).join("");

        inspectionLookupArea.innerHTML = rows;

        inspectionLookupArea.querySelectorAll(".inspection-row").forEach(row => {
            row.addEventListener("click", () => {
                const id = row.getAttribute("data-inspection-id");
                const selected = currentInspections.find(i => String(i.id) === String(id));
                if (!selected) return;
                openInspectionDetails(selected);
                renderInspections(currentInspections);
            });
        });
    }

    function updateInspectionListTitle() {
        if (!inspectionListTitle) return;
        inspectionListTitle.textContent = inspectionFilterMode === "all"
            ? "All Inspections"
            : "Active Inspections";
    }

    function hideInspectionDetails() {
        selectedInspectionId = null;
        if (inspectionDetailsPanel) {
            inspectionDetailsPanel.classList.remove("open");
            inspectionDetailsPanel.setAttribute("aria-hidden", "true");
        }
        if (inspectionDetailsBody) inspectionDetailsBody.hidden = true;
        if (inspectionDetailsEmpty) inspectionDetailsEmpty.hidden = false;
        if (cancelInspectionBtn) cancelInspectionBtn.removeAttribute("data-inspection-id");
    }

    function openInspectionDetails(inspection) {
        if (!inspection) return;
        selectedInspectionId = inspection.id;

        if (inspectionDetailStatus) inspectionDetailStatus.textContent = inspection.status ?? "";
        if (inspectionDetailStart) inspectionDetailStart.textContent = inspection.startUtc ? new Date(inspection.startUtc).toLocaleString() : "";
        if (inspectionDetailEnd) inspectionDetailEnd.textContent = inspection.endUtc ? new Date(inspection.endUtc).toLocaleString() : "";
        if (inspectionDetailAddress) inspectionDetailAddress.textContent = inspection.address ?? "";
        if (inspectionDetailComments) inspectionDetailComments.textContent = inspection.comments ?? "";

        if (cancelInspectionBtn) cancelInspectionBtn.setAttribute("data-inspection-id", inspection.id ?? "");
        if (inspectionDetailsEmpty) inspectionDetailsEmpty.hidden = true;
        if (inspectionDetailsBody) inspectionDetailsBody.hidden = false;
        if (inspectionDetailsPanel) {
            inspectionDetailsPanel.classList.add("open");
            inspectionDetailsPanel.setAttribute("aria-hidden", "false");
        }
    }

    if (inspectionFilterActiveBtn) {
        inspectionFilterActiveBtn.addEventListener("click", () => {
            inspectionFilterMode = "active";
            inspectionFilterActiveBtn.classList.add("active");
            inspectionFilterAllBtn?.classList.remove("active");
            updateInspectionListTitle();
            renderInspections(currentInspections);
        });
    }

    if (inspectionFilterAllBtn) {
        inspectionFilterAllBtn.addEventListener("click", () => {
            inspectionFilterMode = "all";
            inspectionFilterAllBtn.classList.add("active");
            inspectionFilterActiveBtn?.classList.remove("active");
            updateInspectionListTitle();
            renderInspections(currentInspections);
        });
    }

    if (closeInspectionDetailsBtn) {
        closeInspectionDetailsBtn.addEventListener("click", () => {
            hideInspectionDetails();
            renderInspections(currentInspections);
        });
    }

    if (cancelInspectionBtn) {
        cancelInspectionBtn.addEventListener("click", async () => {
            const id = cancelInspectionBtn.getAttribute("data-inspection-id");
            if (!id) return;

            if (!confirm("Cancel this booking?")) return;

            const response = await fetch("?handler=CancelInspection", {
                method: "POST",
                headers: {
                    "RequestVerificationToken": getRequestVerificationToken(),
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    id: id,
                    projectNumber: (projectInput?.value || "").trim(),
                    email: (contactEmailField?.value || "").trim()
                })
            });

            const data = await readJsonSafe(response);
            if (!response.ok || (data && data.error)) {
                alert((data && data.error) || describeHttpError(response));
                return;
            }

            currentInspections = currentInspections.map(i =>
                String(i.id) === String(id)
                    ? { ...i, status: "Cancelled" }
                    : i);

            hideInspectionDetails();
            renderInspections(currentInspections);
        });
    }

    if (sendProjectEmailCodeBtn) {
        sendProjectEmailCodeBtn.addEventListener("click", async () => {
            const project = (projectInput?.value || "").trim();
            const email = (contactEmailField?.value || "").trim();
            await sendVerificationCode(project, email);
        });
    }

    if (verifyProjectEmailCodeBtn) {
        verifyProjectEmailCodeBtn.addEventListener("click", async () => {
            const project = (projectInput?.value || "").trim();
            const email = (contactEmailField?.value || "").trim();
            const code = (projectEmailCodeInput?.value || "").trim();

            if (!project || !email || !code) {
                setProjectEmailVerificationStatus("Enter project number, email, and code.");
                return;
            }

            verifyProjectEmailCodeBtn.disabled = true;
            const originalText = verifyProjectEmailCodeBtn.textContent;
            verifyProjectEmailCodeBtn.textContent = "Verifying...";

            try {
                const response = await fetch("?handler=VerifyProjectEmailCode", {
                    method: "POST",
                    headers: {
                        "RequestVerificationToken": getRequestVerificationToken(),
                        "Content-Type": "application/json"
                    },
                    body: JSON.stringify({ projectNumber: project, email: email, code: code })
                });

                const data = await readJsonSafe(response);
                if (!response.ok || (data && data.error)) {
                    setProjectEmailVerificationStatus((data && data.error) || describeHttpError(response));
                    return;
                }

                hideProjectEmailVerification();
                setProjectEmailVerificationStatus("Email verified. You can now manage contacts.");

                lastModalTrigger = verifyProjectEmailCodeBtn;
                await openModal();
            } catch {
                setProjectEmailVerificationStatus("Unable to verify code.");
            } finally {
                verifyProjectEmailCodeBtn.disabled = false;
                verifyProjectEmailCodeBtn.textContent = originalText;
            }
        });
    }

    if (projectInput) {
        projectInput.addEventListener("input", () => {
            hideProjectEmailVerification();
            setProjectEmailVerificationStatus("");
        });
    }

    if (contactEmailField) {
        contactEmailField.addEventListener("input", () => {
            hideProjectEmailVerification();
            setProjectEmailVerificationStatus("");
        });
    }

    function closeProjectSuggestions() {
        if (!projectSuggestions) return;
        projectSuggestions.hidden = true;
        projectSuggestions.innerHTML = "";
        projectSuggestionItems = [];
        activeProjectSuggestionIndex = -1;
    }

    function setProjectSearchStatus(message) {
        if (!projectSearchStatus) return;
        projectSearchStatus.textContent = message || "";
    }

    function applyProjectSuggestion(item) {
        if (!item || !item.projectNumber) return;

        projectInput.value = item.projectNumber;

        const projectAddressInput = document.getElementById("ProjectAddress");
        if (projectAddressInput && !projectAddressInput.value && item.address) {
            projectAddressInput.value = item.address;
        }

        closeProjectSuggestions();
        setProjectSearchStatus(`Selected ${item.projectNumber}.`);
    }

    function renderProjectSuggestions(items) {
        if (!projectSuggestions) return;

        projectSuggestionItems = Array.isArray(items) ? items : [];
        activeProjectSuggestionIndex = -1;

        if (projectSuggestionItems.length === 0) {
            closeProjectSuggestions();
            setProjectSearchStatus("No matching projects found.");
            return;
        }

        projectSuggestions.innerHTML = "";

        projectSuggestionItems.forEach((item, index) => {
            const row = document.createElement("button");
            row.type = "button";
            row.className = "project-suggestion-item";
            const number = item.projectNumber || "";
            const name = (item.projectName || "").trim();
            row.textContent = name ? `${number} — ${name}` : number;
            row.addEventListener("click", () => applyProjectSuggestion(item));
            row.addEventListener("mouseenter", () => {
                activeProjectSuggestionIndex = index;
                syncActiveProjectSuggestion();
            });
            projectSuggestions.appendChild(row);
        });

        projectSuggestions.hidden = false;
        setProjectSearchStatus(`${projectSuggestionItems.length} matching project(s) found.`);
    }

    function syncActiveProjectSuggestion() {
        if (!projectSuggestions) return;
        const rows = projectSuggestions.querySelectorAll(".project-suggestion-item");
        rows.forEach((row, index) => {
            row.classList.toggle("active", index === activeProjectSuggestionIndex);
        });
    }

    async function fetchProjectSuggestions() {
        if (!projectInput || !projectSuggestions) return;

        const term = (projectInput.value || "").trim();
        if (term.length < 2) {
            closeProjectSuggestions();
            setProjectSearchStatus("Enter at least 2 characters to search.");
            return;
        }

        const requestId = ++projectSuggestRequestCounter;
        const url = `?handler=ProjectSuggestions&term=${encodeURIComponent(term)}&limit=15`;
        const originalButtonText = projectSearchBtn ? projectSearchBtn.textContent : "";

        if (projectSearchBtn) {
            projectSearchBtn.disabled = true;
            projectSearchBtn.textContent = "Searching...";
        }

        setProjectSearchStatus(`Searching for "${term}"...`);

        try {
            const response = await fetch(url, { method: "GET" });
            if (!response.ok) {
                console.error("Project suggestion request failed:", response.status);
                closeProjectSuggestions();
                setProjectSearchStatus("Project search failed. Please try again.");
                return;
            }

            const data = await readJsonSafe(response);
            if (!Array.isArray(data)) {
                closeProjectSuggestions();
                setProjectSearchStatus("Project search failed. Please try again.");
                return;
            }

            if (requestId !== projectSuggestRequestCounter) {
                return;
            }

            renderProjectSuggestions(data);
        } catch {
            console.error("Project suggestion request threw.");
            closeProjectSuggestions();
            setProjectSearchStatus("Project search failed. Please try again.");
        } finally {
            if (projectSearchBtn) {
                projectSearchBtn.disabled = false;
                projectSearchBtn.textContent = originalButtonText;
            }
        }
    }

    if (projectSearchBtn) {
        projectSearchBtn.addEventListener("click", () => {
            fetchProjectSuggestions();
        });
    }

    if (projectInput) {
        projectInput.addEventListener("input", () => {
            closeProjectSuggestions();
            setProjectSearchStatus("");
        });

        projectInput.addEventListener("keydown", e => {
            const hasOpenSuggestions = projectSuggestionItems.length > 0 && !projectSuggestions.hidden;

            if (e.key === "Enter" && !hasOpenSuggestions) {
                e.preventDefault();
                fetchProjectSuggestions();
                return;
            }

            if (!hasOpenSuggestions) {
                if (e.key === "Escape") {
                    closeProjectSuggestions();
                }
                return;
            }

            if (e.key === "ArrowDown") {
                e.preventDefault();
                activeProjectSuggestionIndex =
                    (activeProjectSuggestionIndex + 1) % projectSuggestionItems.length;
                syncActiveProjectSuggestion();
                return;
            }

            if (e.key === "ArrowUp") {
                e.preventDefault();
                activeProjectSuggestionIndex =
                    activeProjectSuggestionIndex <= 0
                        ? projectSuggestionItems.length - 1
                        : activeProjectSuggestionIndex - 1;
                syncActiveProjectSuggestion();
                return;
            }

            if (e.key === "Enter") {
                if (activeProjectSuggestionIndex >= 0 &&
                    activeProjectSuggestionIndex < projectSuggestionItems.length) {
                    e.preventDefault();
                    applyProjectSuggestion(projectSuggestionItems[activeProjectSuggestionIndex]);
                }
                return;
            }

            if (e.key === "Escape") {
                closeProjectSuggestions();
            }
        });
    }

    document.addEventListener("keydown", e => {
        if (e.key === "Escape") closeModal();
    });

    document.addEventListener("click", e => {
        if (!projectSuggestions || !projectInput) return;
        if (e.target === projectInput || projectSuggestions.contains(e.target)) return;
        closeProjectSuggestions();
    });

    modal.addEventListener("pointerdown", e => {
        modalBackdropPointerDown = (e.target === modal);
    });

    modal.addEventListener("pointerup", e => {
        if (modalBackdropPointerDown && e.target === modal) closeModal();
        modalBackdropPointerDown = false;
    });
    modal.addEventListener("keydown", handleModalKeydown);
    document.querySelector(".kor-modal-content")
        ?.addEventListener("click", e => e.stopPropagation());

    function resetContactForm() {
        editIdInput.value = "";
        contactFormTitle.textContent = "Add Contact";
        document.getElementById("saveContactBtn").textContent = "Save Contact";
        cancelEditBtn.style.display = "none";

        contactNameInput.value = "";
        contactPhoneInput.value = "";
        contactAddressInput.value = "";
        contactEmailInput.readOnly = false;
        contactEmailInput.value = (contactEmailField?.value || "").trim();
    }

    function beginEditContact(id) {
        const contact = currentContacts.find(c => c.id === id);
        if (!contact) {
            alert("Contact not found.");
            return;
        }

        editIdInput.value = String(contact.id);
        contactFormTitle.textContent = `Editing: ${contact.name ?? "Contact"}`;
        document.getElementById("saveContactBtn").textContent = "Save Changes";
        cancelEditBtn.style.display = "";
        contactNameInput.value = contact.name ?? "";
        contactPhoneInput.value = contact.phone ?? "";
        contactAddressInput.value = contact.address ?? "";
        contactEmailInput.readOnly = true;
        contactEmailInput.value = contact.email ?? (document.getElementById("ContactEmail").value || "");
    }

    async function openModal() {

        const projectInputEl = document.getElementById("ProjectNumber");
        const emailInputEl = document.getElementById("ContactEmail");

        // force browser to finalize typed values
        projectInputEl.blur();
        emailInputEl.blur();

        const project = (projectInputEl?.value || "").trim();
        const email = (emailInputEl?.value || "").trim();

        if (!project || !email) {
            alert("Enter a valid project number and email first.");
            return;
        }

        inspectionFilterMode = "active";
        inspectionFilterActiveBtn?.classList.add("active");
        inspectionFilterAllBtn?.classList.remove("active");
        updateInspectionListTitle();

        try {
            await loadContacts(project, email);
        } catch (err) {
            const message = (err && err.message) || "";
            if (!/verify|verification|access denied|403/i.test(message)) {
                alert(message || "Unable to load contacts.");
                return;
            }

            const verified = await ensureProjectEmailVerified(project, email);
            if (!verified) {
                return;
            }

            try {
                await loadContacts(project, email);
            } catch (retryErr) {
                alert((retryErr && retryErr.message) || "Unable to load contacts.");
                return;
            }
        }

        currentInspections = [];
        // New feature: load inspections if the UI exists
        if (inspectionLookupArea) {
            try {
                await loadInspections(project, email);
            } catch (err) {
                console.warn("Unable to load inspections:", err);
                currentInspections = [];
            }
        }
        hideInspectionDetails();
        renderInspections(currentInspections);

        resetContactForm();
        modal.style.display = "flex";
        document.body.style.overflow = "hidden";
        getModalFocusableElements()[0]?.focus();
    }

    function closeModal() {
        hideInspectionDetails();
        modal.style.display = "none";
        document.body.style.overflow = "";
        lastModalTrigger?.focus();
    }

    function renderContacts(data) {

        const area = document.getElementById("contactLookupArea");
        currentContacts = Array.isArray(data) ? data : [];
        area.innerHTML = "";

        if (!data || data.length === 0) {
            area.innerHTML = "<p>No saved contacts yet for this project and email domain. Create your first contact below to continue.</p>";
            return;
        }

        data.forEach(c => {
            const name = escapeHtml(c.name);
            const email = escapeHtml(c.email);
            const phone = escapeHtml(c.phone);
            const contactId = Number(c.id);

            const row = document.createElement("div");
            row.className = "contact-row";

            row.innerHTML = `
                <div class="contact-row-main">
                    <div class="contact-row-name">${name}</div>
                    <div class="contact-row-meta">${email} · ${phone}</div>
                </div>
                <div class="contact-row-actions">
                    <button type="button" data-action="select" class="btn btn-sm btn-secondary">Use</button>
                    <button type="button" data-action="edit" class="btn btn-sm btn-outline-primary">Edit</button>
                    <button type="button" data-action="delete" data-confirm="Delete this contact?" class="btn btn-sm btn-danger">Delete</button>
                </div>
            `;

            area.appendChild(row);

            row.querySelector('[data-action="select"]').addEventListener("click", () => selectContact(contactId));
            row.querySelector('[data-action="edit"]').addEventListener("click", () => beginEditContact(contactId));
            row.querySelector('[data-action="delete"]').addEventListener("click", () => deleteContact(contactId));
        });
    }

    function selectContact(id) {

        const project = document.getElementById("ProjectNumber").value;
        const email = document.getElementById("ContactEmail").value;

        const qs = new URLSearchParams({
            handler: "SelectContact",
            id: String(id),
            ProjectNumber: project ?? "",
            ContactEmail: email ?? ""
        });

        fetch("?" + qs.toString(), {
            method: "POST",
            headers: {
                "RequestVerificationToken":
                    document.querySelector('input[name="__RequestVerificationToken"]').value
            }
        })
        .then(async r => {
            const data = await readJsonSafe(r);
            if (!r.ok) {
                throw new Error((data && data.error) || describeHttpError(r));
            }
            if (!data) {
                throw new Error("Unexpected server response.");
            }
            return data;
        })
        .then(c => {
            if (c && c.error) {
                alert(c.error);
                return;
            }

            document.getElementById("SelectedContactId").value = c.id;
            document.getElementById("ContactName").value = c.name;
            document.getElementById("ContactPhone").value = c.phone;
            document.getElementById("ProjectAddress").value = c.address ?? "";

            step2.disabled = false;

            if (step2Wrapper) {
                step2Wrapper.classList.remove("step2-locked");

                const lockMsg = step2Wrapper.querySelector(".step-lock-overlay");
                if (lockMsg) {
                    lockMsg.remove();
                }
            }

            closeModal();

            step2.scrollIntoView({ behavior: "smooth", block: "start" });
        })
        .catch(err => alert(err.message || "Failed to select contact."));
    }

    function deleteContact(id) {

        const project = document.getElementById("ProjectNumber").value;
        const email = document.getElementById("ContactEmail").value;

        const deleteQs = new URLSearchParams({
            handler: "DeleteContact",
            id: String(id),
            ProjectNumber: project ?? "",
            ContactEmail: email ?? ""
        });

        fetch("?" + deleteQs.toString(), {
            method: "POST",
            headers: {
                "RequestVerificationToken":
                    document.querySelector('input[name="__RequestVerificationToken"]').value
            }
        })
        .then(async r => {
            const data = await readJsonSafe(r);
            if (!r.ok || (data && data.error)) {
                throw new Error((data && data.error) || describeHttpError(r));
            }
        })
        .then(() => loadContacts(project, email))
        .catch(err => alert(err.message || "Failed to delete contact."));
    }

    document.getElementById("saveContactBtn")
        ?.addEventListener("click", () => {

            const saveBtn = document.getElementById("saveContactBtn");
            const originalSaveText = saveBtn.textContent;
            saveBtn.disabled = true;
            saveBtn.textContent = "Saving...";
            const editIdRaw = editIdInput.value;
            const editId = editIdRaw ? Number(editIdRaw) : null;
            const isEdit = Number.isFinite(editId);
            const existingContact = isEdit
                ? currentContacts.find(c => c.id === editId)
                : null;
            const emailForSave = isEdit
                ? (existingContact?.email ?? "")
                : (contactEmailInput.value || "");

            if (!isEdit && !emailForSave.trim()) {
                alert("Email is required.");
                saveBtn.disabled = false;
                saveBtn.textContent = originalSaveText;
                return;
            }

            const payload = {
                id: Number.isFinite(editId) ? editId : null,
                name: contactNameInput.value,
                phone: contactPhoneInput.value,
                address: contactAddressInput.value,
                projectNumber: document.getElementById("ProjectNumber").value,
                requesterEmail: (contactEmailField?.value || emailForSave),
                email: emailForSave
            };

            fetch("?handler=SaveContactAjax", {
                method: "POST",
                headers: {
                    "RequestVerificationToken":
                        document.querySelector('input[name="__RequestVerificationToken"]').value,
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(payload)
            })
            .then(async r => {
                const data = await readJsonSafe(r);
                if (!r.ok) {
                    throw new Error((data && data.error) || describeHttpError(r));
                }
                return data;
            })
            .then(data => {
                if (data && data.error) {
                    alert(data.error);
                    return;
                }

                if (!data || typeof data.id !== "number") {
                    alert("Unable to save contact. Please try again.");
                    return;
                }

                if (!isEdit) {
                    const project = document.getElementById("ProjectNumber").value;
                    const email = document.getElementById("ContactEmail").value;

                    loadContacts(project, email)
                        .catch(err => alert((err && err.message) || "Unable to load contacts."));
                    resetContactForm();
                    return;
                }

                selectContact(data.id);
            })
            .finally(() => {
                saveBtn.disabled = false;
                saveBtn.textContent = originalSaveText;
            });
        });

    contactPhoneInput.addEventListener("input", () => {
        contactPhoneInput.value = window.formatKorPhoneInput(contactPhoneInput.value);
    });

