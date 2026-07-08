document.addEventListener("DOMContentLoaded", () => {
    const busyOverlay = document.getElementById("globalBusyOverlay");
    const countdownRoot = document.getElementById("automationCountdown");
    const categoryPickers = document.querySelectorAll(".category-picker");
    const tagPickers = document.querySelectorAll("[data-tag-picker]");
    const semiPrivatePrivacyCheckbox = document.querySelector("input[name='SelectedAutoAddPrivacyFilters'][value='semi-private']");
    const globalCredentialFields = document.querySelectorAll("[data-global-credential-field]");

    if (busyOverlay) {
        const showBusyOverlay = () => {
            if (busyOverlay.classList.contains("is-active")) {
                return;
            }

            document.body.classList.add("is-busy");
            busyOverlay.classList.add("is-active");
            busyOverlay.setAttribute("aria-hidden", "false");
        };

        const trackedForms = document.querySelectorAll("form[data-long-operation='true']");
        trackedForms.forEach((form) => {
            form.addEventListener("submit", () => {
                const trigger = document.activeElement;
                if (trigger instanceof HTMLButtonElement || trigger instanceof HTMLInputElement) {
                    trigger.setAttribute("disabled", "disabled");
                }

                showBusyOverlay();
            });
        });
    }

    if (countdownRoot) {
        const valueNode = countdownRoot.querySelector("[data-role='countdown-value']");
        const labelNode = document.querySelector("[data-role='automation-status-label']");
        const lastStartNode = document.querySelector("[data-role='last-start']");
        const lastEndNode = document.querySelector("[data-role='last-end']");
        const messageLabelNode = document.querySelector("[data-role='message-label']");
        const messageValueNode = document.querySelector("[data-role='message-value']");
        const statusEndpoint = countdownRoot.dataset.statusEndpoint;

        let enabled = countdownRoot.dataset.enabled === "true";
        let running = countdownRoot.dataset.running === "true";
        let intervalSeconds = Math.max(60, Number.parseInt(countdownRoot.dataset.intervalSeconds ?? "900", 10) || 900);
        let nextRunUtc = countdownRoot.dataset.nextRunUtc ? Date.parse(countdownRoot.dataset.nextRunUtc) : Number.NaN;

        const nextLabel = countdownRoot.dataset.labelNext ?? "Next run";
        const dueLabel = countdownRoot.dataset.labelDue ?? "Due";
        const runningLabel = countdownRoot.dataset.labelRunning ?? "In progress";
        const disabledLabel = countdownRoot.dataset.labelDisabled ?? "No schedule";
        const messageLabel = countdownRoot.dataset.labelMessage ?? "Message";
        const indexerStateLabel = countdownRoot.dataset.labelIndexerState ?? "Indexer status";
        const noRunsLabel = countdownRoot.dataset.labelNoRuns ?? "-";

        const setVisualState = (progress, color) => {
            const clamped = Math.max(0, Math.min(1, progress));
            countdownRoot.style.setProperty("--countdown-progress", `${clamped}`);
            countdownRoot.style.setProperty("--countdown-color", color);
        };

        const formatRemaining = (seconds) => {
            const total = Math.max(0, Math.floor(seconds));
            const hours = Math.floor(total / 3600);
            const minutes = Math.floor((total % 3600) / 60);
            const secs = total % 60;

            if (hours > 0) {
                return `${hours}:${String(minutes).padStart(2, "0")}:${String(secs).padStart(2, "0")}`;
            }

            return `${String(minutes).padStart(2, "0")}:${String(secs).padStart(2, "0")}`;
        };

        const formatDateTime = (isoValue) => {
            if (!isoValue) {
                return "-";
            }

            const parsed = new Date(isoValue);
            if (Number.isNaN(parsed.getTime())) {
                return "-";
            }

            const day = String(parsed.getDate()).padStart(2, "0");
            const month = String(parsed.getMonth() + 1).padStart(2, "0");
            const year = parsed.getFullYear();
            const hours = String(parsed.getHours()).padStart(2, "0");
            const minutes = String(parsed.getMinutes()).padStart(2, "0");

            return `${day}/${month}/${year} ${hours}:${minutes}`;
        };

        const applyMessage = (rawMessage) => {
            if (!(messageLabelNode instanceof HTMLElement) || !(messageValueNode instanceof HTMLElement)) {
                return;
            }

            if (!rawMessage) {
                messageLabelNode.textContent = `${messageLabel}:`;
                messageValueNode.textContent = "-";
                return;
            }

            const separatorIndex = rawMessage.indexOf(":");
            if (separatorIndex >= 0) {
                const detail = rawMessage.slice(separatorIndex + 1).trim().replace(/\.$/, "");
                if (/ok/i.test(detail) && /ko/i.test(detail)) {
                    messageLabelNode.textContent = `${indexerStateLabel}:`;
                    messageValueNode.textContent = detail;
                    return;
                }
            }

            messageLabelNode.textContent = `${messageLabel}:`;
            messageValueNode.textContent = rawMessage;
        };

        const renderCountdown = () => {
            if (!valueNode || !(labelNode instanceof HTMLElement)) {
                return;
            }

            if (!enabled || Number.isNaN(nextRunUtc)) {
                valueNode.textContent = "--";
                labelNode.textContent = disabledLabel;
                labelNode.classList.remove("is-running", "is-idle");
                labelNode.classList.add("is-disabled");
                setVisualState(0, "#7b8798");
                return;
            }

            if (running) {
                valueNode.textContent = "LIVE";
                labelNode.textContent = runningLabel;
                labelNode.classList.remove("is-disabled", "is-idle");
                labelNode.classList.add("is-running");
                setVisualState(1, "#7fe7c4");
                return;
            }

            const remainingSeconds = Math.max(0, (nextRunUtc - Date.now()) / 1000);
            const progress = remainingSeconds / intervalSeconds;
            valueNode.textContent = remainingSeconds <= 0 ? "00:00" : formatRemaining(remainingSeconds);
            labelNode.textContent = remainingSeconds <= 0 ? dueLabel : nextLabel;
            labelNode.classList.remove("is-running", "is-disabled");
            labelNode.classList.add("is-idle");

            const color = remainingSeconds <= 30
                ? "#ff7a45"
                : remainingSeconds <= 120
                    ? "#f7c85d"
                    : "#5ec8ff";

            setVisualState(progress, color);
        };

        const refreshStatus = async () => {
            if (!statusEndpoint) {
                return;
            }

            try {
                const response = await fetch(statusEndpoint, { headers: { Accept: "application/json" } });
                if (!response.ok) {
                    return;
                }

                const data = await response.json();
                enabled = Boolean(data.enabled);
                running = Boolean(data.running);
                intervalSeconds = Math.max(60, Number.parseInt(data.intervalSeconds, 10) || intervalSeconds);
                nextRunUtc = data.nextRunUtc ? Date.parse(data.nextRunUtc) : Number.NaN;

                if (lastStartNode instanceof HTMLElement) {
                    lastStartNode.textContent = formatDateTime(data.lastStartedUtc);
                }

                if (lastEndNode instanceof HTMLElement) {
                    lastEndNode.textContent = formatDateTime(data.lastCompletedUtc);
                }

                applyMessage(data.lastStartedUtc || data.lastCompletedUtc ? data.lastMessage : noRunsLabel);
                renderCountdown();
            } catch {
                // Rete non disponibile o richiesta interrotta: si riprova al prossimo giro.
            }
        };

        renderCountdown();
        window.setInterval(renderCountdown, 1000);
        // Interroga lo stato reale ogni pochi secondi così la card si aggiorna da sola
        // (avvio/fine esecuzione, nuovo orario pianificato) senza bisogno di un refresh manuale.
        window.setInterval(refreshStatus, 5000);
        refreshStatus();
    }

    categoryPickers.forEach((picker) => {
        const checkboxes = Array.from(picker.querySelectorAll("input[type='checkbox'][data-category-depth]"));
        if (checkboxes.length === 0) {
            return;
        }

        picker.addEventListener("change", (event) => {
            const target = event.target;
            if (!(target instanceof HTMLInputElement) || target.type !== "checkbox") {
                return;
            }

            const startIndex = checkboxes.indexOf(target);
            if (startIndex === -1) {
                return;
            }

            const parentDepth = Number.parseInt(target.dataset.categoryDepth ?? "", 10);
            if (Number.isNaN(parentDepth)) {
                return;
            }

            for (let index = startIndex + 1; index < checkboxes.length; index += 1) {
                const child = checkboxes[index];
                const childDepth = Number.parseInt(child.dataset.categoryDepth ?? "", 10);
                if (Number.isNaN(childDepth) || childDepth <= parentDepth) {
                    break;
                }

                child.checked = target.checked;
            }
        });
    });

    if (semiPrivatePrivacyCheckbox instanceof HTMLInputElement && globalCredentialFields.length > 0) {
        const syncGlobalCredentialState = () => {
            const enabled = semiPrivatePrivacyCheckbox.checked;

            globalCredentialFields.forEach((field) => {
                field.classList.toggle("is-disabled", !enabled);

                const inputs = field.querySelectorAll("[data-global-credential-input]");
                inputs.forEach((input) => {
                    if (input instanceof HTMLInputElement) {
                        input.disabled = !enabled;
                    }
                });
            });
        };

        syncGlobalCredentialState();
        semiPrivatePrivacyCheckbox.addEventListener("change", syncGlobalCredentialState);
    }

    tagPickers.forEach((picker) => {
        const hiddenInput = picker.querySelector("input[type='hidden']");
        const textInput = picker.querySelector("[data-role='input']");
        const pillsRoot = picker.querySelector("[data-role='pills']");
        const menu = picker.querySelector("[data-role='menu']");
        const surface = picker.querySelector("[data-role='surface']");
        if (!(hiddenInput instanceof HTMLInputElement)
            || !(textInput instanceof HTMLInputElement)
            || !(pillsRoot instanceof HTMLElement)
            || !(menu instanceof HTMLElement)
            || !(surface instanceof HTMLElement)) {
            return;
        }

        let availableTags = [];
        try {
            const parsed = JSON.parse(picker.getAttribute("data-available-tags") ?? "[]");
            if (Array.isArray(parsed)) {
                availableTags = parsed
                    .map((tag) => ({
                        id: Number.parseInt(String(tag.id ?? tag.Id ?? "0"), 10) || 0,
                        label: String(tag.label ?? tag.Label ?? "").trim()
                    }))
                    .filter((tag) => tag.label.length > 0);
            }
        } catch {
            availableTags = [];
        }

        const selectedTags = [];
        const syncHiddenInput = () => {
            hiddenInput.value = selectedTags.join(", ");
        };

        const renderPills = () => {
            pillsRoot.replaceChildren();

            selectedTags.forEach((tag) => {
                const pill = document.createElement("span");
                pill.className = "tag-pill";
                pill.textContent = tag;

                const removeButton = document.createElement("button");
                removeButton.type = "button";
                removeButton.className = "tag-pill-remove";
                removeButton.setAttribute("aria-label", `Remove ${tag}`);
                removeButton.textContent = "x";
                removeButton.addEventListener("click", () => {
                    const index = selectedTags.findIndex((item) => item.localeCompare(tag, undefined, { sensitivity: "accent" }) === 0);
                    if (index >= 0) {
                        selectedTags.splice(index, 1);
                        syncHiddenInput();
                        renderPills();
                        renderSuggestions();
                        textInput.focus();
                    }
                });

                pill.appendChild(removeButton);
                pillsRoot.appendChild(pill);
            });
        };

        const normalizeTag = (value) => value.trim();

        const isSelected = (value) => selectedTags.some((tag) => tag.localeCompare(value, undefined, { sensitivity: "accent" }) === 0);

        const findAvailableTag = (value) => {
            const normalized = normalizeTag(value);
            if (!normalized) {
                return null;
            }

            return availableTags.find((tag) => tag.label.localeCompare(normalized, undefined, { sensitivity: "accent" }) === 0) ?? null;
        };

        const getSuggestions = () => {
            const query = normalizeTag(textInput.value).toLocaleLowerCase();
            return availableTags
                .filter((tag) => !isSelected(tag.label))
                .filter((tag) => query.length === 0 || tag.label.toLocaleLowerCase().includes(query))
                .slice(0, 8);
        };

        const findPreferredTag = () => {
            const exactMatch = findAvailableTag(textInput.value);
            if (exactMatch) {
                return exactMatch;
            }

            const suggestions = getSuggestions();
            return suggestions.length > 0 ? suggestions[0] : null;
        };

        const addTag = (value) => {
            const matchedTag = findAvailableTag(value);
            if (!matchedTag || isSelected(matchedTag.label)) {
                return;
            }

            selectedTags.push(matchedTag.label);
            syncHiddenInput();
            renderPills();
        };

        const hideSuggestions = () => {
            menu.hidden = true;
            menu.replaceChildren();
        };

        const renderSuggestions = () => {
            const suggestions = getSuggestions();
            menu.replaceChildren();

            if (suggestions.length === 0 || document.activeElement !== textInput) {
                menu.hidden = true;
                return;
            }

            suggestions.forEach((tag) => {
                const option = document.createElement("button");
                option.type = "button";
                option.className = "tag-picker-option";
                option.textContent = tag.label;
                option.addEventListener("mousedown", (event) => {
                    event.preventDefault();
                    addTag(tag.label);
                    textInput.value = "";
                    hideSuggestions();
                    textInput.focus();
                });
                menu.appendChild(option);
            });

            menu.hidden = false;
        };

        hiddenInput.value
            .split(",")
            .map((tag) => normalizeTag(tag))
            .filter((tag) => tag.length > 0)
            .forEach((tag) => addTag(tag));

        surface.addEventListener("click", () => {
            textInput.focus();
        });

        textInput.addEventListener("input", () => {
            renderSuggestions();
        });

        textInput.addEventListener("focus", () => {
            renderSuggestions();
        });

        textInput.addEventListener("keydown", (event) => {
            if (event.key === "Enter" || event.key === ",") {
                const value = normalizeTag(textInput.value);
                if (!value) {
                    return;
                }

                event.preventDefault();
                const matchedTag = findPreferredTag();
                if (matchedTag) {
                    addTag(matchedTag.label);
                    textInput.value = "";
                    hideSuggestions();
                }
                return;
            }

            if (event.key === "Backspace" && textInput.value.length === 0 && selectedTags.length > 0) {
                selectedTags.pop();
                syncHiddenInput();
                renderPills();
                renderSuggestions();
            }

            if (event.key === "Escape") {
                hideSuggestions();
            }
        });

        textInput.addEventListener("blur", () => {
            window.setTimeout(() => {
                if (!findPreferredTag()) {
                    textInput.value = "";
                }
                hideSuggestions();
            }, 100);
        });

        renderPills();
    });
});
