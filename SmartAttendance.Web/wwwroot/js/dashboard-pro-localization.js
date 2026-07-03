(function () {
    const dictionary = {
        ar: {
            dashboardTitle: "لوحة التحكم",
            overviewFor: "نظرة عامة على النظام بتاريخ",
            totalEmployees: "إجمالي الموظفين",
            activeEmployees: "موظف فعال",
            attendanceRate: "نسبة الحضور",
            expectedToday: "متوقع اليوم",
            lateToday: "المتأخرون اليوم",
            lateBasedOnRecords: "حسب سجلات اليوم",
            missingCheckout: "عدم وجود خروج",
            todayRecordsWithoutOut: "سجلات اليوم بدون خروج",
            newHires: "تعيينات هذا الشهر",
            basedOnHireDate: "حسب تاريخ المباشرة",
            turnover: "مؤشر الدوران",
            withoutShift: "موظفون بدون شفت",
            activeEmployeesWithoutCurrentShift: "موظفون فعالون بدون شفت حالي",
            attendanceRecords: "سجلات الحضور",
            rawRecords: "سجلات الحضور الخام",
            companies: "الشركات",
            branches: "الفروع",
            departments: "الأقسام",
            devices: "الأجهزة",
            active: "فعال",
            shifts: "الشفتات",
            onLeaveToday: "إجازات اليوم",
            holidayToday: "عطلة اليوم",
            inactiveEmployees: "موظفون غير فعالين",
            employeesByBranch: "الموظفون حسب الفرع",
            activeDistribution: "توزيع الموظفين الفعالين",
            genderDistribution: "توزيع الجنس",
            requiresGenderField: "يعتمد على حقل Gender إذا كان موجوداً",
            countryDistribution: "البلد / الجنسية",
            requiresCountryField: "يعتمد على حقل Country/Nationality إذا كان موجوداً",
            recentAttendance: "آخر سجلات الحضور",
            latestTenRecords: "آخر 10 سجلات حضور",
            employeeNo: "رقم الموظف",
            employeeName: "اسم الموظف",
            date: "التاريخ",
            checkIn: "دخول",
            checkOut: "خروج",
            status: "الحالة",
            source: "المصدر",
            noAttendanceRecords: "لا توجد سجلات حضور.",
            currentLeaves: "الإجازات الحالية",
            approvedLeavesToday: "الإجازات الموافق عليها اليوم",
            employee: "الموظف",
            type: "النوع",
            period: "الفترة",
            noEmployeesOnLeave: "لا يوجد موظفون في إجازة اليوم.",
            upcomingHolidays: "العطل القادمة",
            nextOfficialHolidays: "العطل الرسمية القادمة",
            holiday: "العطلة",
            recurring: "متكررة",
            noUpcomingHolidays: "لا توجد عطل قادمة."
        },
        en: {
            dashboardTitle: "Dashboard",
            overviewFor: "SmartAttendance overview for",
            totalEmployees: "Total Employees",
            activeEmployees: "active employees",
            attendanceRate: "Attendance Rate",
            expectedToday: "expected today",
            lateToday: "Late Today",
            lateBasedOnRecords: "Based on today's records",
            missingCheckout: "Missing Check-Out",
            todayRecordsWithoutOut: "Today records without check-out",
            newHires: "New Hires This Month",
            basedOnHireDate: "Based on hire date",
            turnover: "Turnover Indicator",
            withoutShift: "Employees Without Shift",
            activeEmployeesWithoutCurrentShift: "Active employees without current shift",
            attendanceRecords: "Attendance Records",
            rawRecords: "Raw attendance records",
            companies: "Companies",
            branches: "Branches",
            departments: "Departments",
            devices: "Devices",
            active: "active",
            shifts: "Shifts",
            onLeaveToday: "On Leave Today",
            holidayToday: "Holiday Today",
            inactiveEmployees: "Inactive Employees",
            employeesByBranch: "Employees by Branch",
            activeDistribution: "Active distribution",
            genderDistribution: "Gender Distribution",
            requiresGenderField: "Uses Gender field if available",
            countryDistribution: "Country / Nationality",
            requiresCountryField: "Uses Country/Nationality field if available",
            recentAttendance: "Recent Attendance",
            latestTenRecords: "Latest 10 attendance records",
            employeeNo: "Employee No",
            employeeName: "Employee Name",
            date: "Date",
            checkIn: "Check In",
            checkOut: "Check Out",
            status: "Status",
            source: "Source",
            noAttendanceRecords: "No attendance records found.",
            currentLeaves: "Current Leaves",
            approvedLeavesToday: "Approved leaves for today",
            employee: "Employee",
            type: "Type",
            period: "Period",
            noEmployeesOnLeave: "No employees on leave today.",
            upcomingHolidays: "Upcoming Holidays",
            nextOfficialHolidays: "Next official holidays",
            holiday: "Holiday",
            recurring: "Recurring",
            noUpcomingHolidays: "No upcoming holidays."
        }
    };

    function applyDashboardLanguage() {
        const lang = document.documentElement.getAttribute("lang") === "ar" ? "ar" : "en";
        const map = dictionary[lang];

        document.querySelectorAll("[data-dashboard-i18n]").forEach(function (element) {
            const key = element.getAttribute("data-dashboard-i18n");
            if (map[key]) {
                element.textContent = map[key];
            }
        });
    }

    applyDashboardLanguage();

    const observer = new MutationObserver(applyDashboardLanguage);
    observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ["lang"]
    });
})();
