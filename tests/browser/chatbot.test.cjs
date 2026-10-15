const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../../src/Rroms.Web/wwwroot/js/chatbot.js'), 'utf8');

async function ask(reply, consent, status = 200) {
    let submit;
    const navigation = [];
    const prompts = [];
    const log = [];
    const elements = {
        'chatbot-form': { action: '/Chat', addEventListener: (_, fn) => { submit = fn; } },
        'chatbot-question': { value: 'show menu', focus() {} },
        'chatbot-log': { append: p => log.push(p.textContent) },
        'chatbot-state': {},
        'chatbot-send': { disabled: false }
    };
    vm.runInNewContext(script, {
        document: { getElementById: id => elements[id], createElement: () => ({ scrollIntoView() {} }) },
        window: { confirm: text => { prompts.push(text); return consent; }, location: { assign: url => navigation.push(url) } },
        FormData: class {},
        AbortSignal: { timeout: () => ({}) },
        fetch: async () => ({ status, ok: status === 200, json: async () => reply })
    });
    await submit({ preventDefault() {} });
    return { navigation, prompts, log, elements };
}

test('accepting the popup opens the suggested customer page', async () => {
    const result = await ask({ answer: 'Menu', pageUrl: '/Menu', pageLabel: 'Menu', source: 'Local' }, true);
    assert.deepEqual(result.navigation, ['/Menu']);
    assert.equal(result.prompts.length, 1);
});
test('declining the popup keeps the customer on the current page', async () => {
    const result = await ask({ answer: 'Menu', pageUrl: '/Menu', pageLabel: 'Menu' }, false);
    assert.deepEqual(result.navigation, []);
    assert.equal(result.prompts.length, 1);
});
test('external or unapproved navigation is ignored', async () => {
    for (const url of ['https://example.com', '//example.com', '/Admin/Announcements', 'javascript:alert(1)']) {
        const result = await ask({ answer: 'Answer', pageUrl: url, pageLabel: 'Page' }, true);
        assert.deepEqual(result.navigation, []);
        assert.deepEqual(result.prompts, []);
    }
});
test('answer text is assigned as text and AI answers are labelled', async () => {
    const result = await ask({ answer: '<img src=x onerror=alert(1)>', source: 'AI' }, true);
    assert.equal(result.log[1], 'AI answer: <img src=x onerror=alert(1)>');
    assert.deepEqual(result.navigation, []);
});
test('rate limiting leaves the form usable with an explanation', async () => {
    const result = await ask({}, false, 429);
    assert.match(result.elements['chatbot-state'].textContent, /wait a minute/);
    assert.equal(result.elements['chatbot-send'].disabled, false);
});
test('a failed response does not navigate', async () => {
    const result = await ask({}, true, 500);
    assert.deepEqual(result.navigation, []);
    assert.equal(result.elements['chatbot-send'].disabled, false);
});
