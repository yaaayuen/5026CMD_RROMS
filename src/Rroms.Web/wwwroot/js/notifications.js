// Three Grills - live notifications (YY-6).
//
// 1. Connects to the SignalR hub and listens for "notificationReceived".
// 2. On each notification: increases the bell badge and shows a pop-up (Bootstrap toast).
// 3. If the live connection fails or drops, falls back to asking the server for the unread
//    count every 30 seconds (polling), so the badge stays roughly up to date anyway.
//
// Security note: notification text is inserted with textContent, never innerHTML,
// so text containing HTML or <script> is displayed as plain text (prevents XSS).

"use strict";

(function () {
    const POLL_INTERVAL_MS = 30000;
    const TOAST_HIDE_DELAY_MS = 8000;

    const bell = document.querySelector("[data-notification-bell]");
    if (!bell) {
        // The bell is only rendered for signed-in users; visitors need none of this.
        return;
    }

    const badge = bell.querySelector("[data-notification-count]");
    const toastContainer = document.getElementById("notification-toasts");
    let pollTimer = null;

    function getCount() {
        const value = parseInt(badge.textContent, 10);
        return Number.isNaN(value) ? 0 : value;
    }

    function setCount(count) {
        badge.textContent = String(count);
        badge.classList.toggle("d-none", count === 0);
        bell.setAttribute("aria-label", "Notifications, " + count + " unread");
    }

    function showToast(notification) {
        if (!toastContainer || !window.bootstrap) {
            return;
        }

        const toast = document.createElement("div");
        toast.className = "toast tg-toast";
        toast.setAttribute("role", "status");
        toast.setAttribute("aria-live", "polite");
        toast.setAttribute("aria-atomic", "true");

        const header = document.createElement("div");
        header.className = "toast-header";
        const title = document.createElement("strong");
        title.className = "me-auto";
        title.textContent = notification.title;
        const close = document.createElement("button");
        close.type = "button";
        close.className = "btn-close";
        close.setAttribute("data-bs-dismiss", "toast");
        close.setAttribute("aria-label", "Close");
        header.append(title, close);

        const body = document.createElement("div");
        body.className = "toast-body";
        body.textContent = notification.message;

        if (notification.linkUrl && notification.linkUrl.startsWith("/")) {
            const link = document.createElement("a");
            link.href = bell.getAttribute("href");
            link.className = "d-block mt-2 fw-bold";
            link.textContent = "View in notifications";
            body.append(link);
        }

        toast.append(header, body);
        toastContainer.append(toast);

        const bootstrapToast = new window.bootstrap.Toast(toast, { delay: TOAST_HIDE_DELAY_MS });
        toast.addEventListener("hidden.bs.toast", function () {
            toast.remove();
        });
        bootstrapToast.show();
    }

    async function refreshCount() {
        try {
            const response = await fetch(bell.dataset.countUrl, {
                credentials: "same-origin",
                headers: { "Accept": "application/json" }
            });
            if (response.ok) {
                const data = await response.json();
                setCount(data.unreadCount);
            }
        } catch (error) {
            // Network blip: try again on the next poll.
        }
    }

    function startPolling() {
        if (pollTimer === null) {
            pollTimer = window.setInterval(refreshCount, POLL_INTERVAL_MS);
        }
    }

    function stopPolling() {
        if (pollTimer !== null) {
            window.clearInterval(pollTimer);
            pollTimer = null;
        }
    }

    if (!window.signalR) {
        startPolling();
        return;
    }

    const connection = new window.signalR.HubConnectionBuilder()
        .withUrl(bell.dataset.hubUrl)
        .withAutomaticReconnect()
        .build();

    connection.on("notificationReceived", function (notification) {
        setCount(getCount() + 1);
        showToast(notification);
    });

    // While reconnecting, poll so nothing is missed; once back, refresh and stop polling.
    connection.onreconnecting(startPolling);
    connection.onreconnected(function () {
        stopPolling();
        refreshCount();
    });
    connection.onclose(startPolling);

    connection.start().catch(startPolling);
})();
