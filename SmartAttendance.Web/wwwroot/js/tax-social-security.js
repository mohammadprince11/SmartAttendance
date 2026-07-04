(() => {
    const toNumber = (value) => {
        const n = Number(String(value ?? "").replace(/,/g, ""));
        return Number.isFinite(n) ? n : 0;
    };

    const roundBySettings = (value) => {
        const roundFlag = document.getElementById("settingsRound")?.value === "1";
        if (roundFlag) {
            return Math.round(value);
        }
        return Math.round(value * 100) / 100;
    };

    const el = (id) => document.getElementById(id);

    const grossInput = el("grossSalaryInput");
    const taxableInput = el("taxableSalaryInput");
    const taxInput = el("taxAmountInput");
    const employeeSocialInput = el("employeeSocialInput");
    const companySocialInput = el("companySocialInput");
    const netPreview = el("netSalaryPreview");

    const updateNetPreview = () => {
        if (!netPreview) return;

        const gross = toNumber(grossInput?.value);
        const tax = toNumber(taxInput?.value);
        const employeeSocial = toNumber(employeeSocialInput?.value);
        netPreview.value = roundBySettings(gross - tax - employeeSocial);
    };

    const calculateBySettings = () => {
        const gross = toNumber(grossInput?.value);
        const taxRate = toNumber(el("settingsTaxRate")?.value);
        const exemption = toNumber(el("settingsTaxExemption")?.value);
        const employeeRate = toNumber(el("settingsEmployeeRate")?.value);
        const companyRate = toNumber(el("settingsCompanyRate")?.value);
        const ceiling = toNumber(el("settingsSocialCeiling")?.value);

        const taxable = Math.max(gross - exemption, 0);
        const socialBase = ceiling > 0 && gross > ceiling ? ceiling : gross;

        if (taxableInput) taxableInput.value = roundBySettings(taxable);
        if (taxInput) taxInput.value = roundBySettings(taxable * taxRate / 100);
        if (employeeSocialInput) employeeSocialInput.value = roundBySettings(socialBase * employeeRate / 100);
        if (companySocialInput) companySocialInput.value = roundBySettings(socialBase * companyRate / 100);

        updateNetPreview();
    };

    el("calculateBySettingsBtn")?.addEventListener("click", calculateBySettings);

    [grossInput, taxableInput, taxInput, employeeSocialInput, companySocialInput].forEach((input) => {
        input?.addEventListener("input", updateNetPreview);
    });

    el("clearRecordBtn")?.addEventListener("click", () => {
        const ids = [
            "recordEmployeeId",
            "grossSalaryInput",
            "taxableSalaryInput",
            "taxAmountInput",
            "employeeSocialInput",
            "companySocialInput",
            "netSalaryPreview",
            "notesInput"
        ];

        ids.forEach((id) => {
            const node = el(id);
            if (node) node.value = "";
        });

        const month = el("recordPayrollMonth");
        const currentMonth = document.querySelector("input[name='PayrollMonth']")?.value;
        if (month && currentMonth) month.value = currentMonth;
    });

    document.querySelectorAll(".tax-edit-btn").forEach((button) => {
        button.addEventListener("click", () => {
            const setValue = (id, value) => {
                const node = el(id);
                if (node) node.value = value ?? "";
            };

            setValue("recordEmployeeId", button.dataset.employeeId);
            setValue("recordPayrollMonth", button.dataset.month);
            setValue("grossSalaryInput", button.dataset.gross);
            setValue("taxableSalaryInput", button.dataset.taxable);
            setValue("taxAmountInput", button.dataset.tax);
            setValue("employeeSocialInput", button.dataset.employeeSocial);
            setValue("companySocialInput", button.dataset.companySocial);
            setValue("notesInput", button.dataset.notes);

            updateNetPreview();

            document.getElementById("recordForm")?.scrollIntoView({
                behavior: "smooth",
                block: "start"
            });
        });
    });

    updateNetPreview();
})();
