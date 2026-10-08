// Three Grills - site-wide JavaScript.
// Feature-specific scripts (live notifications, charts, chatbot) live in their own files
// and are added by each module owner in their sprint.

"use strict";

// Shared helpers used on many pages.

//----- Show/hide password toggle -----
//Adds an eye button to every password box, so users can check what they typed before
//submitting (fewer typos, fewer accidental lockouts). Done here once, so every password
//field on every page gets it automatically. Without JavaScript the box stays a normal password box.
(function () {
    // Static icon markup (no user data is ever inserted, so innerHTML is safe here).
    const eyeIcon =
        '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
        '<path d="M1 12s4-7 11-7 11 7 11 7-4 7-11 7S1 12 1 12z" /><circle cx="12" cy="12" r="3" /></svg>';
    const eyeOffIcon =
        '<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" ' +
        'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
        '<path d="M1 12s4-7 11-7 11 7 11 7-4 7-11 7S1 12 1 12z" /><circle cx="12" cy="12" r="3" />' +
        '<line x1="3" y1="3" x2="21" y2="21" /></svg>';

    /** Shows the right icon and screen-reader label for the current state. */
    function setState(button, isVisible) {
        button.innerHTML = isVisible ? eyeOffIcon : eyeIcon;
        button.setAttribute("aria-label", isVisible ? "Hide password" : "Show password");
    }

    /** Wraps one password input and adds the toggle button beside it. */
    function addToggle(input) {
        const wrapper = document.createElement("div");
        wrapper.className = "password-field";
        input.parentNode.insertBefore(wrapper, input);
        wrapper.appendChild(input);

        const button = document.createElement("button");
        button.type = "button"; // "button", not "submit": clicking it must never send the form
        button.className = "password-toggle";
        button.setAttribute("aria-controls", input.id);
        setState(button, false);
        wrapper.appendChild(button);

        button.addEventListener("click", function () {
            const show = input.type === "password";
            input.type = show ? "text" : "password";
            setState(button, show);
            input.focus();
        });

        // Hide the password again just before the form is sent, so the browser and
        // password managers still treat the field as a password.
        if (input.form) {
            input.form.addEventListener("submit", function () {
                input.type = "password";
                setState(button, false);
            });
        }
    }

    document.addEventListener("DOMContentLoaded", function () {
        document.querySelectorAll('input[type="password"]').forEach(addToggle);
    });
})();