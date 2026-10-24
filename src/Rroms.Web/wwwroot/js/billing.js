document.querySelectorAll('.take-payment').forEach(form => {
    const total = Math.round(Number(form.dataset.total) * 100);
    const cash = form.querySelector('[name="Input.CashReceived"]');
    const submit = form.querySelector('.payment-submit');
    const format = cents => 'RM ' + (cents / 100).toLocaleString('en-MY', {minimumFractionDigits: 2, maximumFractionDigits: 2});
    const names = {Cash:'cash',CardTerminal:'card terminal',EWallet:'e-wallet'};
    function update() {
        const method = form.querySelector('[name="Input.Method"]:checked')?.value;
        form.querySelector('.cash-fields').hidden = method !== 'Cash';
        cash.disabled = method !== 'Cash'; cash.required = method === 'Cash';
        const tender = Math.round(Number(cash.value) * 100);
        cash.setCustomValidity(method === 'Cash' && tender < total ? 'Cash received must cover the total.' : '');
        form.querySelector('.cash-change').textContent = tender >= total ? format(tender - total) : 'Not enough cash';
        submit.textContent = `Confirm ${format(total)} paid by ${names[method] || 'selected method'}`;
    }
    form.addEventListener('input', update);
    form.querySelectorAll('[data-cash]').forEach(button => button.addEventListener('click', () => {
        const step = Number(button.dataset.cash) * 100;
        cash.value = ((step ? Math.ceil(total / step) * step : total) / 100).toFixed(2); update();
    }));
    form.addEventListener('submit', event => {
        if (form.dataset.submitting === 'true') { event.preventDefault(); return; }
        if (!form.checkValidity()) { event.preventDefault(); form.reportValidity(); return; }
        form.dataset.submitting = 'true'; submit.disabled = true; submit.textContent = 'Saving payment…';
    });
    window.addEventListener('pageshow', () => { delete form.dataset.submitting; submit.disabled = false; update(); });
    update();
});
