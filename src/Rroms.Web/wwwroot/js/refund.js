// Staff dish refund: shows the refund total while staff choose quantities. The server recalculates it on save.
document.querySelectorAll('.refund-form').forEach(form => {
    const paid = Math.round(Number(form.dataset.paid) * 100);
    const format = cents => 'RM ' + (cents / 100).toLocaleString('en-MY', {minimumFractionDigits: 2, maximumFractionDigits: 2});
    const inputs = [...form.querySelectorAll('.refund-qty')];
    function update() {
        let total = 0;
        const orders = {};
        inputs.forEach(input => {
            const row = input.closest('tr');
            const qty = Math.max(0, Math.min(Number(input.value) || 0, Number(input.dataset.max)));
            const cents = Math.round(qty * Number(input.dataset.share) * 100);
            row.querySelector('.refund-line-amount').textContent = format(cents);
            const order = orders[row.dataset.order] ??= {paid: Math.round(Number(row.dataset.orderPaid) * 100), sum: 0, full: true};
            order.sum += cents;
            order.full = order.full && qty === Number(input.dataset.max);
        });
        // A fully refunded order gets back exactly what it paid (no rounding cents left over).
        Object.values(orders).forEach(order => { total += order.full ? order.paid : Math.min(order.sum, order.paid); });
        form.querySelector('.refund-total').textContent = format(total);
        form.querySelector('.refund-net').textContent = format(paid - total);
    }
    form.addEventListener('input', update);
    form.querySelector('.refund-all')?.addEventListener('click', () => {
        inputs.forEach(input => { input.value = input.dataset.max; });
        update();
    });
    update();
});
