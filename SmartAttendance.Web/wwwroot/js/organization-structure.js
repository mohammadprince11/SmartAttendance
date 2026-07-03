(function () {
    const dictionary = {
        ar: {
            title: "الهيكل التنظيمي",
            subtitle: "عرض مبسط لهيكل الشركة والفروع والأقسام وعدد الموظفين",
            addCompany: "إضافة شركة",
            addBranch: "إضافة فرع",
            addDepartment: "إضافة قسم",
            companies: "الشركات",
            branches: "الفروع",
            departments: "الأقسام",
            employees: "الموظفون",
            active: "فعال",
            inactive: "غير فعال",
            searchPlaceholder: "ابحث عن شركة أو فرع أو قسم...",
            expandDepartments: "فتح الأقسام",
            collapseDepartments: "إغلاق الأقسام",
            branch: "الفرع",
            status: "الحالة",
            actions: "الإجراءات",
            branchesSmall: "فروع",
            departmentsSmall: "أقسام",
            employeesSmall: "موظفون",
            edit: "تعديل",
            delete: "حذف",
            noDepartments: "لا توجد أقسام ضمن هذا الفرع.",
            noBranches: "لا توجد فروع ضمن هذه الشركة.",
            noCompanies: "لا توجد شركات.",
            unlinkedBranches: "فروع غير مرتبطة بشركة"
        },
        en: {
            title: "Organization",
            subtitle: "Simple structure view: companies, branches, departments, and employee counts",
            addCompany: "Add Company",
            addBranch: "Add Branch",
            addDepartment: "Add Department",
            companies: "Companies",
            branches: "Branches",
            departments: "Departments",
            employees: "Employees",
            active: "active",
            inactive: "inactive",
            searchPlaceholder: "Search company, branch, department...",
            expandDepartments: "Expand Departments",
            collapseDepartments: "Collapse Departments",
            branch: "Branch",
            status: "Status",
            actions: "Actions",
            branchesSmall: "branches",
            departmentsSmall: "departments",
            employeesSmall: "employees",
            edit: "Edit",
            delete: "Delete",
            noDepartments: "No departments under this branch.",
            noBranches: "No branches under this company.",
            noCompanies: "No companies found.",
            unlinkedBranches: "Branches Without Company"
        }
    };

    function getLanguage() {
        return document.documentElement.getAttribute("lang") === "ar" ? "ar" : "en";
    }

    function translateOrganization() {
        const lang = getLanguage();
        const map = dictionary[lang];

        document.querySelectorAll("[data-org-i18n]").forEach(function (element) {
            const key = element.getAttribute("data-org-i18n");

            if (map[key]) {
                element.textContent = map[key];
            }
        });

        document.querySelectorAll("[data-org-placeholder]").forEach(function (element) {
            const key = element.getAttribute("data-org-placeholder");

            if (map[key]) {
                element.placeholder = map[key];
            }
        });
    }

    window.expandAllBranches = function () {
        document.querySelectorAll(".branch-directory-row").forEach(function (node) {
            node.open = true;
        });
    };

    window.collapseAllBranches = function () {
        document.querySelectorAll(".branch-directory-row").forEach(function (node) {
            node.open = false;
        });
    };

    window.filterOrganization = function (value) {
        const term = (value || "").trim().toLowerCase();

        document.querySelectorAll(".company-directory-section, .branch-directory-row, .department-compact-item").forEach(function (node) {
            node.hidden = false;
        });

        if (!term) {
            return;
        }

        document.querySelectorAll(".company-directory-section").forEach(function (company) {
            const companyText = (company.getAttribute("data-org-search") || company.textContent || "").toLowerCase();
            let companyHasMatch = companyText.includes(term);

            company.querySelectorAll(".branch-directory-row").forEach(function (branch) {
                const branchText = (branch.getAttribute("data-org-search") || branch.textContent || "").toLowerCase();
                let branchHasMatch = branchText.includes(term);

                branch.querySelectorAll(".department-compact-item").forEach(function (department) {
                    const departmentText = (department.getAttribute("data-org-search") || department.textContent || "").toLowerCase();
                    const departmentHasMatch = departmentText.includes(term);

                    department.hidden = !departmentHasMatch && !branchHasMatch && !companyText.includes(term);

                    if (departmentHasMatch) {
                        branchHasMatch = true;
                        companyHasMatch = true;
                        branch.open = true;
                    }
                });

                branch.hidden = !branchHasMatch && !companyText.includes(term);

                if (branchHasMatch) {
                    companyHasMatch = true;
                }
            });

            company.hidden = !companyHasMatch;
        });
    };

    translateOrganization();

    const observer = new MutationObserver(translateOrganization);
    observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ["lang"]
    });
})();
