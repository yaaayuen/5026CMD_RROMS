const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../../src/Rroms.Web/wwwroot/js/chatbot.js'), 'utf8');

class Node {
    constructor(tag) { this.tag = tag; this.children = []; this.textContent = ''; this.listeners = {}; }
    append(child) { this.children.push(child); }
    replaceChildren() { this.children = []; }
    addEventListener(type, fn) { this.listeners[type] = fn; }
    querySelectorAll() { return this.buttons || []; }
    find(tag) { return this.children.find(c => c.tag === tag); }
}

function load(storage = new Map(), user = 'darren@gmail.com') {
    const chip = new Node('button');
    chip.textContent = 'How do I book a table?';
    const suggestions = new Node('div');
    suggestions.buttons = [chip];
    const elements = {
        'chatbot-form': new Node('form'),
        'chatbot-question': Object.assign(new Node('input'), { value: '', focus() {} }),
        'chatbot-log': new Node('div'),
        'chatbot-state': new Node('p'),
        'chatbot-send': Object.assign(new Node('button'), { disabled: false }),
        'chatbot-clear': new Node('button'),
        'chatbot-suggestions': suggestions,
        'alex-chat': Object.assign(new Node('div'), { dataset: { chatUser: user } })
    };
    elements['chatbot-form'].action = '/Chat';
    const page = { reply: {}, status: 200, prompts: [] };
    vm.runInNewContext(script, {
        document: { getElementById: id => elements[id], createElement: tag => new Node(tag), querySelectorAll: () => [] },
        window: { confirm: text => { page.prompts.push(text); return true; } },
        sessionStorage: { getItem: k => storage.get(k) ?? null, setItem: (k, v) => storage.set(k, v), removeItem: k => storage.delete(k) },
        FormData: class {},
        AbortSignal: { timeout: () => ({}) },
        fetch: async () => ({ status: page.status, ok: page.status === 200, json: async () => page.reply })
    });
    const ask = async (question, reply, status = 200) => {
        Object.assign(page, { reply, status });
        elements['chatbot-question'].value = question;
        elements['chatbot-form'].listeners.submit({ preventDefault() {} });
        await new Promise(resolve => setImmediate(resolve));
    };
    const bubbles = () => elements['chatbot-log'].children;
    return { elements, page, ask, bubbles, chip, storage };
}

test('the suggested page is a link inside the answer, with no popup', async () => {
    const chat = load();
    await chat.ask('show menu', { answer: 'Menu', pageUrl: '/Menu', pageLabel: 'Menu', source: 'Local' });
    const link = chat.bubbles()[1].find('a');
    assert.equal(link.href, '/Menu');
    assert.equal(link.textContent, 'Open Menu →');
    assert.deepEqual(chat.page.prompts, []);
});
test('the opening hours page is an approved link', async () => {
    const chat = load();
    await chat.ask('opening hours', { answer: 'Hours', pageUrl: '/Visit', pageLabel: 'Opening hours' });
    assert.equal(chat.bubbles()[1].find('a').href, '/Visit');
});
test('external or unapproved pages never become links', async () => {
    for (const url of ['https://example.com', '//example.com', '/Admin/Announcements', 'javascript:alert(1)']) {
        const chat = load();
        await chat.ask('hi', { answer: 'Answer', pageUrl: url, pageLabel: 'Page' });
        assert.equal(chat.bubbles()[1].find('a'), undefined);
    }
});
test('how-to steps are shown as a numbered list', async () => {
    const chat = load();
    await chat.ask('how do I book', { answer: 'Here is how', steps: ['Open My reservations', 'Press Book'] });
    const list = chat.bubbles()[1].find('ol');
    assert.deepEqual(list.children.map(li => li.textContent), ['Open My reservations', 'Press Book']);
});
test('answer text is assigned as text and AI answers are labelled', async () => {
    const chat = load();
    await chat.ask('hello', { answer: '<img src=x onerror=alert(1)>', source: 'AI' });
    const bubble = chat.bubbles()[1];
    assert.equal(bubble.children[0].textContent, 'AI answer');
    assert.equal(bubble.children[1].textContent, '<img src=x onerror=alert(1)>');
});
test('the history survives a page change and is kept per user', async () => {
    const storage = new Map();
    await load(storage).ask('show menu', { answer: 'Menu', pageUrl: '/Menu', pageLabel: 'Menu' });
    assert.equal(load(storage).bubbles().length, 2);
    assert.equal(load(storage, 'someone@else.com').bubbles().length, 0);
});
test('tampered history still cannot create an unapproved link', () => {
    const storage = new Map([['tg-chat:darren@gmail.com', JSON.stringify([{ from: 'bot', text: 'x', pageUrl: 'https://evil.example' }])]]);
    assert.equal(load(storage).bubbles()[0].find('a'), undefined);
});
test('clearing the chat empties the log and the saved history', async () => {
    const chat = load();
    await chat.ask('show menu', { answer: 'Menu' });
    chat.elements['chatbot-clear'].listeners.click();
    assert.equal(chat.bubbles().length, 0);
    assert.equal(chat.storage.get('tg-chat:darren@gmail.com'), '[]');
    assert.equal(chat.elements['chatbot-suggestions'].hidden, false);
});
test('a suggestion chip asks its question', async () => {
    const chat = load();
    chat.page.reply = { answer: 'Steps' };
    await chat.chip.listeners.click();
    assert.equal(chat.bubbles()[0].children[0].textContent, 'How do I book a table?');
});
test('rate limiting leaves the form usable with an explanation', async () => {
    const chat = load();
    await chat.ask('show menu', {}, 429);
    assert.match(chat.elements['chatbot-state'].textContent, /wait a minute/);
    assert.equal(chat.elements['chatbot-send'].disabled, false);
});
test('a failed response adds no answer', async () => {
    const chat = load();
    await chat.ask('show menu', {}, 500);
    assert.equal(chat.bubbles().length, 1);
    assert.equal(chat.elements['chatbot-send'].disabled, false);
});
