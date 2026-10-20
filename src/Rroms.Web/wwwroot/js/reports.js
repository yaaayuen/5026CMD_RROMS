/* Progressive enhancement: every report and date field is usable without JavaScript. */
(() => {
    "use strict";
    const filters = document.getElementById("report-filters");
    const period = filters?.querySelector('[name="Filter.Period"]');
    function updateDates() {
        const custom = period.value === "3" || period.value === "Custom";
        filters.querySelectorAll("[data-date-field]").forEach(field => {
            const visible = (field.dataset.dateField === "custom") === custom;
            field.hidden = !visible;
            field.querySelectorAll("input").forEach(input => { input.disabled = !visible; });
        });
    }
    if (period) {
        period.addEventListener("change", updateDates);
        updateDates();
    }
    const navigation = document.querySelector(".report-tabs");
    if (!navigation) { return; }
    const tabs = Array.from(navigation.querySelectorAll("a"));
    const selector = document.getElementById("export-kind");
    navigation.setAttribute("role", "tablist");
    tabs.forEach(tab => {
        const panel = document.querySelector(tab.hash);
        tab.setAttribute("role", "tab");
        tab.setAttribute("aria-controls", panel.id);
        panel.setAttribute("role", "tabpanel");
        panel.setAttribute("aria-labelledby", tab.id);
        panel.tabIndex = 0;
    });
    function activate(tab, updateHash = false) {
        tabs.forEach(candidate => {
            const selected = candidate === tab;
            candidate.setAttribute("aria-selected", String(selected));
            candidate.tabIndex = selected ? 0 : -1;
            document.querySelector(candidate.hash).hidden = !selected;
        });
        // The server enum and tabs both follow Reservations, Sales, PopularItems, CustomerActivity.
        selector.selectedIndex = tabs.indexOf(tab);
        if (updateHash) { history.replaceState(null, "", tab.hash); }
    }
    tabs.forEach((tab, index) => {
        tab.addEventListener("click", event => {
            event.preventDefault();
            activate(tab, true);
        });
        tab.addEventListener("keydown", event => {
            let next;
            if (event.key === "ArrowRight") { next = (index + 1) % tabs.length; }
            else if (event.key === "ArrowLeft") { next = (index + tabs.length - 1) % tabs.length; }
            else if (event.key === "Home") { next = 0; }
            else if (event.key === "End") { next = tabs.length - 1; }
            else { return; }
            event.preventDefault();
            tabs[next].focus();
            activate(tabs[next], true);
        });
    });
    selector.addEventListener("change", () => { activate(tabs[selector.selectedIndex], true); });
    function followHash() { activate(tabs.find(tab => tab.hash === location.hash) || tabs[0]); }
    window.addEventListener("hashchange", followHash);
    followHash();
})();
