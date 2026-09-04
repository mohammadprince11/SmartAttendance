namespace SmartAttendance.Web.Infrastructure.Ui;

public sealed record EmployeeGeoCountry(
    string Value,
    string Label);

public sealed record EmployeeGeoCity(
    string CountryValue,
    string Value,
    string Label);

public static class ZynoraEmployeeGeoLookups
{
    public static IReadOnlyList<EmployeeGeoCountry> Countries { get; } =
    [
        new("Iraq", "العراق"),
        new("Syria", "سوريا"),
        new("Jordan", "الأردن"),
        new("Lebanon", "لبنان"),
        new("Palestine", "فلسطين"),
        new("Saudi Arabia", "السعودية"),
        new("United Arab Emirates", "الإمارات"),
        new("Qatar", "قطر"),
        new("Kuwait", "الكويت"),
        new("Bahrain", "البحرين"),
        new("Oman", "عُمان"),
        new("Egypt", "مصر"),
        new("Turkey", "تركيا"),
        new("Iran", "إيران"),
        new("India", "الهند"),
        new("Pakistan", "باكستان"),
        new("Bangladesh", "بنغلادش"),
        new("Philippines", "الفلبين"),
        new("Nepal", "نيبال"),
        new("Other", "أخرى")
    ];

    public static IReadOnlyList<EmployeeGeoCity> Cities { get; } =
    [
        // Iraq
        new("Iraq", "Baghdad", "بغداد"),
        new("Iraq", "Basra", "البصرة"),
        new("Iraq", "Mosul", "الموصل"),
        new("Iraq", "Erbil", "أربيل"),
        new("Iraq", "Sulaymaniyah", "السليمانية"),
        new("Iraq", "Duhok", "دهوك"),
        new("Iraq", "Kirkuk", "كركوك"),
        new("Iraq", "Najaf", "النجف"),
        new("Iraq", "Karbala", "كربلاء"),
        new("Iraq", "Hilla", "الحلة"),
        new("Iraq", "Diwaniyah", "الديوانية"),
        new("Iraq", "Nasiriyah", "الناصرية"),
        new("Iraq", "Amarah", "العمارة"),
        new("Iraq", "Kut", "الكوت"),
        new("Iraq", "Ramadi", "الرمادي"),
        new("Iraq", "Fallujah", "الفلوجة"),
        new("Iraq", "Tikrit", "تكريت"),
        new("Iraq", "Samarra", "سامراء"),
        new("Iraq", "Baqubah", "بعقوبة"),
        new("Iraq", "Samawah", "السماوة"),

        // Syria
        new("Syria", "Damascus", "دمشق"),
        new("Syria", "Aleppo", "حلب"),
        new("Syria", "Homs", "حمص"),
        new("Syria", "Hama", "حماة"),
        new("Syria", "Latakia", "اللاذقية"),
        new("Syria", "Tartus", "طرطوس"),
        new("Syria", "Daraa", "درعا"),
        new("Syria", "Idlib", "إدلب"),
        new("Syria", "Raqqa", "الرقة"),
        new("Syria", "Deir ez-Zor", "دير الزور"),
        new("Syria", "Hasakah", "الحسكة"),

        // Jordan
        new("Jordan", "Amman", "عمّان"),
        new("Jordan", "Zarqa", "الزرقاء"),
        new("Jordan", "Irbid", "إربد"),
        new("Jordan", "Aqaba", "العقبة"),
        new("Jordan", "Salt", "السلط"),
        new("Jordan", "Madaba", "مادبا"),
        new("Jordan", "Karak", "الكرك"),
        new("Jordan", "Mafraq", "المفرق"),

        // Lebanon
        new("Lebanon", "Beirut", "بيروت"),
        new("Lebanon", "Tripoli", "طرابلس"),
        new("Lebanon", "Sidon", "صيدا"),
        new("Lebanon", "Tyre", "صور"),
        new("Lebanon", "Zahle", "زحلة"),
        new("Lebanon", "Baalbek", "بعلبك"),
        new("Lebanon", "Nabatieh", "النبطية"),

        // Palestine
        new("Palestine", "Jerusalem", "القدس"),
        new("Palestine", "Ramallah", "رام الله"),
        new("Palestine", "Hebron", "الخليل"),
        new("Palestine", "Nablus", "نابلس"),
        new("Palestine", "Bethlehem", "بيت لحم"),
        new("Palestine", "Jenin", "جنين"),
        new("Palestine", "Gaza", "غزة"),

        // Saudi Arabia
        new("Saudi Arabia", "Riyadh", "الرياض"),
        new("Saudi Arabia", "Jeddah", "جدة"),
        new("Saudi Arabia", "Mecca", "مكة"),
        new("Saudi Arabia", "Medina", "المدينة المنورة"),
        new("Saudi Arabia", "Dammam", "الدمام"),
        new("Saudi Arabia", "Khobar", "الخبر"),
        new("Saudi Arabia", "Taif", "الطائف"),
        new("Saudi Arabia", "Abha", "أبها"),
        new("Saudi Arabia", "Tabuk", "تبوك"),

        // UAE
        new("United Arab Emirates", "Abu Dhabi", "أبوظبي"),
        new("United Arab Emirates", "Dubai", "دبي"),
        new("United Arab Emirates", "Sharjah", "الشارقة"),
        new("United Arab Emirates", "Ajman", "عجمان"),
        new("United Arab Emirates", "Ras Al Khaimah", "رأس الخيمة"),
        new("United Arab Emirates", "Fujairah", "الفجيرة"),
        new("United Arab Emirates", "Umm Al Quwain", "أم القيوين"),
        new("United Arab Emirates", "Al Ain", "العين"),

        // Qatar
        new("Qatar", "Doha", "الدوحة"),
        new("Qatar", "Al Rayyan", "الريان"),
        new("Qatar", "Al Wakrah", "الوكرة"),
        new("Qatar", "Al Khor", "الخور"),
        new("Qatar", "Lusail", "لوسيل"),

        // Kuwait
        new("Kuwait", "Kuwait City", "مدينة الكويت"),
        new("Kuwait", "Hawalli", "حولي"),
        new("Kuwait", "Salmiya", "السالمية"),
        new("Kuwait", "Farwaniya", "الفروانية"),
        new("Kuwait", "Ahmadi", "الأحمدي"),
        new("Kuwait", "Jahra", "الجهراء"),

        // Bahrain
        new("Bahrain", "Manama", "المنامة"),
        new("Bahrain", "Muharraq", "المحرق"),
        new("Bahrain", "Riffa", "الرفاع"),
        new("Bahrain", "Isa Town", "مدينة عيسى"),
        new("Bahrain", "Hamad Town", "مدينة حمد"),

        // Oman
        new("Oman", "Muscat", "مسقط"),
        new("Oman", "Salalah", "صلالة"),
        new("Oman", "Sohar", "صحار"),
        new("Oman", "Nizwa", "نزوى"),
        new("Oman", "Sur", "صور"),
        new("Oman", "Ibri", "عبري"),

        // Egypt
        new("Egypt", "Cairo", "القاهرة"),
        new("Egypt", "Alexandria", "الإسكندرية"),
        new("Egypt", "Giza", "الجيزة"),
        new("Egypt", "Luxor", "الأقصر"),
        new("Egypt", "Aswan", "أسوان"),
        new("Egypt", "Port Said", "بورسعيد"),
        new("Egypt", "Suez", "السويس"),
        new("Egypt", "Mansoura", "المنصورة"),
        new("Egypt", "Tanta", "طنطا"),

        // Turkey
        new("Turkey", "Istanbul", "إسطنبول"),
        new("Turkey", "Ankara", "أنقرة"),
        new("Turkey", "Izmir", "إزمير"),
        new("Turkey", "Bursa", "بورصة"),
        new("Turkey", "Antalya", "أنطاليا"),
        new("Turkey", "Gaziantep", "غازي عنتاب"),
        new("Turkey", "Adana", "أضنة"),

        // Iran
        new("Iran", "Tehran", "طهران"),
        new("Iran", "Mashhad", "مشهد"),
        new("Iran", "Isfahan", "أصفهان"),
        new("Iran", "Shiraz", "شيراز"),
        new("Iran", "Tabriz", "تبريز"),
        new("Iran", "Ahvaz", "الأهواز"),
        new("Iran", "Qom", "قم"),
        new("Iran", "Kermanshah", "كرمانشاه"),

        // India
        new("India", "Delhi", "دلهي"),
        new("India", "Mumbai", "مومباي"),
        new("India", "Kolkata", "كولكاتا"),
        new("India", "Chennai", "تشيناي"),
        new("India", "Bengaluru", "بنغالورو"),
        new("India", "Hyderabad", "حيدر آباد"),
        new("India", "Pune", "بونه"),
        new("India", "Kochi", "كوتشي"),

        // Pakistan
        new("Pakistan", "Karachi", "كراتشي"),
        new("Pakistan", "Lahore", "لاهور"),
        new("Pakistan", "Islamabad", "إسلام آباد"),
        new("Pakistan", "Rawalpindi", "روالبندي"),
        new("Pakistan", "Faisalabad", "فيصل آباد"),
        new("Pakistan", "Peshawar", "بيشاور"),
        new("Pakistan", "Multan", "ملتان"),

        // Bangladesh
        new("Bangladesh", "Dhaka", "دكا"),
        new("Bangladesh", "Chattogram", "شيتاغونغ"),
        new("Bangladesh", "Khulna", "خولنا"),
        new("Bangladesh", "Rajshahi", "راجشاهي"),
        new("Bangladesh", "Sylhet", "سيلهيت"),

        // Philippines
        new("Philippines", "Manila", "مانيلا"),
        new("Philippines", "Quezon City", "كيزون سيتي"),
        new("Philippines", "Cebu City", "سيبو"),
        new("Philippines", "Davao City", "دافاو"),
        new("Philippines", "Makati", "ماكاتي"),

        // Nepal
        new("Nepal", "Kathmandu", "كاتماندو"),
        new("Nepal", "Pokhara", "بوخارا"),
        new("Nepal", "Lalitpur", "لاليتبور"),
        new("Nepal", "Biratnagar", "بيراتناغار"),
        new("Nepal", "Bharatpur", "بهاراتبور"),

        // Other
        new("Other", "Other", "أخرى")
    ];

    public static bool IsKnownCountry(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Countries.Any(
            item => string.Equals(
                item.Value,
                value.Trim(),
                StringComparison.OrdinalIgnoreCase));

    public static bool IsKnownCity(
        string? country,
        string? city) =>
        !string.IsNullOrWhiteSpace(country) &&
        !string.IsNullOrWhiteSpace(city) &&
        Cities.Any(
            item =>
                string.Equals(
                    item.CountryValue,
                    country.Trim(),
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    item.Value,
                    city.Trim(),
                    StringComparison.OrdinalIgnoreCase));
}
