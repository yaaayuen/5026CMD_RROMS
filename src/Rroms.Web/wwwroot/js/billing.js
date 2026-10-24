document.querySelectorAll('.take-payment').forEach(form => {
    const total = Math.round(Number(form.dataset.total) * 100);
    const cash = form.querySelector('[name="Input.CashReceived"]');
    const submit = form.querySelector('.payment-submit');
    const format = cents => 'RM ' + (cents / 100).toLocaleString('en-MY', {minimumFractionDigits: 2, maximumFractionDigits: 2});
    const names = {Cash:'cash',CardTerminal:'card terminal',EWallet:'e-wallet'};
    const terminal = form.querySelector('.terminal-fields');
    const screen = form.querySelector('.terminal-screen');
    function update() {
        const method = form.querySelector('[name="Input.Method"]:checked')?.value;
        form.querySelector('.cash-fields').hidden = method !== 'Cash';
        cash.disabled = method !== 'Cash'; cash.required = method === 'Cash';
        if (terminal) terminal.hidden = !method || method === 'Cash';
        const tender = Math.round(Number(cash.value) * 100);
        cash.setCustomValidity(method === 'Cash' && tender < total ? 'Cash received must cover the total.' : '');
        form.querySelector('.cash-change').textContent = tender >= total ? format(tender - total) : 'Not enough cash';
        submit.textContent = method === 'CardTerminal' ? `Send ${format(total)} to card terminal`
            : method === 'EWallet' ? `Show ${format(total)} e-wallet QR on terminal`
            : `Confirm ${format(total)} paid by ${names[method] || 'selected method'}`;
    }
    form.addEventListener('input', update);
    form.querySelectorAll('[data-cash]').forEach(button => button.addEventListener('click', () => {
        const step = Number(button.dataset.cash) * 100;
        cash.value = ((step ? Math.ceil(total / step) * step : total) / 100).toFixed(2); update();
    }));
    form.addEventListener('submit', event => {
        if (form.dataset.submitting === 'true') { event.preventDefault(); return; }
        if (!form.checkValidity()) { event.preventDefault(); form.reportValidity(); return; }
        const method = form.querySelector('[name="Input.Method"]:checked')?.value;
        if (screen && method !== 'Cash' && form.dataset.terminalDone !== 'true') {
            // Show the simulated terminal for a moment, then send the form. The server makes the real decision.
            event.preventDefault();
            runTerminal(method);
            return;
        }
        form.dataset.submitting = 'true'; submit.disabled = true; submit.textContent = 'Saving payment…';
    });
    function runTerminal(method) {
        const wallet = method === 'EWallet';
        const status = screen.querySelector('.terminal-status');
        screen.querySelector('.terminal-prompt').textContent = wallet ? 'Scan with any DuitNow e-wallet' : 'Tap, insert or swipe card';
        const qr = screen.querySelector('.terminal-qr');
        qr.hidden = !wallet;
        if (wallet) drawQr(qr);
        status.textContent = 'Waiting for the customer…';
        submit.disabled = true;
        screen.showModal();
        setTimeout(() => { status.textContent = wallet ? 'Payment scanned. Contacting the bank…' : 'Card read. Contacting the bank…'; }, 1200);
        setTimeout(() => { form.dataset.terminalDone = 'true'; submit.disabled = false; form.requestSubmit(submit); }, 2400);
    }
    // A QR-style pattern with the three corner squares (the simulated terminal encodes nothing).
    function drawQr(canvas) {
        const ctx = canvas.getContext('2d'), cells = 25, size = canvas.width / cells;
        ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, canvas.width, canvas.height); ctx.fillStyle = '#111';
        const finder = (x, y) => { ctx.fillRect(x * size, y * size, 7 * size, 7 * size); ctx.fillStyle = '#fff';
            ctx.fillRect((x + 1) * size, (y + 1) * size, 5 * size, 5 * size); ctx.fillStyle = '#111';
            ctx.fillRect((x + 2) * size, (y + 2) * size, 3 * size, 3 * size); };
        for (let y = 0; y < cells; y++) for (let x = 0; x < cells; x++) {
            const corner = (x < 8 && y < 8) || (x > cells - 9 && y < 8) || (x < 8 && y > cells - 9);
            if (!corner && Math.random() < 0.45) ctx.fillRect(x * size, y * size, size, size);
        }
        finder(0, 0); finder(cells - 7, 0); finder(0, cells - 7);
    }
    screen?.addEventListener('cancel', event => event.preventDefault());
    window.addEventListener('pageshow', () => { delete form.dataset.submitting; delete form.dataset.terminalDone; if (screen?.open) screen.close(); submit.disabled = false; update(); });
    update();
});
