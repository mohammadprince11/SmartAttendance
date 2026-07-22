(function () {
    "use strict";

    var wrap = document.querySelector(".ar-wrap");
    if (!wrap) {
        return;
    }

    var users = [];
    var assigned = {};
    var grants = {};
    try { users = JSON.parse(wrap.getAttribute("data-users") || "[]"); } catch (e) { users = []; }
    try { assigned = JSON.parse(wrap.getAttribute("data-assigned") || "{}"); } catch (e) { assigned = {}; }
    try { grants = JSON.parse(wrap.getAttribute("data-grants") || "{}"); } catch (e) { grants = {}; }

    var modal = document.getElementById("ar-modal");
    var usersBox = document.getElementById("ar-users");

    // Pre-check the page-permission tree from a role's stored grants.
    function applyGrants(roleGrants) {
        var tree = document.querySelector(".ar-tree");
        if (!tree) { return; }
        var map = roleGrants || {};
        tree.querySelectorAll("input[type=checkbox][data-page]").forEach(function (cb) {
            var page = cb.getAttribute("data-page");
            var action = cb.getAttribute("data-action");
            var actions = map[page] || [];
            cb.checked = actions.indexOf(action) > -1;
        });
    }

    function renderUsers(checkedIds) {
        var checked = {};
        (checkedIds || []).forEach(function (id) { checked[id] = true; });
        usersBox.innerHTML = "";
        users.forEach(function (u) {
            var label = document.createElement("label");
            label.className = "ar-user-row";
            var cb = document.createElement("input");
            cb.type = "checkbox";
            cb.name = "UserIds";
            cb.value = u.Id;
            if (checked[u.Id]) { cb.checked = true; }
            var span = document.createElement("span");
            span.textContent = (u.FullName && u.FullName.trim()) ? (u.FullName + " (" + u.UserName + ")") : u.UserName;
            label.appendChild(cb);
            label.appendChild(span);
            usersBox.appendChild(label);
        });
    }

    window.arOpenModal = function () {
        document.getElementById("ar-modal-title").textContent = document.getElementById("ar-modal-title").textContent.replace(/^.*?—/, "دور جديد —");
        document.getElementById("ar-id").value = "0";
        document.getElementById("ar-nameAr").value = "";
        document.getElementById("ar-nameEn").value = "";
        document.getElementById("ar-note").value = "";
        document.getElementById("ar-active").checked = true;
        renderUsers([]);
        applyGrants({});
        modal.hidden = false;
    };

    window.arEditRole = function (id, nameAr, nameEn, note, isActive) {
        document.getElementById("ar-modal-title").textContent = document.getElementById("ar-modal-title").textContent.replace(/^.*?—/, "تعديل الدور —");
        document.getElementById("ar-id").value = id;
        document.getElementById("ar-nameAr").value = nameAr || "";
        document.getElementById("ar-nameEn").value = nameEn || "";
        document.getElementById("ar-note").value = note || "";
        document.getElementById("ar-active").checked = !!isActive;
        renderUsers(assigned[id] || []);
        applyGrants(grants[id] || {});
        modal.hidden = false;
    };

    window.arCloseModal = function () {
        modal.hidden = true;
    };

    window.arFilterUsers = function () {
        var q = (document.getElementById("ar-user-search").value || "").toLowerCase();
        usersBox.querySelectorAll(".ar-user-row").forEach(function (row) {
            row.style.display = row.textContent.toLowerCase().indexOf(q) > -1 ? "" : "none";
        });
    };

    // Close on backdrop click.
    modal.addEventListener("click", function (e) {
        if (e.target === modal) { window.arCloseModal(); }
    });
})();
