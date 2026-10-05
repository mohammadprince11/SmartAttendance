using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SmartAttendance.Web.Infrastructure.Ui;

/// <summary>
/// قوائم مساعدة مشتركة لشاشات الموظف (جنسيات، بلدان، ديانات...) — تُستهلك
/// من صفحات الإنشاء والتعديل والفلاتر.
/// </summary>
public static class ZynoraEmployeeLookups
{
    public sealed record EmployeeSelectOption(
        string Value,
        string Label);

    public static IReadOnlyList<EmployeeSelectOption> PrimaryCountries { get; } =
    [
        new("Iraq", "العراق"),
        new("Syria", "سوريا"),
        new("Jordan", "الأردن"),
        new("Lebanon", "لبنان"),
        new("Saudi Arabia", "السعودية"),
        new("United Arab Emirates", "الإمارات"),
        new("Qatar", "قطر"),
        new("Kuwait", "الكويت"),
        new("Bahrain", "البحرين"),
        new("Oman", "عمان"),
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

    public static IReadOnlyList<EmployeeSelectOption> PrimaryNationalities { get; } =
    [
        new("Iraqi", "عراقي"),
        new("Syrian", "سوري"),
        new("Jordanian", "أردني"),
        new("Lebanese", "لبناني"),
        new("Saudi", "سعودي"),
        new("Emirati", "إماراتي"),
        new("Qatari", "قطري"),
        new("Kuwaiti", "كويتي"),
        new("Bahraini", "بحريني"),
        new("Omani", "عماني"),
        new("Egyptian", "مصري"),
        new("Turkish", "تركي"),
        new("Iranian", "إيراني"),
        new("Indian", "هندي"),
        new("Pakistani", "باكستاني"),
        new("Bangladeshi", "بنغلادشي"),
        new("Filipino", "فلبيني"),
        new("Nepali", "نيبالي"),
        new("Other", "أخرى")
    ];

    private static readonly IReadOnlyDictionary<string, string>
        PrimaryNationalityByCountry =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Iraq"] = "Iraqi",
                ["Syria"] = "Syrian",
                ["Jordan"] = "Jordanian",
                ["Lebanon"] = "Lebanese",
                ["Saudi Arabia"] = "Saudi",
                ["United Arab Emirates"] = "Emirati",
                ["Qatar"] = "Qatari",
                ["Kuwait"] = "Kuwaiti",
                ["Bahrain"] = "Bahraini",
                ["Oman"] = "Omani",
                ["Egypt"] = "Egyptian",
                ["Turkey"] = "Turkish",
                ["Iran"] = "Iranian",
                ["India"] = "Indian",
                ["Pakistan"] = "Pakistani",
                ["Bangladesh"] = "Bangladeshi",
                ["Philippines"] = "Filipino",
                ["Nepal"] = "Nepali",
                ["Other"] = "Other"
            };

    private static readonly IReadOnlyDictionary<string, string>
        IsoCodeToCountry = BuildIsoCodeToCountry();

    public static bool IsKnownPrimaryCountry(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        PrimaryCountries.Any(x =>
            x.Value.Equals(
                value.Trim(),
                StringComparison.OrdinalIgnoreCase));

    public static bool IsKnownPrimaryNationality(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        PrimaryNationalities.Any(x =>
            x.Value.Equals(
                value.Trim(),
                StringComparison.OrdinalIgnoreCase));

    public static string? NormalizePrimaryCountry(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        var direct = PrimaryCountries.FirstOrDefault(x =>
            x.Value.Equals(
                trimmed,
                StringComparison.OrdinalIgnoreCase));

        if (direct is not null)
        {
            return direct.Value;
        }

        var code = trimmed.ToUpperInvariant();

        if (IsoCodeToCountry.TryGetValue(code, out var country))
        {
            var supported = PrimaryCountries.FirstOrDefault(x =>
                x.Value.Equals(
                    country,
                    StringComparison.OrdinalIgnoreCase));

            return supported?.Value ?? "Other";
        }

        return "Other";
    }

    public static string? NormalizePrimaryNationality(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        var direct = PrimaryNationalities.FirstOrDefault(x =>
            x.Value.Equals(
                trimmed,
                StringComparison.OrdinalIgnoreCase));

        if (direct is not null)
        {
            return direct.Value;
        }

        var country = NormalizePrimaryCountry(trimmed);
        if (!string.IsNullOrWhiteSpace(country) &&
            PrimaryNationalityByCountry.TryGetValue(
                country,
                out var nationality))
        {
            return nationality;
        }

        return "Other";
    }

    private static IReadOnlyDictionary<string, string>
        BuildIsoCodeToCountry()
    {
        var map = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var culture in CultureInfo.GetCultures(
                     CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);

                map.TryAdd(
                    region.TwoLetterISORegionName,
                    region.EnglishName);
                map.TryAdd(
                    region.ThreeLetterISORegionName,
                    region.EnglishName);
            }
            catch (ArgumentException)
            {
                // Ignore incomplete/custom cultures.
            }
        }

        // Keep employee dropdown values authoritative for common aliases.
        map["IRQ"] = "Iraq";
        map["JOR"] = "Jordan";
        map["SYR"] = "Syria";
        map["LBN"] = "Lebanon";
        map["SAU"] = "Saudi Arabia";
        map["ARE"] = "United Arab Emirates";
        map["QAT"] = "Qatar";
        map["KWT"] = "Kuwait";
        map["BHR"] = "Bahrain";
        map["OMN"] = "Oman";
        map["EGY"] = "Egypt";
        map["TUR"] = "Turkey";
        map["IRN"] = "Iran";
        map["IND"] = "India";
        map["PAK"] = "Pakistan";
        map["BGD"] = "Bangladesh";
        map["PHL"] = "Philippines";
        map["NPL"] = "Nepal";
        map["USA"] = "United States";
        map["GBR"] = "United Kingdom";

        return map;
    }

