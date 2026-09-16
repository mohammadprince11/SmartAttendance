// ZYNORA — unified single-sidebar navigation.
// Parent modules and nested dropdowns never animate height at the same time.
(function () {
    "use strict";

    var groups = Array.prototype.slice.call(document.querySelectorAll(".zynora-nav-group"));
    if (!groups.length) return;

    var reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    var topDuration = reduceMotion ? 0 : 220;

    function nextFrames(callback) {
        requestAnimationFrame(function () {
            requestAnimationFrame(callback);
        });
    }

    function normalize(path) {
        if (!path) return "/";
        var clean = path.split("?")[0].split("#")[0].replace(/\/+$/, "").toLowerCase();
        return clean || "/";
    }

    function hrefPath(link) {
        try {
            return normalize(new URL(link.href, window.location.origin).pathname);
        } catch (_) {
            return "";
        }
    }

    function isSame(current, target) {
        if (current === target) return true;
        if (target.endsWith("/index")) {
            return current === (target.slice(0, -"/index".length) || "/");
        }
        return false;
    }

    function markCurrentGroup() {
        var current = normalize(window.location.pathname);

        groups.forEach(function (group) {
            var summary = group.querySelector(":scope > summary");
            if (!summary) return;

            var matches = Array.prototype.some.call(
                group.querySelectorAll(":scope > .zynora-nav-group-links a[href]"),
                function (link) {
                    var target = hrefPath(link);
                    return target && target !== "/" &&
                        (isSame(current, target) || current.indexOf(target + "/") === 0);
                }
            );

            summary.classList.toggle("ky-current", matches);
            group.classList.toggle("is-active", matches);
        });
    }

    function clearTopMotion(group, links) {
        delete group.dataset.zyNavMotion;
        if (!links) return;
        links.style.removeProperty("height");
        links.style.removeProperty("opacity");
        links.style.removeProperty("transition");
        links.style.removeProperty("will-change");
    }

    function closeGroup(group, immediate) {
        return new Promise(function (resolve) {
            var links = group.querySelector(":scope > .zynora-nav-group-links");

            if (!group.open) {
                clearTopMotion(group, links);
                resolve();
                return;
            }

            if (!links || immediate || topDuration === 0) {
                group.open = false;
                clearTopMotion(group, links);
                resolve();
                return;
            }

            group.dataset.zyNavMotion = "closing";
            links.style.height = links.scrollHeight + "px";
            links.style.opacity = "1";
            links.style.transition =
                "height " + topDuration + "ms cubic-bezier(.22,.61,.36,1), opacity 150ms ease";
            links.style.willChange = "height, opacity";

            nextFrames(function () {
                links.style.height = "0px";
                links.style.opacity = "0";
            });

            window.setTimeout(function () {
                group.open = false;
                clearTopMotion(group, links);
                resolve();
            }, topDuration);
        });
    }

    async function openGroup(group) {
        if (group.open || group.dataset.zyNavMotion) return;

        var others = groups.filter(function (other) {
            return other !== group && other.open;
        });

        if (others.length) {
            await Promise.all(others.map(function (other) {
                return closeGroup(other, false);
            }));
        }

        var links = group.querySelector(":scope > .zynora-nav-group-links");
        group.open = true;

        if (!links || topDuration === 0) {
            clearTopMotion(group, links);
            return;
        }

        group.dataset.zyNavMotion = "opening";
        links.style.height = "0px";
        links.style.opacity = "0";
        links.style.transition =
            "height " + topDuration + "ms cubic-bezier(.22,.61,.36,1), opacity 170ms ease";
        links.style.willChange = "height, opacity";

        var targetHeight = links.scrollHeight;

        nextFrames(function () {
            links.style.height = targetHeight + "px";
            links.style.opacity = "1";
        });

        window.setTimeout(function () {
            clearTopMotion(group, links);
        }, topDuration + 16);
    }

    groups.forEach(function (group) {
        group.classList.remove("ky-open", "ky-closing");
        group.open = false;
        clearTopMotion(group, group.querySelector(":scope > .zynora-nav-group-links"));

        var summary = group.querySelector(":scope > summary");
        if (!summary) return;

        summary.addEventListener("click", function (event) {
            event.preventDefault();
            if (group.dataset.zyNavMotion) return;

            if (group.open) {
                closeGroup(group, false);
            } else {
                openGroup(group);
            }
        });
    });

    markCurrentGroup();

    document.addEventListener("keydown", function (event) {
        if (event.key !== "Escape") return;
        groups.forEach(function (group) {
            if (group.open && !group.dataset.zyNavMotion) {
                closeGroup(group, false);
            }
        });
    });
})();

