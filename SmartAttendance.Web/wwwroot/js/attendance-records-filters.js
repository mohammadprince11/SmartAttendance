(function () {
    const dictionary = {
        ar: {
            title: "سجلات الحضور",
            subtitle: "إدارة سجلات دخول وخروج الموظفين مع فلاتر متقدمة.",
            addRecord: "إضافة سجل",
            filters: "فلاتر البحث",
            filtersHint: "ابحث حسب رقم الموظف، الاسم، التاريخ، القسم، الفرع، المنصب، الحالة والمصدر.",
            employeeNo: "رقم الموظف",
            employeeName: "اسم الموظف",
            fromDate: "من تاريخ",
            toDate: "إلى تاريخ",
            branch: "الفرع",
            department: "القسم",
            position: "المنصب",
            status: "الحالة",
            source: "المصدر",
            rowsPerPage: "عدد الصفوف",
            generalSearch: "بحث عام",
            all: "الكل",
            present: "حاضر",
            late: "متأخر",
            absent: "غائب",
            leave: "إجازة",
            holiday: "عطلة",
            device: "جهاز",
            mobile: "موبايل",
            manual: "يدوي",
            apply: "تطبيق",
            clear: "مسح",
            displayData: "عرض البيانات",
            of: "من",
            previous: "السابق",
            next: "التالي",
            actions: "الإجراءات",
            date: "التاريخ",
            checkIn: "دخول",
            checkOut: "خروج",
            edit: "تعديل",
            delete: "حذف",
            noData: "لا توجد بيانات حسب الفلاتر المحددة.",
            employeeNoPlaceholder: "مثال: 11230",
            employeeNamePlaceholder: "اسم الموظف...",
            generalSearchPlaceholder: "بحث عام بالموظف، القسم، الفرع، الجهاز أو الملاحظات...",
            positionNotAvailable: "يضاف بعد إضافة حقل المنصب للموظفين"
        },
        en: {
            title: "Attendance Records",
            subtitle: "Manage employee check-in and check-out records with advanced filters.",
            addRecord: "Add Record",
            filters: "Search Filters",
            filtersHint: "Search by employee number, name, date, department, branch, position, status, and source.",
            employeeNo: "Employee No",
            employeeName: "Employee Name",
            fromDate: "From Date",
            toDate: "To Date",
            branch: "Branch",
            department: "Department",
            position: "Position",
            status: "Status",
            source: "Source",
            rowsPerPage: "Rows",
            generalSearch: "General Search",
            all: "All",
            present: "Present",
            late: "Late",
            absent: "Absent",
            leave: "Leave",
            holiday: "Holiday",
            device: "Device",
            mobile: "Mobile",
            manual: "Manual",
            apply: "Apply",
            clear: "Clear",
            displayData: "Display Data",
            of: "of",
            previous: "Previous",
            next: "Next",
            actions: "Actions",
            date: "Date",
            checkIn: "Check In",
            checkOut: "Check Out",
            edit: "Edit",
            delete: "Delete",
            noData: "No records found with selected filters.",
            employeeNoPlaceholder: "Example: 11230",
            employeeNamePlaceholder: "Employee name...",
            generalSearchPlaceholder: "General search by employee, department, branch, device, or notes...",
            positionNotAvailable: "Available after adding Position field to Employees"
        }
    };

    function lang() {
        return document.documentElement.getAttribute("lang") === "ar" ? "ar" : "en";
    }

    function translate() {
        const current = dictionary[lang()];

        document.querySelectorAll("[data-att-text]").forEach(function (element) {
            const key = element.getAttribute("data-att-text");
            if (current[key]) {
                element.textContent = current[key];
            }
        });

        document.querySelectorAll("[data-att-placeholder]").forEach(function (element) {
            const key = element.getAttribute("data-att-placeholder");
            if (current[key]) {
                element.placeholder = current[key];
            }
        });

        document.querySelectorAll("[data-att-value]").forEach(function (element) {
            const key = element.getAttribute("data-att-value");
            if (current[key]) {
                element.value = current[key];
            }
        });
    }

    function init() {
        translate();

        const observer = new MutationObserver(translate);
        observer.observe(document.documentElement, {
            attributes: true,
            attributeFilter: ["lang"]
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
