"use strict";
(function () {
    const form = document.getElementById("chatbot-form");
    if (!form) { return; }
    const input = document.getElementById("chatbot-question");
    const log = document.getElementById("chatbot-log");
    const state = document.getElementById("chatbot-state");
    const button = document.getElementById("chatbot-send");
    const allowedPages = new Set(["/Menu", "/Announcements", "/Customer/Reservations", "/Customer/Tracking", "/Customer/Feedback", "/Account/Profile", "/Account/Login"]);
    function line(text) {
        const p = document.createElement("p");
        p.textContent = text;
        log.append(p);
        p.scrollIntoView({ block: "nearest" });
    }
    form.addEventListener("submit", async function (event) {
        event.preventDefault();
        if (button.disabled || !input.value.trim()) { return; }
        button.disabled = true;
        state.textContent = "Checking your question...";
        line("You: " + input.value);
        try {
            const response = await fetch(form.action, { method: "POST", body: new FormData(form), signal: AbortSignal.timeout(18000) });
            if (response.status === 429) { throw new Error("Please wait a minute before asking again."); }
            if (!response.ok) { throw new Error("The assistant is unavailable. Please try again."); }
            const reply = await response.json();
            line((reply.source === "AI" ? "AI answer: " : "Three Grills: ") + reply.answer);
            input.value = "";
            if (allowedPages.has(reply.pageUrl) && window.confirm("Would you like to open " + reply.pageLabel + "?")) {
                window.location.assign(reply.pageUrl);
            }
            state.textContent = "";
        } catch (error) {
            state.textContent = error.name === "TimeoutError" ? "That took too long. Try a menu or booking question." : error.message;
        } finally { button.disabled = false; input.focus(); }
    });
})();
