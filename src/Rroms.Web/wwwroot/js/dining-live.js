"use strict";
(function () {
    const content = document.getElementById("dining-live-content");
    if (!content) { return; }
    const state = document.getElementById("dining-refresh-state");
    const kitchen = content.dataset.kitchen === "true";
    let busy = false;
    let knownOrders = orderIds();
    let soundOn = false;

    // Countdowns: every [data-ends-at] shows the time left as m:ss, counted on this device between refreshes.
    function tick() {
        content.querySelectorAll("[data-ends-at]").forEach(function (el) {
            const left = Math.max(0, Math.round((Date.parse(el.dataset.endsAt) - Date.now()) / 1000));
            el.textContent = left === 0 ? "finishing…" : Math.floor(left / 60) + ":" + String(left % 60).padStart(2, "0");
        });
    }

    function orderIds() {
        const holder = content.querySelector("[data-order-ids]");
        return new Set(holder && holder.dataset.orderIds ? holder.dataset.orderIds.split(",") : []);
    }

    // Kitchen only: flash cards for orders that arrived since the last refresh, and beep if staff switched sound on.
    function announceNewOrders() {
        const now = orderIds();
        const fresh = Array.from(now).filter(id => !knownOrders.has(id));
        knownOrders = now;
        if (!kitchen || fresh.length === 0) { return; }
        fresh.forEach(function (id) {
            content.querySelectorAll('[data-order-id="' + id + '"]').forEach(el => el.classList.add("is-new"));
        });
        state.textContent = fresh.length + (fresh.length === 1 ? " new order" : " new orders") + " just arrived.";
        if (soundOn) { beep(); }
    }

    function beep() {
        try {
            const audio = new (window.AudioContext || window.webkitAudioContext)();
            const tone = audio.createOscillator();
            const volume = audio.createGain();
            tone.frequency.value = 880;
            volume.gain.value = 0.15;
            tone.connect(volume).connect(audio.destination);
            tone.start();
            tone.stop(audio.currentTime + 0.25);
        } catch { /* Sound is optional; the flashing card still shows the new order. */ }
    }

    const soundButton = document.getElementById("kitchen-sound");
    if (soundButton) {
        try { soundOn = localStorage.getItem("rroms-kitchen-sound") === "on"; } catch { soundOn = false; }
        const show = function () {
            soundButton.textContent = "Sound alerts: " + (soundOn ? "on" : "off");
            soundButton.setAttribute("aria-pressed", String(soundOn));
        };
        show();
        // Browsers only play sound after a click on the page, which this button provides.
        soundButton.addEventListener("click", function () {
            soundOn = !soundOn;
            try { localStorage.setItem("rroms-kitchen-sound", soundOn ? "on" : "off"); } catch { /* Not saved; still works now. */ }
            show();
            if (soundOn) { beep(); }
        });
    }

    async function refresh() {
        if (busy || document.hidden || content.contains(document.activeElement)) { return; }
        busy = true;
        try {
            const response = await fetch(content.dataset.refreshUrl, { cache: "no-store" });
            if (response.redirected || response.status === 401 || response.status === 403) {
                state.textContent = "Please sign in again to continue receiving updates.";
                return;
            }
            if (!response.ok) { throw new Error("Update failed"); }
            // This is a Razor-rendered same-origin partial; Razor encodes all stored text.
            const open = Array.from(content.querySelectorAll("details[open][id]"), d => d.id);
            // Kitchen columns scroll on their own; keep each one where staff left it.
            const scrolled = Array.from(content.querySelectorAll("section[aria-labelledby]"), s => [s.getAttribute("aria-labelledby"), s.scrollTop]);
            content.innerHTML = await response.text();
            open.forEach(id => document.getElementById(id)?.setAttribute("open", ""));
            scrolled.forEach(function ([label, top]) {
                const column = content.querySelector('section[aria-labelledby="' + label + '"]');
                if (column) { column.scrollTop = top; }
            });
            state.textContent = "Last checked " + new Date().toLocaleTimeString();
            tick();
            announceNewOrders();
        } catch {
            state.textContent = "Connection interrupted. Retrying automatically.";
        } finally { busy = false; }
    }
    // Column scrollbars stay hidden and only show while a column is being scrolled (site.css .is-scrolling).
    // Scroll events do not bubble, so listen in the capture phase; this survives the refreshes above.
    content.addEventListener("scroll", function (event) {
        const column = event.target;
        if (!(column instanceof Element) || !column.matches("section[aria-labelledby]")) { return; }
        column.classList.add("is-scrolling");
        clearTimeout(column.scrollHideTimer);
        column.scrollHideTimer = setTimeout(() => column.classList.remove("is-scrolling"), 900);
    }, true);

    tick();
    window.setInterval(tick, 1000);
    window.setInterval(refresh, 5000);
    if (content.dataset.useSignalr && window.signalR) {
        const connection = new signalR.HubConnectionBuilder().withUrl("/hubs/notifications").withAutomaticReconnect().build();
        connection.on("notificationReceived", refresh);
        connection.onreconnected(refresh);
        connection.start().then(refresh).catch(function () { state.textContent = "Using 5-second updates."; });
    }
})();
