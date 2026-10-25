const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../../src/Rroms.Web/wwwroot/js/dining-live.js'), 'utf8');

// A refreshed partial is described as data: the order ids it lists and its countdown end times.
function view(ids, endsAt = []) {
    const cards = Object.fromEntries(ids.map(id => [id, { classList: new Set() }]));
    return { ids, cards, timers: endsAt.map(iso => ({ dataset: { endsAt: iso }, textContent: '' })) };
}

function load(first, kitchen = true) {
    let current = first;
    const timers = [];
    const state = { textContent: '' };
    const content = {
        dataset: { kitchen: String(kitchen), refreshUrl: '/Staff/Orders?handler=Queue' },
        contains: () => false,
        querySelector: sel => sel === '[data-order-ids]' ? { dataset: { orderIds: current.ids.join(',') } } : null,
        querySelectorAll(sel) {
            if (sel === '[data-ends-at]') { return current.timers; }
            const match = /data-order-id="(\d+)"/.exec(sel);
            if (match) { return current.cards[match[1]] ? [{ classList: { add: c => current.cards[match[1]].classList.add(c) } }] : []; }
            return [];
        },
        set innerHTML(next) { current = next; }
    };
    const page = { next: first };
    vm.runInNewContext(script, {
        document: { getElementById: id => ({ 'dining-live-content': content, 'dining-refresh-state': state })[id] ?? null, hidden: false, activeElement: null },
        window: { setInterval: fn => timers.push(fn) },
        localStorage: { getItem: () => null, setItem() {} },
        fetch: async () => ({ ok: true, status: 200, redirected: false, text: async () => page.next }),
        Date
    });
    const [tick, refresh] = timers;
    return { tick, refresh: async next => { page.next = next; refresh(); await new Promise(r => setImmediate(r)); await new Promise(r => setImmediate(r)); }, state, current: () => current };
}

test('countdowns show minutes and seconds left, then finishing', () => {
    const soon = new Date(Date.now() + 125000).toISOString();
    const past = new Date(Date.now() - 1000).toISOString();
    const kitchen = load(view(['1', '2'], [soon, past]));
    kitchen.tick();
    const [running, done] = kitchen.current().timers;
    assert.match(running.textContent, /^2:0[45]$/);
    assert.equal(done.textContent, 'finishing…');
});

test('orders that arrive between refreshes flash and are announced', async () => {
    const kitchen = load(view(['1', '2']));
    await kitchen.refresh(view(['1', '2', '3']));
    assert.ok(kitchen.current().cards['3'].classList.has('is-new'));
    assert.ok(!kitchen.current().cards['1'].classList.has('is-new'));
    assert.equal(kitchen.state.textContent, '1 new order just arrived.');
});

test('the customer page never flashes or announces orders', async () => {
    const tracking = load(view(['1']), false);
    await tracking.refresh(view(['1', '2']));
    assert.ok(!tracking.current().cards['2'].classList.has('is-new'));
    assert.match(tracking.state.textContent, /^Last checked/);
});