// Internal dropdowns use height animation only.
// The parent module stays at auto height while an inner dropdown moves.
(function () {
    "use strict";

    var reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    var duration = reduceMotion ? 0 : 220;

    function nextFrames(callback) {
        requestAnimationFrame(function () {
            requestAnimationFrame(callback);
        });
    }

    function clearBody(acc, body) {
        delete acc.dataset.zyAccMotion;
        if (!body) return;
        body.style.removeProperty("height");
        body.style.removeProperty("opacity");
        body.style.removeProperty("transition");
        body.style.removeProperty("will-change");
    }

    function closeAcc(acc, immediate) {
        return new Promise(function (resolve) {
            var body = acc.querySelector(":scope > .ky-acc-body");

            if (!acc.open) {
                clearBody(acc, body);
                resolve();
                return;
            }

            if (!body || immediate || duration === 0) {
                acc.open = false;
                clearBody(acc, body);
                resolve();
                return;
            }

            acc.dataset.zyAccMotion = "closing";
            body.style.height = body.scrollHeight + "px";
            body.style.opacity = "1";
            body.style.transition =
                "height " + duration + "ms cubic-bezier(.22,.61,.36,1), opacity 150ms ease";
            body.style.willChange = "height, opacity";

            nextFrames(function () {
                body.style.height = "0px";
                body.style.opacity = "0";
            });

            window.setTimeout(function () {
                acc.open = false;
                clearBody(acc, body);
                resolve();
            }, duration);
        });
    }

    async function openAcc(acc) {
        if (acc.open || acc.dataset.zyAccMotion) return;

        var parentGroup = acc.closest(".zynora-nav-group");
        if (parentGroup && parentGroup.dataset.zyNavMotion) return;

        var parent = acc.parentElement;
        var siblings = Array.prototype.filter.call(
            parent ? parent.children : [],
            function (node) {
                return node !== acc &&
                    node.classList &&
                    node.classList.contains("ky-acc") &&
                    node.open;
            }
        );

        if (siblings.length) {
            await Promise.all(siblings.map(function (other) {
                return closeAcc(other, false);
            }));
        }

        var body = acc.querySelector(":scope > .ky-acc-body");
        acc.open = true;

        if (!body || duration === 0) {
            clearBody(acc, body);
            return;
        }

        acc.dataset.zyAccMotion = "opening";
        body.style.height = "0px";
        body.style.opacity = "0";
        body.style.transition =
            "height " + duration + "ms cubic-bezier(.22,.61,.36,1), opacity 170ms ease";
        body.style.willChange = "height, opacity";

        var targetHeight = body.scrollHeight;

        nextFrames(function () {
            body.style.height = targetHeight + "px";
            body.style.opacity = "1";
        });

        window.setTimeout(function () {
            clearBody(acc, body);
        }, duration + 16);
    }

    document.querySelectorAll(".ky-acc").forEach(function (acc) {
        clearBody(acc, acc.querySelector(":scope > .ky-acc-body"));

        var summary = acc.querySelector(":scope > summary");
        if (!summary) return;

        summary.addEventListener("click", function (event) {
            event.preventDefault();
            event.stopPropagation();

            var parentGroup = acc.closest(".zynora-nav-group");
            if (parentGroup && parentGroup.dataset.zyNavMotion) return;
            if (acc.dataset.zyAccMotion) return;

            if (acc.open) {
                closeAcc(acc, false);
            } else {
                openAcc(acc);
            }
        });
    });
})();

// Mobile shell drawer remains independent from module accordions.
(function () {
    "use strict";

    var toggle = document.querySelector("[data-zy-mobile-nav-toggle]");
    var sidebar = document.getElementById("zy-mobile-navigation");
    var closeSurface = document.querySelector("[data-zy-mobile-nav-close]");
    var mobile = window.matchMedia("(max-width: 980px)");

    if (!toggle || !sidebar || !closeSurface) return;

    function setOpen(open, returnFocus) {
        document.body.classList.toggle("zy-mobile-nav-open", open);
        toggle.setAttribute("aria-expanded", open ? "true" : "false");
        closeSurface.tabIndex = open ? 0 : -1;

        if (open) {
            var active = sidebar.querySelector("[aria-current='page']") ||
                sidebar.querySelector("a, summary");
            if (active) {
                window.setTimeout(function () { active.focus(); }, 180);
            }
        } else if (returnFocus) {
            toggle.focus();
        }
    }

    toggle.addEventListener("click", function () {
        setOpen(!document.body.classList.contains("zy-mobile-nav-open"), false);
    });

    closeSurface.addEventListener("click", function () {
        setOpen(false, true);
    });

    sidebar.addEventListener("click", function (event) {
        if (mobile.matches && event.target.closest("a[href]")) {
            setOpen(false, false);
        }
    });

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" &&
            document.body.classList.contains("zy-mobile-nav-open")) {
            setOpen(false, true);
        }
    });

    mobile.addEventListener("change", function (event) {
        if (!event.matches) setOpen(false, false);
    });
})();