    public static IReadOnlyList<string> Countries { get; } = new[]
    {
        "Afghanistan",
        "Albania",
        "Algeria",
        "Andorra",
        "Angola",
        "Antigua and Barbuda",
        "Argentina",
        "Armenia",
        "Australia",
        "Austria",
        "Azerbaijan",
        "Bahamas",
        "Bahrain",
        "Bangladesh",
        "Barbados",
        "Belarus",
        "Belgium",
        "Belize",
        "Benin",
        "Bhutan",
        "Bolivia",
        "Bosnia and Herzegovina",
        "Botswana",
        "Brazil",
        "Brunei",
        "Bulgaria",
        "Burkina Faso",
        "Burundi",
        "Cabo Verde",
        "Cambodia",
        "Cameroon",
        "Canada",
        "Central African Republic",
        "Chad",
        "Chile",
        "China",
        "Colombia",
        "Comoros",
        "Congo",
        "Costa Rica",
        "Cote d'Ivoire",
        "Croatia",
        "Cuba",
        "Cyprus",
        "Czechia",
        "Democratic Republic of the Congo",
        "Denmark",
        "Djibouti",
        "Dominica",
        "Dominican Republic",
        "Ecuador",
        "Egypt",
        "El Salvador",
        "Equatorial Guinea",
        "Eritrea",
        "Estonia",
        "Eswatini",
        "Ethiopia",
        "Fiji",
        "Finland",
        "France",
        "Gabon",
        "Gambia",
        "Georgia",
        "Germany",
        "Ghana",
        "Greece",
        "Grenada",
        "Guatemala",
        "Guinea",
        "Guinea-Bissau",
        "Guyana",
        "Haiti",
        "Honduras",
        "Hungary",
        "Iceland",
        "India",
        "Indonesia",
        "Iran",
        "Iraq",
        "Ireland",
        "Israel",
        "Italy",
        "Jamaica",
        "Japan",
        "Jordan",
        "Kazakhstan",
        "Kenya",
        "Kiribati",
        "Kuwait",
        "Kyrgyzstan",
        "Laos",
        "Latvia",
        "Lebanon",
        "Lesotho",
        "Liberia",
        "Libya",
        "Liechtenstein",
        "Lithuania",
        "Luxembourg",
        "Madagascar",
        "Malawi",
        "Malaysia",
        "Maldives",
        "Mali",
        "Malta",
        "Marshall Islands",
        "Mauritania",
        "Mauritius",
        "Mexico",
        "Micronesia",
        "Moldova",
        "Monaco",
        "Mongolia",
        "Montenegro",
        "Morocco",
        "Mozambique",
        "Myanmar",
        "Namibia",
        "Nauru",
        "Nepal",
        "Netherlands",
        "New Zealand",
        "Nicaragua",
        "Niger",
        "Nigeria",
        "North Korea",
        "North Macedonia",
        "Norway",
        "Oman",
        "Pakistan",
        "Palau",
        "Palestine",
        "Panama",
        "Papua New Guinea",
        "Paraguay",
        "Peru",
        "Philippines",
        "Poland",
        "Portugal",
        "Qatar",
        "Romania",
        "Russia",
        "Rwanda",
        "Saint Kitts and Nevis",
        "Saint Lucia",
        "Saint Vincent and the Grenadines",
        "Samoa",
        "San Marino",
        "Sao Tome and Principe",
        "Saudi Arabia",
        "Senegal",
        "Serbia",
        "Seychelles",
        "Sierra Leone",
        "Singapore",
        "Slovakia",
        "Slovenia",
        "Solomon Islands",
        "Somalia",
        "South Africa",
        "South Korea",
        "South Sudan",
        "Spain",
        "Sri Lanka",
        "Sudan",
        "Suriname",
        "Sweden",
        "Switzerland",
        "Syria",
        "Tajikistan",
        "Tanzania",
        "Thailand",
        "Timor-Leste",
        "Togo",
        "Tonga",
        "Trinidad and Tobago",
        "Tunisia",
        "Turkey",
        "Turkmenistan",
        "Tuvalu",
        "Uganda",
        "Ukraine",
        "United Arab Emirates",
        "United Kingdom",
        "United States",
        "Uruguay",
        "Uzbekistan",
        "Vanuatu",
        "Vatican City",
        "Venezuela",
        "Vietnam",
        "Yemen",
        "Zambia",
        "Zimbabwe"
    };

