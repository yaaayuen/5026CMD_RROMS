/* =============================================================================
   Three Grills - light / dark theme switch (YJ-6)

   Loaded in <head> WITHOUT defer, so the theme is set before the page is drawn (no white flash on dark).

   Order of choice:
     1. the visitor's saved choice (localStorage), if there is one
     2. otherwise the device setting (prefers-color-scheme)
     3. otherwise light
   Any button with the attribute data-theme-toggle switches the theme and remembers the choice.
   Storage can be blocked (private window), so every read and write is wrapped in try/catch.
   ============================================================================= */
(function () {
    'use strict';

    var STORAGE_KEY = 'threegrills-theme';
    var root = document.documentElement;

    function savedTheme() {
        try {
            var value = window.localStorage.getItem(STORAGE_KEY);
            return value === 'dark' || value === 'light' ? value : null;
        } catch (error) {
            return null;
        }
    }

    function deviceTheme() {
        var wantsDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
        return wantsDark ? 'dark' : 'light';
    }

    function currentTheme() {
        return root.getAttribute('data-bs-theme') === 'dark' ? 'dark' : 'light';
    }

    function applyTheme(theme) {
        root.setAttribute('data-bs-theme', theme);
    }

    // Show the state on every toggle button: text, pressed state, and the label a screen reader hears.
    function updateButtons() {
        var isDark = currentTheme() === 'dark';
        var buttons = document.querySelectorAll('[data-theme-toggle]');

        buttons.forEach(function (button) {
            button.setAttribute('aria-pressed', isDark ? 'true' : 'false');
            button.setAttribute('aria-label', isDark ? 'Dark theme is on. Switch to light theme' : 'Light theme is on. Switch to dark theme');

            var text = button.querySelector('[data-theme-toggle-text]');
            if (text) {
                text.textContent = isDark ? 'Dark' : 'Light';
            }
        });
    }

    // 1. Set the theme right now, before the first paint.
    applyTheme(savedTheme() || deviceTheme());

    // 2. Wire up the buttons once the page exists.
    document.addEventListener('DOMContentLoaded', function () {
        updateButtons();

        document.addEventListener('click', function (event) {
            var button = event.target.closest ? event.target.closest('[data-theme-toggle]') : null;
            if (!button) {
                return;
            }

            var next = currentTheme() === 'dark' ? 'light' : 'dark';
            applyTheme(next);

            try {
                window.localStorage.setItem(STORAGE_KEY, next);
            } catch (error) {
                // Storage blocked: the theme still changes for this page view, it just is not remembered.
            }

            updateButtons();
        });
    });

    // 3. A visitor who never chose follows the device when it changes (for example, sunset on a phone).
    if (window.matchMedia) {
        var query = window.matchMedia('(prefers-color-scheme: dark)');
        var onChange = function () {
            if (!savedTheme()) {
                applyTheme(deviceTheme());
                updateButtons();
            }
        };

        if (query.addEventListener) {
            query.addEventListener('change', onChange);
        }
    }
})();