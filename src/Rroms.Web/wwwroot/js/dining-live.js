"use strict";
(function () {
    const content = document.getElementById("dining-live-content");
    if (!content) { return; }
    const state = document.getElementById("dining-refresh-state");
    let busy = false;
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
            content.innerHTML = await response.text();
            state.textContent = "Last checked " + new Date().toLocaleTimeString();
        } catch {
            state.textContent = "Connection interrupted. Retrying automatically.";
        } finally { busy = false; }
    }
    window.setInterval(refresh, 5000);
    if (content.dataset.useSignalr && window.signalR) {
        const connection = new signalR.HubConnectionBuilder().withUrl("/hubs/notifications").withAutomaticReconnect().build();
        connection.on("notificationReceived", refresh);
        connection.onreconnected(refresh);
        connection.start().then(refresh).catch(function () { state.textContent = "Using 5-second updates."; });
    }
})();