    public static IReadOnlyList<string> Nationalities { get; } = new[]
    {
        "Afghan",
        "Albanian",
        "Algerian",
        "Andorran",
        "Angolan",
        "Antiguan and Barbudan",
        "Argentine",
        "Armenian",
        "Australian",
        "Austrian",
        "Azerbaijani",
        "Bahamian",
        "Bahraini",
        "Bangladeshi",
        "Barbadian",
        "Belarusian",
        "Belgian",
        "Belizean",
        "Beninese",
        "Bhutanese",
        "Bolivian",
        "Bosnian and Herzegovinian",
        "Botswanan",
        "Brazilian",
        "Bruneian",
        "Bulgarian",
        "Burkinabe",
        "Burundian",
        "Cabo Verdean",
        "Cambodian",
        "Cameroonian",
        "Canadian",
        "Central African",
        "Chadian",
        "Chilean",
        "Chinese",
        "Colombian",
        "Comorian",
        "Congolese",
        "Costa Rican",
        "Ivorian",
        "Croatian",
        "Cuban",
        "Cypriot",
        "Czech",
        "Danish",
        "Djiboutian",
        "Dominican",
        "Dominican Republic",
        "Ecuadorian",
        "Egyptian",
        "Salvadoran",
        "Equatorial Guinean",
        "Eritrean",
        "Estonian",
        "Eswatini",
        "Ethiopian",
        "Fijian",
        "Finnish",
        "French",
        "Gabonese",
        "Gambian",
        "Georgian",
        "German",
        "Ghanaian",
        "Greek",
        "Grenadian",
        "Guatemalan",
        "Guinean",
        "Bissau-Guinean",
        "Guyanese",
        "Haitian",
        "Honduran",
        "Hungarian",
        "Icelandic",
        "Indian",
        "Indonesian",
        "Iranian",
        "Iraqi",
        "Irish",
        "Israeli",
        "Italian",
        "Jamaican",
        "Japanese",
        "Jordanian",
        "Kazakhstani",
        "Kenyan",
        "I-Kiribati",
        "Kuwaiti",
        "Kyrgyzstani",
        "Lao",
        "Latvian",
        "Lebanese",
        "Mosotho",
        "Liberian",
        "Libyan",
        "Liechtensteiner",
        "Lithuanian",
        "Luxembourger",
        "Malagasy",
        "Malawian",
        "Malaysian",
        "Maldivian",
        "Malian",
        "Maltese",
        "Marshallese",
        "Mauritanian",
        "Mauritian",
        "Mexican",
        "Micronesian",
        "Moldovan",
        "Monegasque",
        "Mongolian",
        "Montenegrin",
        "Moroccan",
        "Mozambican",
        "Burmese",
        "Namibian",
        "Nauruan",
        "Nepali",
        "Dutch",
        "New Zealander",
        "Nicaraguan",
        "Nigerien",
        "Nigerian",
        "North Korean",
        "Macedonian",
        "Norwegian",
        "Omani",
        "Pakistani",
        "Palauan",
        "Palestinian",
        "Panamanian",
        "Papua New Guinean",
        "Paraguayan",
        "Peruvian",
        "Filipino",
        "Polish",
        "Portuguese",
        "Qatari",
        "Romanian",
        "Russian",
        "Rwandan",
        "Kittitian and Nevisian",
        "Saint Lucian",
        "Vincentian",
        "Samoan",
        "Sammarinese",
        "Sao Tomean",
        "Saudi",
        "Senegalese",
        "Serbian",
        "Seychellois",
        "Sierra Leonean",
        "Singaporean",
        "Slovak",
        "Slovenian",
        "Solomon Islander",
        "Somali",
        "South African",
        "South Korean",
        "South Sudanese",
        "Spanish",
        "Sri Lankan",
        "Sudanese",
        "Surinamese",
        "Swedish",
        "Swiss",
        "Syrian",
        "Tajikistani",
        "Tanzanian",
        "Thai",
        "Timorese",
        "Togolese",
        "Tongan",
        "Trinidadian and Tobagonian",
        "Tunisian",
        "Turkish",
        "Turkmen",
        "Tuvaluan",
        "Ugandan",
        "Ukrainian",
        "Emirati",
        "British",
        "American",
        "Uruguayan",
        "Uzbekistani",
        "Ni-Vanuatu",
        "Vatican",
        "Venezuelan",
        "Vietnamese",
        "Yemeni",
        "Zambian",
        "Zimbabwean"
    };

    public static bool IsKnownCountry(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               Countries.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsKnownNationality(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               Nationalities.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}