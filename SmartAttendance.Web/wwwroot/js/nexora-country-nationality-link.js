(function () {
    "use strict";

    const pairs = [
        ["Afghanistan", "Afghan"],
        ["Albania", "Albanian"],
        ["Algeria", "Algerian"],
        ["Andorra", "Andorran"],
        ["Angola", "Angolan"],
        ["Antigua and Barbuda", "Antiguan and Barbudan"],
        ["Argentina", "Argentine"],
        ["Armenia", "Armenian"],
        ["Australia", "Australian"],
        ["Austria", "Austrian"],
        ["Azerbaijan", "Azerbaijani"],
        ["Bahamas", "Bahamian"],
        ["Bahrain", "Bahraini"],
        ["Bangladesh", "Bangladeshi"],
        ["Barbados", "Barbadian"],
        ["Belarus", "Belarusian"],
        ["Belgium", "Belgian"],
        ["Belize", "Belizean"],
        ["Benin", "Beninese"],
        ["Bhutan", "Bhutanese"],
        ["Bolivia", "Bolivian"],
        ["Bosnia and Herzegovina", "Bosnian and Herzegovinian"],
        ["Botswana", "Botswanan"],
        ["Brazil", "Brazilian"],
        ["Brunei", "Bruneian"],
        ["Bulgaria", "Bulgarian"],
        ["Burkina Faso", "Burkinabe"],
        ["Burundi", "Burundian"],
        ["Cabo Verde", "Cabo Verdean"],
        ["Cambodia", "Cambodian"],
        ["Cameroon", "Cameroonian"],
        ["Canada", "Canadian"],
        ["Central African Republic", "Central African"],
        ["Chad", "Chadian"],
        ["Chile", "Chilean"],
        ["China", "Chinese"],
        ["Colombia", "Colombian"],
        ["Comoros", "Comorian"],
        ["Congo", "Congolese"],
        ["Costa Rica", "Costa Rican"],
        ["Cote d'Ivoire", "Ivorian"],
        ["Croatia", "Croatian"],
        ["Cuba", "Cuban"],
        ["Cyprus", "Cypriot"],
        ["Czechia", "Czech"],
        ["Democratic Republic of the Congo", "Congolese"],
        ["Denmark", "Danish"],
        ["Djibouti", "Djiboutian"],
        ["Dominica", "Dominican"],
        ["Dominican Republic", "Dominican Republic"],
        ["Ecuador", "Ecuadorian"],
        ["Egypt", "Egyptian"],
        ["El Salvador", "Salvadoran"],
        ["Equatorial Guinea", "Equatorial Guinean"],
        ["Eritrea", "Eritrean"],
        ["Estonia", "Estonian"],
        ["Eswatini", "Eswatini"],
        ["Ethiopia", "Ethiopian"],
        ["Fiji", "Fijian"],
        ["Finland", "Finnish"],
        ["France", "French"],
        ["Gabon", "Gabonese"],
        ["Gambia", "Gambian"],
        ["Georgia", "Georgian"],
        ["Germany", "German"],
        ["Ghana", "Ghanaian"],
        ["Greece", "Greek"],
        ["Grenada", "Grenadian"],
        ["Guatemala", "Guatemalan"],
        ["Guinea", "Guinean"],
        ["Guinea-Bissau", "Bissau-Guinean"],
        ["Guyana", "Guyanese"],
        ["Haiti", "Haitian"],
        ["Honduras", "Honduran"],
        ["Hungary", "Hungarian"],
        ["Iceland", "Icelandic"],
        ["India", "Indian"],
        ["Indonesia", "Indonesian"],
        ["Iran", "Iranian"],
        ["Iraq", "Iraqi"],
        ["Ireland", "Irish"],
        ["Israel", "Israeli"],
        ["Italy", "Italian"],
        ["Jamaica", "Jamaican"],
        ["Japan", "Japanese"],
        ["Jordan", "Jordanian"],
        ["Kazakhstan", "Kazakhstani"],
        ["Kenya", "Kenyan"],
        ["Kiribati", "I-Kiribati"],
        ["Kuwait", "Kuwaiti"],
        ["Kyrgyzstan", "Kyrgyzstani"],
        ["Laos", "Lao"],
        ["Latvia", "Latvian"],
        ["Lebanon", "Lebanese"],
        ["Lesotho", "Mosotho"],
        ["Liberia", "Liberian"],
        ["Libya", "Libyan"],
        ["Liechtenstein", "Liechtensteiner"],
        ["Lithuania", "Lithuanian"],
        ["Luxembourg", "Luxembourger"],
        ["Madagascar", "Malagasy"],
        ["Malawi", "Malawian"],
        ["Malaysia", "Malaysian"],
        ["Maldives", "Maldivian"],
        ["Mali", "Malian"],
        ["Malta", "Maltese"],
        ["Marshall Islands", "Marshallese"],
        ["Mauritania", "Mauritanian"],
        ["Mauritius", "Mauritian"],
        ["Mexico", "Mexican"],
        ["Micronesia", "Micronesian"],
        ["Moldova", "Moldovan"],
        ["Monaco", "Monegasque"],
        ["Mongolia", "Mongolian"],
        ["Montenegro", "Montenegrin"],
        ["Morocco", "Moroccan"],
        ["Mozambique", "Mozambican"],
        ["Myanmar", "Burmese"],
        ["Namibia", "Namibian"],
        ["Nauru", "Nauruan"],
        ["Nepal", "Nepali"],
        ["Netherlands", "Dutch"],
        ["New Zealand", "New Zealander"],
        ["Nicaragua", "Nicaraguan"],
        ["Niger", "Nigerien"],
        ["Nigeria", "Nigerian"],
        ["North Korea", "North Korean"],
        ["North Macedonia", "Macedonian"],
        ["Norway", "Norwegian"],
        ["Oman", "Omani"],
        ["Pakistan", "Pakistani"],
        ["Palau", "Palauan"],
        ["Palestine", "Palestinian"],
        ["Panama", "Panamanian"],
        ["Papua New Guinea", "Papua New Guinean"],
        ["Paraguay", "Paraguayan"],
        ["Peru", "Peruvian"],
        ["Philippines", "Filipino"],
        ["Poland", "Polish"],
        ["Portugal", "Portuguese"],
        ["Qatar", "Qatari"],
        ["Romania", "Romanian"],
        ["Russia", "Russian"],
        ["Rwanda", "Rwandan"],
        ["Saint Kitts and Nevis", "Kittitian and Nevisian"],
        ["Saint Lucia", "Saint Lucian"],
        ["Saint Vincent and the Grenadines", "Vincentian"],
        ["Samoa", "Samoan"],
        ["San Marino", "Sammarinese"],
        ["Sao Tome and Principe", "Sao Tomean"],
        ["Saudi Arabia", "Saudi"],
        ["Senegal", "Senegalese"],
        ["Serbia", "Serbian"],
        ["Seychelles", "Seychellois"],
        ["Sierra Leone", "Sierra Leonean"],
        ["Singapore", "Singaporean"],
        ["Slovakia", "Slovak"],
        ["Slovenia", "Slovenian"],
        ["Solomon Islands", "Solomon Islander"],
        ["Somalia", "Somali"],
        ["South Africa", "South African"],
        ["South Korea", "South Korean"],
        ["South Sudan", "South Sudanese"],
        ["Spain", "Spanish"],
        ["Sri Lanka", "Sri Lankan"],
        ["Sudan", "Sudanese"],
        ["Suriname", "Surinamese"],
        ["Sweden", "Swedish"],
        ["Switzerland", "Swiss"],
        ["Syria", "Syrian"],
        ["Tajikistan", "Tajikistani"],
        ["Tanzania", "Tanzanian"],
        ["Thailand", "Thai"],
        ["Timor-Leste", "Timorese"],
        ["Togo", "Togolese"],
        ["Tonga", "Tongan"],
        ["Trinidad and Tobago", "Trinidadian and Tobagonian"],
        ["Tunisia", "Tunisian"],
        ["Turkey", "Turkish"],
        ["Turkmenistan", "Turkmen"],
        ["Tuvalu", "Tuvaluan"],
        ["Uganda", "Ugandan"],
        ["Ukraine", "Ukrainian"],
        ["United Arab Emirates", "Emirati"],
        ["United Kingdom", "British"],
        ["United States", "American"],
        ["Uruguay", "Uruguayan"],
        ["Uzbekistan", "Uzbekistani"],
        ["Vanuatu", "Ni-Vanuatu"],
        ["Vatican City", "Vatican"],
        ["Venezuela", "Venezuelan"],
        ["Vietnam", "Vietnamese"],
        ["Yemen", "Yemeni"],
        ["Zambia", "Zambian"],
        ["Zimbabwe", "Zimbabwean"]
    ];

    const countryByNationality = new Map();
    const nationalityByCountry = new Map();

    pairs.forEach(pair => {
        const country = pair[0];
        const nationality = pair[1];

        countryByNationality.set(nationality.toLowerCase(), country);
        nationalityByCountry.set(country.toLowerCase(), nationality);
    });

    let internalChange = false;

    function findSelectBySuffix(suffix) {
        return (
            document.getElementById("Employee_" + suffix) ||
            document.querySelector('select[name="Employee.' + suffix + '"]') ||
            document.querySelector('select[id$="_' + suffix + '"]')
        );
    }

    function normalize(value) {
        return String(value || "").trim();
    }

    function hasOption(select, value) {
        if (!select || !value) {
            return false;
        }

        return Array.from(select.options).some(option => normalize(option.value) === value);
    }

    function refreshSelect(select) {
        if (!select) {
            return;
        }

        if (window.NexoraSelect && typeof window.NexoraSelect.refresh === "function") {
            window.NexoraSelect.refresh(select);
        }

        if (window.NexoraSelect && typeof window.NexoraSelect.refreshAll === "function") {
            window.NexoraSelect.refreshAll();
        }
    }

    function setSelectValue(select, value) {
        if (!select || !value || !hasOption(select, value)) {
            return;
        }

        if (select.value === value) {
            refreshSelect(select);
            return;
        }

        internalChange = true;
        select.value = value;

        select.dispatchEvent(new Event("input", { bubbles: true }));
        select.dispatchEvent(new Event("change", { bubbles: true }));

        refreshSelect(select);
        internalChange = false;
    }

    function linkFromNationality() {
        if (internalChange) {
            return;
        }

        const nationalitySelect = findSelectBySuffix("Nationality");
        const countrySelect = findSelectBySuffix("Country");

        if (!nationalitySelect || !countrySelect) {
            return;
        }

        const nationality = normalize(nationalitySelect.value);
        const country = countryByNationality.get(nationality.toLowerCase());

        if (country) {
            setSelectValue(countrySelect, country);
        }
    }

    function linkFromCountry() {
        if (internalChange) {
            return;
        }

        const nationalitySelect = findSelectBySuffix("Nationality");
        const countrySelect = findSelectBySuffix("Country");

        if (!nationalitySelect || !countrySelect) {
            return;
        }

        const country = normalize(countrySelect.value);
        const nationality = nationalityByCountry.get(country.toLowerCase());

        if (nationality) {
            setSelectValue(nationalitySelect, nationality);
        }
    }

    function bind() {
        const nationalitySelect = findSelectBySuffix("Nationality");
        const countrySelect = findSelectBySuffix("Country");

        if (!nationalitySelect || !countrySelect) {
            return;
        }

        if (!nationalitySelect.dataset.nexoraCountryNationalityExactBound) {
            nationalitySelect.dataset.nexoraCountryNationalityExactBound = "1";
            nationalitySelect.addEventListener("change", linkFromNationality);
            nationalitySelect.addEventListener("input", linkFromNationality);
        }

        if (!countrySelect.dataset.nexoraCountryNationalityExactBound) {
            countrySelect.dataset.nexoraCountryNationalityExactBound = "1";
            countrySelect.addEventListener("change", linkFromCountry);
            countrySelect.addEventListener("input", linkFromCountry);
        }

        refreshSelect(countrySelect);
        refreshSelect(nationalitySelect);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", bind);
    } else {
        bind();
    }

    setTimeout(bind, 250);
    setTimeout(bind, 800);
    setTimeout(bind, 1500);

    window.NexoraCountryNationality = {
        bind: bind,
        linkFromCountry: linkFromCountry,
        linkFromNationality: linkFromNationality
    };
})();