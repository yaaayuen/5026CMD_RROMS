"use strict";
(function () {
    const form = document.getElementById("chatbot-form");
    if (!form) { return; }
    const input = document.getElementById("chatbot-question");
    const log = document.getElementById("chatbot-log");
    const state = document.getElementById("chatbot-state");
    const button = document.getElementById("chatbot-send");
    const clear = document.getElementById("chatbot-clear");
    const suggestions = document.getElementById("chatbot-suggestions");
    const panel = document.getElementById("alex-chat");
    const allowedPages = new Set(["/Menu", "/Announcements", "/Customer/Reservations", "/Customer/Tracking", "/Customer/Feedback", "/Account/Profile", "/Account/Login", "/Visit"]);
    // History stays in this browser tab only, and each signed-in user gets their own.
    const storageKey = "tg-chat:" + ((panel && panel.dataset && panel.dataset.chatUser) || "guest");
    const maxMessages = 40;
    let history = load();

    function load() {
        try {
            const saved = JSON.parse(sessionStorage.getItem(storageKey) || "[]");
            return Array.isArray(saved) ? saved : [];
        } catch { return []; }
    }
    function save() {
        try { sessionStorage.setItem(storageKey, JSON.stringify(history)); } catch { /* storage can be blocked; the chat still works */ }
    }
    function element(tag, className, text) {
        const node = document.createElement(tag);
        if (className) { node.className = className; }
        if (text !== undefined) { node.textContent = text; }
        return node;
    }
    // Every value is assigned as text, and links are only made for approved pages, even for restored history.
    function render(message) {
        const mine = message.from === "you";
        const bubble = element("div", "chat-msg " + (mine ? "from-you" : "from-bot"));
        if (!mine) { bubble.append(element("span", "chat-who", message.source === "AI" ? "Three Grills AI" : "Three Grills")); }
        bubble.append(element("p", "mb-0", String(message.text || "")));
        if (Array.isArray(message.steps) && message.steps.length > 0) {
            const list = element("ol", "chat-steps");
            message.steps.forEach(function (step) { list.append(element("li", "", String(step))); });
            bubble.append(list);
        }
        if (allowedPages.has(message.pageUrl)) {
            const link = element("a", "btn btn-sm chat-action", "Open " + (message.pageLabel || "page") + " →");
            link.href = message.pageUrl;
            bubble.append(link);
        }
        log.append(bubble);
        log.scrollTop = log.scrollHeight;
        if (suggestions) { suggestions.hidden = true; }
    }
    function add(message) {
        history.push(message);
        history = history.slice(-maxMessages);
        save();
        render(message);
    }
    history.forEach(render);

    async function ask() {
        const question = input.value.trim();
        if (button.disabled || !question) { return; }
        button.disabled = true;
        state.textContent = "Checking your question...";
        add({ from: "you", text: question });
        try {
            const response = await fetch(form.action, { method: "POST", body: new FormData(form), signal: AbortSignal.timeout(18000) });
            if (response.status === 429) { throw new Error("Please wait a minute before asking again."); }
            if (!response.ok) { throw new Error("The assistant is unavailable. Please try again."); }
            const reply = await response.json();
            add({ from: "bot", text: reply.answer, source: reply.source, steps: reply.steps, pageUrl: reply.pageUrl, pageLabel: reply.pageLabel });
            input.value = "";
            state.textContent = "";
        } catch (error) {
            state.textContent = error.name === "TimeoutError" ? "That took too long. Try a menu or booking question." : error.message;
        } finally { button.disabled = false; input.focus(); }
    }
    form.addEventListener("submit", function (event) {
        event.preventDefault();
        ask();
    });
    if (suggestions) {
        suggestions.querySelectorAll("button").forEach(function (chip) {
            chip.addEventListener("click", function () {
                input.value = chip.textContent;
                ask();
            });
        });
    }
    if (clear) {
        clear.addEventListener("click", function () {
            history = [];
            save();
            log.replaceChildren();
            if (suggestions) { suggestions.hidden = false; }
            input.focus();
        });
    }
    // Signing out removes this user's chat from the tab.
    document.querySelectorAll("form[action*='/Account/Logout']").forEach(function (logout) {
        logout.addEventListener("submit", function () {
            try { sessionStorage.removeItem(storageKey); } catch { /* nothing to remove */ }
        });
    });
})();
