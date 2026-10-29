"use strict";
/* =============================================================================
   Three Grills floor plan (Alex, Part 20)

   <div data-floor-plan="pick" data-source="planJson">  Book a table: click a free table to choose it.
   <div data-floor-plan="edit" data-source="planJson">  Admin layout editor: drag, rotate, add tables.

   Units are grid boxes (1 box = 0.5 m). The geometry matches Services/FloorPlanRules.cs; the server checks
   every save again, so this script only gives instant feedback.
   ============================================================================= */
(function () {
    const NS = "http://www.w3.org/2000/svg";
    const GAP = 1;

    function el(tag, attrs, parent) {
        const n = document.createElementNS(NS, tag);
        for (const k in attrs) { n.setAttribute(k, attrs[k]); }
        if (parent) { parent.appendChild(n); }
        return n;
    }

    function text(parent, x, y, s, size, cls, rotate) {
        const t = el("text", { x: x, y: y, "font-size": size, "text-anchor": "middle", class: cls || "" }, parent);
        if (rotate) { t.setAttribute("transform", "rotate(" + rotate + " " + x + " " + y + ")"); }
        t.textContent = s;
        return t;
    }

    function footprint(seats, rotation) {
        const size = seats <= 2 ? [3, 3] : seats <= 4 ? [4, 4] : [2 + Math.ceil(seats / 2), 4];
        return rotation === 90 ? [size[1], size[0]] : size;
    }

    function boxOf(t) { const s = footprint(t.capacity, t.rotation); return { x: t.x, y: t.y, w: s[0], h: s[1] }; }
    function overlaps(a, b, gap) { gap = gap || 0; return a.x < b.x + b.w + gap && b.x < a.x + a.w + gap && a.y < b.y + b.h + gap && b.y < a.y + a.h + gap; }
    function blocks(area) { return area.features.filter(f => f.kind === "Block" && f.width > 0 && f.height > 0).map(f => ({ x: f.x, y: f.y, w: f.width, h: f.height, label: f.label })); }

    // ---------- drawing ----------
    function drawRoom(svg, area, grid) {
        svg.setAttribute("viewBox", "-1 -1.4 " + (area.width + 2) + " " + (area.height + 2.8));
        svg.replaceChildren();
        const defs = el("defs", {}, svg);
        const pat = el("pattern", { id: svg.id + "-hatch", width: .5, height: .5, patternUnits: "userSpaceOnUse", patternTransform: "rotate(45)" }, defs);
        el("rect", { width: .5, height: .5, class: "fp-taken-bg" }, pat);
        el("line", { x1: 0, y1: 0, x2: 0, y2: .5, class: "fp-taken-stripe" }, pat);
        el("rect", { x: 0, y: 0, width: area.width, height: area.height, rx: .3, class: "fp-floor" }, svg);
        if (grid) {
            const g = el("g", { class: "fp-grid" }, svg);
            for (let x = 1; x < area.width; x++) { el("line", { x1: x, y1: 0, x2: x, y2: area.height }, g); }
            for (let y = 1; y < area.height; y++) { el("line", { x1: 0, y1: y, x2: area.width, y2: y }, g); }
        }
        // Blocks (bar, kitchen) first, then the wall on top so it stays a clean outline, then windows and doors on the wall.
        // Clipped to the room's rounded outline, so a block in a corner never pokes past the wall.
        const clip = el("clipPath", { id: svg.id + "-room" }, defs);
        el("rect", { x: 0, y: 0, width: area.width, height: area.height, rx: .3 }, clip);
        const blocksLayer = el("g", { "clip-path": "url(#" + svg.id + "-room)" }, svg);
        for (const f of area.features.filter(x => x.kind !== "Window" && x.kind !== "Door")) {
            el("rect", { x: f.x, y: f.y, width: f.width, height: f.height, class: "fp-block" }, blocksLayer);
            text(svg, f.x + f.width / 2, f.y + f.height / 2 + .18, f.label, .5, "fp-block-label", f.height > f.width * 1.5 ? -90 : 0);
        }
        el("rect", { x: 0, y: 0, width: area.width, height: area.height, rx: .3, class: "fp-wall" }, svg);
        for (const f of area.features) {
            if (f.kind === "Window") {
                el("line", { x1: f.x, y1: f.y, x2: f.x + f.width, y2: f.y + f.height, class: "fp-window" }, svg);
                text(svg, f.x + f.width / 2, f.y === 0 ? -.45 : f.y + .9, f.label, .5, "fp-note");
            } else if (f.kind === "Door") {
                const vertical = f.width === 0;
                el("line", { x1: f.x, y1: f.y, x2: f.x + f.width, y2: f.y + f.height, class: "fp-door" }, svg);
                if (vertical) { text(svg, f.x + (f.x === 0 ? -.55 : .55), f.y + f.height / 2, f.label, .45, "fp-note", f.x === 0 ? -90 : 90); }
                else { text(svg, f.x + f.width / 2, f.y + (f.y === 0 ? -.45 : .85), f.label, .5, "fp-note"); }
            }
        }
    }

    function drawTable(svg, t, cls, sub) {
        const s = footprint(t.capacity, t.rotation), w = s[0], h = s[1];
        const g = el("g", { class: "fp-table " + cls, transform: "translate(" + t.x + " " + t.y + ")", "data-id": t.key }, svg);
        const inset = .75, bw = w - 2 * inset, bh = h - 2 * inset;
        const chairs = [];
        if (t.capacity <= 2) {
            chairs.push([w / 2, .35]);
            if (t.capacity === 2) { chairs.push([w / 2, h - .35]); }
        } else if (t.capacity <= 4) {
            const sides = [[w / 2, .35], [w / 2, h - .35], [.35, h / 2], [w - .35, h / 2]];
            for (let i = 0; i < t.capacity; i++) { chairs.push(sides[i]); }
        } else {
            const long = w >= h, top = Math.ceil(t.capacity / 2), bottom = t.capacity - top, span = long ? bw : bh;
            for (let i = 0; i < top; i++) { const p = inset + (i + .5) * span / top; chairs.push(long ? [p, .35] : [.35, p]); }
            for (let i = 0; i < bottom; i++) { const p = inset + (i + .5) * span / bottom; chairs.push(long ? [p, h - .35] : [w - .35, p]); }
        }
        // Small tables are square, so turning them 90° does not change their footprint: turn the chairs instead
        // (a 2-seat table then has its chairs left and right rather than top and bottom).
        if (t.capacity <= 4 && t.rotation === 90) { for (let i = 0; i < chairs.length; i++) { chairs[i] = [w - chairs[i][1], chairs[i][0]]; } }
        for (const c of chairs) { el("rect", { x: c[0] - .3, y: c[1] - .3, width: .6, height: .6, rx: .18, class: "fp-chair" }, g); }
        if (t.capacity <= 2) { el("circle", { cx: w / 2, cy: h / 2, r: Math.min(bw, bh) / 2 + .1, class: "fp-top" }, g); }
        else { el("rect", { x: inset, y: inset, width: bw, height: bh, rx: .25, class: "fp-top" }, g); }
        text(g, w / 2, h / 2 - .02, t.label, .62, "fp-label");
        text(g, w / 2, h / 2 + .62, sub, .4, "fp-sub");
        return g;
    }

    function readJson(id) { const s = document.getElementById(id); return s ? JSON.parse(s.textContent) : null; }

    function areaTabs(holder, areas, current, onPick) {
        holder.replaceChildren();
        if (areas.length < 2) { holder.hidden = true; return; }
        for (const a of areas) {
            const b = document.createElement("button");
            b.type = "button"; b.textContent = a.name; b.setAttribute("aria-pressed", String(a.id === current));
            b.addEventListener("click", () => onPick(a.id));
            holder.appendChild(b);
        }
    }

    // ---------- Book a table: pick a free table ----------
    function initPick(root) {
        const plan = readJson(root.dataset.source);
        if (!plan || plan.areas.length === 0) { root.hidden = true; return; }
        const svg = root.querySelector("svg"), tabs = root.querySelector("[data-areas]"), summary = root.querySelector("[data-summary]");
        const radios = Array.from(document.querySelectorAll('input[name="' + root.dataset.input + '"]'));
        const tables = plan.areas.flatMap(a => a.tables.map(t => Object.assign({}, t, { key: t.id, areaId: a.id, areaName: a.name })));
        const free = tables.filter(t => t.state === "Free");
        const best = free.length ? Math.min.apply(null, free.map(t => t.capacity)) : 0;
        const checked = () => { const r = radios.find(x => x.checked); return r ? +r.value : 0; };
        let areaId = (tables.find(t => t.id === checked()) || free[0] || tables[0]).areaId;

        function choose(id) {
            const r = radios.find(x => +x.value === id);
            if (r) { r.checked = true; r.dispatchEvent(new Event("change", { bubbles: true })); }
            render();
        }

        function render() {
            const area = plan.areas.find(a => a.id === areaId);
            areaTabs(tabs, plan.areas, areaId, id => { areaId = id; render(); });
            drawRoom(svg, area, false);
            const picked = checked();
            for (const t of tables.filter(x => x.areaId === areaId)) {
                const sel = t.id === picked;
                const sub = t.state === "Taken" ? "Booked" : t.state === "Small" ? "Only " + t.capacity + " seats" : t.capacity + " seats";
                const g = drawTable(svg, t, "is-" + t.state.toLowerCase() + (sel ? " is-selected" : ""), sub);
                if (t.state === "Taken") { g.querySelector(".fp-top").setAttribute("fill", "url(#" + svg.id + "-hatch)"); }
                if (t.state === "Free" && t.capacity === best) {
                    const w = footprint(t.capacity, t.rotation)[0];
                    el("circle", { cx: w - .55, cy: .55, r: .42, class: "fp-best" }, g);
                    text(g, w - .55, .7, "★", .45, "fp-best-star");
                }
                const label = "Table " + t.label + ", " + t.capacity + " seats, " + t.areaName + ", " +
                    (t.state === "Taken" ? "already booked at this time" : t.state === "Small" ? "too small for your group" : "free" + (t.capacity === best ? ", best fit" : ""));
                el("title", {}, g).textContent = label;
                if (t.state === "Free") {
                    g.setAttribute("role", "radio"); g.setAttribute("aria-checked", String(sel)); g.setAttribute("tabindex", sel ? "0" : "-1");
                    g.setAttribute("aria-label", label);
                    g.addEventListener("click", () => choose(t.id));
                    g.addEventListener("keydown", e => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); choose(t.id); } });
                } else {
                    g.setAttribute("aria-hidden", "true");
                }
            }
            const chosen = tables.find(t => t.id === picked);
            if (summary) {
                summary.textContent = chosen ? "Table " + chosen.label + " · " + chosen.capacity + " seats · " + chosen.areaName : "Tap a gold table to choose it.";
            }
            if (!svg.querySelector('.fp-table[tabindex="0"]')) { const first = svg.querySelector(".fp-table.is-free"); if (first) { first.setAttribute("tabindex", "0"); } }
        }

        radios.forEach(r => r.addEventListener("change", () => {
            const t = tables.find(x => x.id === +r.value);
            if (t && t.areaId !== areaId) { areaId = t.areaId; }
            render();
        }));
        render();
    }

    // ---------- Admin layout editor ----------
    function initEdit(root) {
        const area = readJson(root.dataset.source);
        if (!area) { return; }
        const svg = root.querySelector("svg"), form = root.closest("form"), field = form.querySelector('[name="LayoutJson"]');
        const $ = s => root.querySelector(s);
        let nextKey = -1, selected = null, dirty = false, history = [], future = [];
        let tables = area.tables.map(t => Object.assign({}, t, { key: t.id }));
        const snapshot = () => JSON.stringify(tables);
        const commit = before => { if (before !== snapshot()) { history.push(before); future = []; dirty = true; } };
        const clampTo = t => { const s = footprint(t.capacity, t.rotation); t.x = Math.max(0, Math.min(area.width - s[0], t.x)); t.y = Math.max(0, Math.min(area.height - s[1], t.y)); };

        function problems() {
            const bad = new Set(), live = tables.filter(t => t.isActive), obs = blocks(area);
            for (const t of live) {
                const b = boxOf(t);
                if (b.x < 0 || b.y < 0 || b.x + b.w > area.width || b.y + b.h > area.height) { bad.add(t.key); }
                if (obs.some(o => overlaps(b, o))) { bad.add(t.key); }
                if (live.some(o => o.key !== t.key && overlaps(b, boxOf(o), GAP))) { bad.add(t.key); }
            }
            return bad;
        }

        function render() {
            drawRoom(svg, area, true);
            const bad = problems();
            for (const t of tables) {
                const cls = (t.isActive ? "" : "is-inactive ") + (bad.has(t.key) ? "is-bad " : "") + (selected === t.key ? "is-selected" : "");
                const g = drawTable(svg, t, "is-edit " + cls, t.capacity + " seats");
                g.setAttribute("role", "button"); g.setAttribute("tabindex", "0");
                g.setAttribute("aria-label", "Table " + t.label + ", " + t.capacity + " seats" + (t.isActive ? "" : ", inactive") + (bad.has(t.key) ? ", too close" : "") + ". Drag or use the arrow keys to move, R to turn.");
                g.addEventListener("pointerdown", e => startDrag(e, t, g));
                g.addEventListener("keydown", e => keyMove(e, t));
                g.addEventListener("focus", () => { if (selected !== t.key) { selected = t.key; renderProps(); syncButtons(bad); } });
            }
            syncButtons(bad);
            renderProps();
        }

        function syncButtons(bad) {
            $("[data-undo]").disabled = !history.length;
            $("[data-redo]").disabled = !future.length;
            $("[data-rotate]").disabled = selected === null;
            const sel = tables.find(t => t.key === selected);
            $("[data-remove]").disabled = !sel || sel.id !== 0;
            $("[data-save]").disabled = bad.size > 0 || !dirty;
            const status = $("[data-status]");
            status.classList.toggle("is-dirty", dirty || bad.size > 0);
            status.textContent = bad.size ? bad.size + (bad.size > 1 ? " tables are" : " table is") + " too close or overlapping — move before saving"
                : dirty ? "Unsaved changes" : "All changes saved";
        }

        function point(e) { const p = svg.createSVGPoint(); p.x = e.clientX; p.y = e.clientY; return p.matrixTransform(svg.getScreenCTM().inverse()); }
        function focusTable(key) { const g = svg.querySelector('.fp-table[data-id="' + key + '"]'); if (g) { g.focus({ preventScroll: true }); } }

        function startDrag(e, t, g) {
            const p = point(e), before = snapshot(), dx = p.x - t.x, dy = p.y - t.y;
            selected = t.key;
            g.setPointerCapture(e.pointerId); g.classList.add("is-dragging");
            const move = ev => {
                const q = point(ev), oldX = t.x, oldY = t.y;
                t.x = Math.round(q.x - dx); t.y = Math.round(q.y - dy); clampTo(t);
                if (t.x !== oldX || t.y !== oldY) { g.setAttribute("transform", "translate(" + t.x + " " + t.y + ")"); }
            };
            const up = () => {
                g.removeEventListener("pointermove", move); g.removeEventListener("pointerup", up); g.removeEventListener("pointercancel", up);
                commit(before); render(); focusTable(t.key);
            };
            g.addEventListener("pointermove", move); g.addEventListener("pointerup", up); g.addEventListener("pointercancel", up);
        }

        function keyMove(e, t) {
            selected = t.key;   // a key press on a table selects it, even where the browser sent no focus event
            const step = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] }[e.key];
            if (e.key === "r" || e.key === "R") { e.preventDefault(); rotate(); return; }
            if (!step) { return; }
            e.preventDefault();
            const before = snapshot(); t.x += step[0]; t.y += step[1]; clampTo(t); commit(before); render(); focusTable(t.key);
        }

        function rotate() {
            const t = tables.find(x => x.key === selected); if (!t) { return; }
            const before = snapshot(); t.rotation = t.rotation === 90 ? 0 : 90; clampTo(t); commit(before); render(); focusTable(t.key);
        }

        function add(seats) {
            // New labels follow the area's existing ones (T.. in the Main Hall, P.. on the Patio).
            const counts = {};
            tables.forEach(x => { const c = (x.label.match(/^[A-Za-z]+/) || [""])[0].toUpperCase(); if (c) { counts[c] = (counts[c] || 0) + 1; } });
            const common = Object.keys(counts).sort((a, b) => counts[b] - counts[a])[0];
            const before = snapshot(), prefix = common || (area.name.trim()[0] || "T").toUpperCase();
            const used = new Set(tables.map(t => t.label.toUpperCase()).concat((root.dataset.otherLabels || "").split("|")));
            let n = 1; while (used.has(prefix + String(n).padStart(2, "0"))) { n++; }
            const t = { id: 0, key: nextKey--, label: prefix + String(n).padStart(2, "0"), capacity: seats, x: 0, y: 0, rotation: 0, isActive: true };
            const s = footprint(seats, 0), obs = blocks(area), live = tables.filter(x => x.isActive);
            let placed = false;
            for (let y = 0; y + s[1] <= area.height && !placed; y++) {
                for (let x = 0; x + s[0] <= area.width && !placed; x++) {
                    const b = { x: x, y: y, w: s[0], h: s[1] };
                    if (!obs.some(o => overlaps(b, o)) && !live.some(o => overlaps(b, boxOf(o), GAP))) { t.x = x; t.y = y; placed = true; }
                }
            }
            tables.push(t); selected = t.key; commit(before);
            message(placed ? t.label + " added. Drag it into place, then save." : "There is no free space for a " + seats + "-seat table. It was put at the top left; move other tables to make room.", !placed);
            render(); focusTable(t.key);
        }

        function remove() {
            const t = tables.find(x => x.key === selected);
            if (!t || t.id !== 0) { return; }   // saved tables are deactivated, never deleted
            const before = snapshot(); tables = tables.filter(x => x.key !== t.key); selected = null; commit(before); message("", false); render();
        }

        function message(s, bad) { const m = $("[data-message]"); m.textContent = s; m.hidden = !s; m.classList.toggle("is-bad", !!bad); }

        function renderProps() {
            const box = $("[data-props]"), t = tables.find(x => x.key === selected);
            if (!t) { box.innerHTML = '<p class="text-secondary mb-0">Click a table on the plan to edit it.</p>'; delete box.dataset.for; return; }
            if (box.dataset.for === String(t.key) && box.contains(document.activeElement)) { return; }
            box.dataset.for = String(t.key);
            box.innerHTML =
                '<div class="fp-props">' +
                '<label class="form-label" for="fp-label">Label</label><input class="form-control" id="fp-label" maxlength="20">' +
                '<label class="form-label" for="fp-seats">Seats</label><input class="form-control" id="fp-seats" type="number" min="1" max="20">' +
                '<div class="form-check"><input class="form-check-input" type="checkbox" id="fp-active"><label class="form-check-label" for="fp-active">Active (customers can book it)</label></div>' +
                '<p class="small text-secondary mb-0">' + (t.id === 0 ? "New table: not saved yet." : "Saved tables cannot be deleted; untick Active to stop bookings.") + '</p></div>';
            const label = box.querySelector("#fp-label"), seats = box.querySelector("#fp-seats"), active = box.querySelector("#fp-active");
            label.value = t.label; seats.value = t.capacity; active.checked = t.isActive;
            const edit = fn => { const before = snapshot(); fn(); clampTo(t); commit(before); render(); };
            label.addEventListener("change", () => { const v = label.value.trim(); if (v) { edit(() => { t.label = v; }); } else { label.value = t.label; } });
            seats.addEventListener("change", () => { const v = Math.max(1, Math.min(20, parseInt(seats.value, 10) || t.capacity)); edit(() => { t.capacity = v; }); });
            active.addEventListener("change", () => edit(() => { t.isActive = active.checked; }));
        }

        root.querySelectorAll("[data-add]").forEach(b => b.addEventListener("click", () => add(+b.dataset.add)));
        $("[data-rotate]").addEventListener("click", rotate);
        $("[data-remove]").addEventListener("click", remove);
        $("[data-undo]").addEventListener("click", () => { if (history.length) { future.push(snapshot()); tables = JSON.parse(history.pop()); dirty = true; render(); } });
        $("[data-redo]").addEventListener("click", () => { if (future.length) { history.push(snapshot()); tables = JSON.parse(future.pop()); dirty = true; render(); } });
        svg.addEventListener("pointerdown", e => { if (!e.target.closest(".fp-table")) { selected = null; render(); } });
        form.addEventListener("submit", () => {
            field.value = JSON.stringify(tables.map(t => ({ id: t.id, label: t.label, capacity: t.capacity, x: t.x, y: t.y, rotation: t.rotation, isActive: t.isActive })));
            dirty = false;
        });
        window.addEventListener("beforeunload", e => { if (dirty) { e.preventDefault(); e.returnValue = ""; } });
        render();
    }

    document.querySelectorAll("[data-floor-plan]").forEach(root => {
        if (root.dataset.floorPlan === "edit") { initEdit(root); } else { initPick(root); }
    });
})();
