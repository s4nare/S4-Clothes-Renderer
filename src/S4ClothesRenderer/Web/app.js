const $ = (id) => document.getElementById(id);
const api = (url, body) => fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body || {}) }).then((r) => r.json());
const fmt = (n) => n.toLocaleString(LANG === 'fr' ? 'fr-FR' : 'en-US').replace(/\s/g, ' ');
const duration = (s) => s >= 3600 ? Math.floor(s / 3600) + ' h ' + String(Math.floor(s % 3600 / 60)).padStart(2, '0')
    : s >= 60 ? Math.floor(s / 60) + ' min ' + String(s % 60).padStart(2, '0') : s + ' s';
const icon = (id, cls) => '<svg class="i ' + (cls || '') + '"><use href="#' + id + '"/></svg>';
const errorText = (code) => t('err.' + code) === 'err.' + code ? code : t('err.' + code);
const STORE = 's4_clothes_renderer_ui';

const state = {
    categories: [], folders: [], counts: null, summary: null, running: false, errors: 0,
    sex: 'male', cat: 'jbib', filter: 'all', items: [], refs: false,
    useGame: true, gamePath: '', game: null,
    kind: 'clothes', objCounts: null, groups: null, groupByKind: {}, groupsPicked: {},
    opts: { colors: 'first', naming: 'cat', size: '512', force: 'false', head: 'false' },
};

const ui = JSON.parse(localStorage.getItem(STORE) || 'null');

function options() {
    return {
        useGame: state.useGame, folders: state.folders, out: $('out').value.trim(),
        sexes: [...document.querySelectorAll('[name=sex]:checked')].map((e) => e.value),
        cats: [...document.querySelectorAll('[name=pick]:checked')].map((e) => e.value),
        colors: state.opts.colors, naming: state.opts.naming, size: +state.opts.size, force: state.opts.force === 'true',
        head: state.opts.head === 'true',
        kind: state.kind, groups: [...document.querySelectorAll('[name=group]:checked')].map((e) => e.value),
    };
}

const isObjects = () => state.kind !== 'clothes';
const KINDS = ['clothes', 'vehicles', 'weapons', 'props', 'peds'];

function grpLabel(kind, group) {
    const key = 'grp.' + kind + '.' + group;
    const text = t(key);
    if (text !== key) return text;
    if (group.length <= 3) return group.toUpperCase();
    const g = group.replace(/_/g, ' ');
    return g.charAt(0).toUpperCase() + g.slice(1);
}

function kindGroups() {
    const counts = state.objCounts && state.objCounts[state.kind];
    return counts ? Object.keys(counts).sort((a, b) => grpLabel(state.kind, a).localeCompare(grpLabel(state.kind, b))) : [];
}

function renderKinds() {
    document.body.classList.toggle('objects', isObjects());
    document.body.classList.toggle('vehicles', state.kind === 'vehicles');
    document.querySelectorAll('[data-kinds]').forEach((box) => {
        box.innerHTML = '';
        box.classList.remove('open');
        const current = document.createElement('button');
        current.className = 'kind-current';
        current.innerHTML = '<span></span>' + icon('down');
        current.querySelector('span').textContent = t('kind.' + state.kind);
        current.disabled = state.running && !!box.closest('.settings');
        current.onclick = (e) => {
            e.stopPropagation();
            const open = !box.classList.contains('open');
            document.querySelectorAll('[data-kinds].open').forEach((o) => o.classList.remove('open'));
            box.classList.toggle('open', open);
        };
        const menu = document.createElement('div');
        menu.className = 'kind-menu';
        for (const k of KINDS) {
            const b = document.createElement('button');
            b.textContent = t('kind.' + k);
            b.setAttribute('aria-checked', String(k === state.kind));
            b.onclick = () => { box.classList.remove('open'); setKind(k); };
            menu.append(b);
        }
        box.append(current, menu);
    });
}

document.addEventListener('click', () => document.querySelectorAll('[data-kinds].open').forEach((o) => o.classList.remove('open')));

function setKind(kind) {
    if (state.kind === kind) return;
    state.kind = kind;
    state.summary = null;
    state.groups = null;
    if (isObjects() && !state.groupByKind[kind]) state.groupByKind[kind] = kindGroups()[0] || '';
    renderKinds();
    renderGroupPicks();
    save();
    renderNav();
    renderEstimate();
    loadSummary();
    loadCategory();
}

let saveTimer = null;
function save() {
    const o = options();
    if (isObjects() && state.objCounts) state.groupsPicked[state.kind] = o.groups;
    localStorage.setItem(STORE, JSON.stringify({
        sexes: o.sexes, cats: o.cats,
        sex: state.sex, cat: state.cat, thumb: $('thumb').value,
        kind: state.kind, groupByKind: state.groupByKind, groupsPicked: state.groupsPicked,
    }));
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => api('/api/settings', {
        useGame: state.useGame, gamePath: state.gamePath, folders: state.folders, out: o.out,
        naming: o.naming, colors: o.colors, size: o.size, force: o.force, head: o.head, lang: LANG,
    }).then((r) => setRefs(!!r.refs)), 200);
}

function toast(text, kind) {
    const el = document.createElement('div');
    el.className = 'toast ' + (kind || '');
    el.innerHTML = '<i></i><span></span>';
    el.querySelector('span').textContent = text;
    $('toasts').append(el);
    setTimeout(() => el.remove(), 5000);
}

function log(text, bad) {
    const el = $('log');
    const lines = (el.textContent ? el.textContent.split('\n') : []).concat(text);
    el.textContent = lines.slice(-400).join('\n');
    el.scrollTop = el.scrollHeight;
    if (bad && !$('journal').classList.contains('on')) {
        state.errors++;
        $('badge').textContent = state.errors;
        $('badge').classList.add('on');
    }
}

function emptyState(el, iconId, text, action) {
    el.innerHTML = '<div class="empty">' + icon(iconId) + '<div></div></div>';
    el.querySelector('.empty div').textContent = text;
    if (action) el.querySelector('.empty').append(action);
}

function applyBranding(b) {
    const hex = /^#?([0-9a-f]{6})$/i.exec((b.color || '').trim());
    if (hex) document.documentElement.style.setProperty('--accent-rgb', [0, 2, 4].map((i) => parseInt(hex[1].slice(i, i + 2), 16)).join(', '));
    $('brand-name').textContent = b.name;
    document.title = b.name;
    $('logo').style.transform = 'scale(' + (b.logoZoom || 1) + ')';
}

function setView(v) {
    document.querySelectorAll('.nav-link[data-view]').forEach((b) => b.classList.toggle('active', b.dataset.view === v));
    $('view-gallery').classList.toggle('on', v === 'gallery');
    $('view-render').classList.toggle('on', v === 'render');
    if (v === 'gallery') loadCategory();
}

function setSegment(group, value) {
    group.querySelectorAll('button').forEach((b) => b.setAttribute('aria-checked', String((b.dataset.value || b.dataset.sex || b.dataset.filter) === value)));
}

const catLabel = (key) => t('cat.' + key);
const catSlot = (key) => (state.categories.find((c) => c.key === key) || {}).slot || '';

function renderNav() {
    if (isObjects()) {
        $('nav-cloth-title').textContent = t('kind.' + state.kind);
        const box = $('nav-cloth');
        box.innerHTML = '';
        for (const g of kindGroups()) {
            const row = document.createElement('button');
            row.className = 'list-row' + (g === state.groupByKind[state.kind] ? ' active' : '');
            row.innerHTML = '<span class="name"></span><span class="cnt"></span>';
            row.querySelector('.name').textContent = grpLabel(state.kind, g);
            const s = state.groups && state.groups[g];
            const cnt = row.querySelector('.cnt');
            if (!s) cnt.textContent = state.groups ? '—' : fmt(state.objCounts[state.kind][g]);
            else { cnt.textContent = fmt(s[1]) + ' / ' + fmt(s[0]); cnt.classList.toggle('full', s[1] === s[0]); }
            row.onclick = () => { state.groupByKind[state.kind] = g; save(); renderNav(); loadCategory(); };
            box.append(row);
        }
        return;
    }
    $('nav-cloth-title').textContent = t('group.clothes');
    for (const prop of [false, true]) {
        const box = $(prop ? 'nav-prop' : 'nav-cloth');
        box.innerHTML = '';
        for (const c of state.categories.filter((c) => c.prop === prop)) {
            const row = document.createElement('button');
            row.className = 'list-row' + (c.key === state.cat ? ' active' : '');
            row.innerHTML = '<span class="slot"></span><span class="name"></span><span class="cnt"></span>';
            row.querySelector('.slot').innerHTML = icon('c-' + c.key);
            row.querySelector('.name').textContent = catLabel(c.key);
            const s = state.summary && state.summary[state.sex] && state.summary[state.sex][c.key];
            const cnt = row.querySelector('.cnt');
            if (!state.summary) cnt.textContent = '…';
            else if (!s) { cnt.textContent = '—'; row.classList.add('empty'); }
            else { cnt.textContent = fmt(s[1]) + ' / ' + fmt(s[0]); cnt.classList.toggle('full', s[1] === s[0]); }
            row.onclick = () => { state.cat = c.key; save(); renderNav(); loadCategory(); };
            box.append(row);
        }
    }
}

function loadSummary() {
    const kind = state.kind;
    api('/api/summary', options()).then((r) => {
        if (kind !== state.kind) return;
        state.summary = r.counts || null;
        state.groups = r.groups || null;
        if (r.refs != null) setRefs(r.refs);
        renderNav();
        renderCatHead();
    });
}

function renderCatHead() {
    const done = state.items.filter((i) => i.url).length, total = state.items.length;
    const group = state.groupByKind[state.kind] || '';
    $('cat-title').textContent = isObjects() ? (group ? grpLabel(state.kind, group) : t('kind.' + state.kind)) : catLabel(state.cat);
    const parts = isObjects()
        ? [t('kind.' + state.kind), t('gallery.images', { n: fmt(total) }), t('gallery.rendered', { n: fmt(done) })]
        : [t('sex.' + state.sex), t('gallery.component', { slot: catSlot(state.cat).replace('prop_', 'prop ') }), t('gallery.images', { n: fmt(total) }), t('gallery.rendered', { n: fmt(done) })];
    $('cat-sub').innerHTML = parts.map(() => '<span></span>').join('<span class="sep"></span>');
    $('cat-sub').querySelectorAll('span:not(.sep)').forEach((el, i) => { el.textContent = parts[i]; });
    $('cat-bar').style.width = (total ? done / total * 100 : 0) + '%';
    $('render-cat').disabled = state.running || !total;
}

function makeTile(item) {
    const tile = document.createElement('div');
    tile.dataset.out = item.out;
    fillTile(tile, item);
    return tile;
}

function fillTile(tile, item) {
    tile.className = 'tile' + (item.url ? '' : ' todo');
    tile.innerHTML = '<div class="pic"></div><div class="meta"><b></b><span></span></div>';
    tile.querySelector('b').textContent = item.label;
    tile.querySelector('.meta span').textContent = item.source;
    tile.title = item.source;
    if (item.url) {
        tile.querySelector('.pic').innerHTML = '<img alt="" loading="lazy">';
        tile.querySelector('img').src = item.url;
        tile.dataset.url = item.url;
        tile.onclick = () => openLightbox(tile);
    } else {
        tile.querySelector('.pic').innerHTML = '<img class="unknown" alt="" src="undefined.png">';
        tile.querySelector('.pic img').title = t('gallery.todo');
        delete tile.dataset.url;
        tile.onclick = null;
    }
}

function renderGallery() {
    const grid = $('gallery');
    const find = $('find').value.trim().toLowerCase();
    const items = state.items.filter((i) => (state.filter === 'all' || (state.filter === 'done') === !!i.url) && (!find || i.label.toLowerCase().startsWith(find)));
    grid.innerHTML = '';
    if (!state.items.length) {
        if (state.useGame && state.game && state.game.loading) return emptyState(grid, 'hourglass', t('gallery.loadingGame'));
        const go = document.createElement('button');
        go.className = 'btn';
        go.innerHTML = icon('spark') + '<span></span>';
        go.querySelector('span').textContent = t('gallery.settings');
        go.onclick = () => setView('render');
        return emptyState(grid, 'image', t('gallery.empty'), go);
    }
    if (!items.length) return emptyState(grid, 'search', t('gallery.noMatch'));
    const frag = document.createDocumentFragment();
    for (const item of items) frag.append(makeTile(item));
    grid.append(frag);
}

let loadToken = 0;
function loadCategory() {
    const token = ++loadToken;
    api('/api/category', { ...options(), sex: state.sex, cat: state.cat, group: state.groupByKind[state.kind] || '' }).then((r) => {
        if (token !== loadToken) return;
        if (r.error) { state.items = []; renderCatHead(); return emptyState($('gallery'), 'image', errorText(r.error)); }
        state.items = r.items;
        renderCatHead();
        renderGallery();
        $('gallery').scrollTop = 0;
    });
}

function renderGame() {
    const g = state.game || {};
    $('game-row').classList.toggle('on', state.useGame);
    $('game-spin').classList.toggle('on', !!g.loading);
    const status = $('game-status');
    status.className = '';
    if (!state.gamePath) { status.textContent = t('game.notFound'); status.className = 'warn'; }
    else if (g.loading) status.textContent = t('game.loading', { status: g.status || '' });
    else if (g.error === 'game_invalid' || g.valid === false) { status.textContent = t('game.invalid'); status.className = 'bad'; }
    else if (g.error) { status.textContent = t('game.error', { error: g.error }); status.className = 'bad'; }
    else if (g.loaded) { status.textContent = t('game.loaded', { n: fmt(g.files), path: state.gamePath }); status.className = 'ok'; }
    else status.textContent = t('game.idle', { path: state.gamePath });
    status.title = state.gamePath;
}

function loadGame() {
    if (!state.useGame || !state.gamePath) return renderGame();
    api('/api/game', { path: state.gamePath }).then((g) => { state.game = g; renderGame(); });
}

function renderSources() {
    const box = $('sources');
    box.innerHTML = '';
    if (!state.folders.length) {
        box.innerHTML = '<div class="hint"></div>';
        box.firstChild.textContent = t('src.none');
        return;
    }
    state.folders.forEach((s, i) => {
        const row = document.createElement('div');
        row.className = 'src-row';
        row.title = s;
        row.innerHTML = icon('folder') + '<span></span><em>' + (i + 1) + '</em><button class="icon-btn">' + icon('x') + '</button>';
        row.querySelector('span').textContent = s;
        row.querySelector('button').title = t('src.remove');
        row.querySelector('button').onclick = () => { if (state.running) return; state.folders.splice(i, 1); sourcesChanged(); };
        box.append(row);
    });
}

function sourcesChanged() {
    renderSources();
    renderGame();
    save();
    state.counts = null;
    renderEstimate();
    api('/api/scan', options()).then((r) => {
        state.counts = r.counts;
        state.objCounts = r.objects;
        state.objPaints = r.paints;
        for (const k of KINDS.slice(1)) {
            const groups = Object.keys((r.objects || {})[k] || {});
            if (!groups.includes(state.groupByKind[k])) state.groupByKind[k] = groups[0] || '';
        }
        renderGroupPicks();
        renderNav();
        renderEstimate();
        loadSummary();
        loadCategory();
    });
}

function renderPicks() {
    for (const prop of [false, true]) {
        const box = $(prop ? 'pick-prop' : 'pick-cloth');
        const checked = new Set([...box.querySelectorAll('input:checked')].map((i) => i.value));
        const first = !box.children.length;
        box.innerHTML = '';
        for (const c of state.categories.filter((c) => c.prop === prop)) {
            const label = document.createElement('label');
            label.className = 'chip-check';
            label.title = catLabel(c.key);
            label.innerHTML = '<input type="checkbox" name="pick"><span class="box"></span><span class="lbl"></span><span class="n"></span>';
            label.querySelector('input').value = c.key;
            label.querySelector('input').checked = first ? (ui ? ui.cats.includes(c.key) : true) : checked.has(c.key);
            label.querySelector('.lbl').textContent = catLabel(c.key);
            box.append(label);
        }
    }
}

function renderGroupPicks() {
    const box = $('pick-groups');
    box.innerHTML = '';
    if (!isObjects()) return;
    const picked = state.groupsPicked[state.kind];
    for (const g of kindGroups()) {
        const label = document.createElement('label');
        label.className = 'chip-check';
        label.title = grpLabel(state.kind, g);
        label.innerHTML = '<input type="checkbox" name="group"><span class="box"></span><span class="lbl"></span><span class="n"></span>';
        label.querySelector('input').value = g;
        label.querySelector('input').checked = picked ? picked.includes(g) : true;
        label.querySelector('input').disabled = state.running;
        label.querySelector('.lbl').textContent = grpLabel(state.kind, g);
        box.append(label);
    }
}

function renderEstimate() {
    if (isObjects()) {
        const counts = state.kind === 'vehicles' && state.opts.colors === 'all' ? state.objPaints : state.objCounts && state.objCounts[state.kind];
        let total = 0;
        document.querySelectorAll('[name=group]').forEach((box) => {
            const n = counts ? counts[box.value] || 0 : 0;
            box.parentNode.querySelector('.n').textContent = fmt(n);
            if (box.checked) total += n;
        });
        $('estimate').textContent = state.objCounts ? fmt(total) : '…';
        return;
    }
    const sexes = options().sexes, all = state.opts.colors === 'all';
    let total = 0;
    document.querySelectorAll('[name=pick]').forEach((box) => {
        let models = 0, colors = 0;
        if (state.counts) for (const s of sexes) { const c = state.counts[s][box.value]; if (c) { models += c[0]; colors += c[1]; } }
        box.parentNode.querySelector('.n').textContent = state.counts ? fmt(all ? colors : models) : '…';
        if (box.checked) total += all ? colors : models;
    });
    $('estimate').textContent = state.counts ? fmt(total) : '…';
    const color = all ? '_0' : '';
    $('hint').textContent = state.opts.naming === 'game' ? 'male/11/0' + color + '.png' : 'male/torso2/0' + color + '.png';
}

function setRefs(on) {
    state.refs = on;
    $('refs-row').classList.toggle('ok', on);
    $('refs-text').textContent = t(on ? 'refs.ok' : 'refs.missing');
}

function setResource(path) {
    state.resource = path || '';
    const text = $('res-text');
    text.textContent = state.resource ? t('res.installed', { path: state.resource }) : t('res.none');
    text.className = state.resource ? 'ok' : '';
    text.title = state.resource;
}

function renderGo() {
    $('go').innerHTML = state.running ? icon('stop', 'fill') + '<span></span>' : icon('play', 'fill') + '<span></span>';
    $('go').querySelector('span').textContent = t(state.running ? 'run.stop' : 'run.start');
}

function setRunning(on) {
    state.running = on;
    $('go').classList.toggle('primary', !on);
    $('go').classList.toggle('danger', on);
    $('go').disabled = false;
    renderGo();
    document.querySelectorAll('.settings-scroll input, .settings-scroll .segmented button, #add-src, #pick, #refs-import, #res-install,#game-pick, #game-detect, [data-all]').forEach((el) => { el.disabled = on; });
    $('run-chip').classList.toggle('on', on);
    renderKinds();
    $('render-cat').disabled = on || !state.items.length;
    renderUpdate();
}

let lastProgress = null;
function showProgress(p) {
    lastProgress = p;
    const processed = p.done + p.failed;
    const ratio = p.total ? processed / p.total : 1;
    $('count').innerHTML = fmt(processed) + ' <span>/ ' + fmt(p.total) + '</span>';
    $('pct').textContent = p.total ? Math.floor(ratio * 100) + ' %' : '';
    $('bar').style.width = ratio * 100 + '%';
    $('current').textContent = p.finished ? '' : p.current;
    $('k-done').textContent = fmt(p.done);
    $('k-skip').textContent = fmt(p.skipped);
    $('k-fail').textContent = fmt(p.failed);
    $('k-fail-box').classList.toggle('bad', p.failed > 0);
    $('k-speed').textContent = p.elapsed > 0 && p.done ? (p.done / p.elapsed).toFixed(1) + ' /s' : '—';
    $('k-time-label').textContent = t(p.finished ? 'kpi.duration' : 'kpi.left');
    $('k-time').textContent = p.finished ? duration(p.elapsed) : p.eta != null ? duration(p.eta) : '—';
    $('chip-count').textContent = fmt(processed) + ' / ' + fmt(p.total);
    $('chip-bar').style.width = ratio * 100 + '%';
    $('chip-eta').textContent = p.eta != null ? duration(p.eta) : '';
}

function onImage(img) {
    const live = $('live');
    live.querySelector('.empty')?.remove();
    const atBottom = live.scrollTop + live.clientHeight >= live.scrollHeight - 300;
    const tile = makeTile({ out: img.out, url: img.url, label: img.label, source: img.source });
    tile.dataset.seq = img.seq;
    let before = null;
    for (let el = live.lastElementChild; el && +el.dataset.seq > img.seq; el = el.previousElementSibling) before = el;
    live.insertBefore(tile, before);
    while (live.children.length > 500) live.firstElementChild.remove();
    if (atBottom) live.scrollTop = live.scrollHeight;

    const shown = img.kind === 'clothes' ? state.kind === 'clothes' && img.sex === state.sex && img.comp === state.cat
        : img.kind === state.kind && img.group === state.groupByKind[state.kind];
    if (shown) {
        const item = state.items.find((i) => i.out === img.out);
        if (item) {
            const wasDone = !!item.url;
            item.url = img.url;
            const el = [...$('gallery').children].find((x) => x.dataset.out === img.out);
            if (el) { fillTile(el, item); el.classList.add('fresh'); }
            if (!wasDone) renderCatHead();
        }
    }
    const s = img.kind === 'clothes' ? state.kind === 'clothes' && state.summary && state.summary[img.sex] && state.summary[img.sex][img.comp]
        : img.kind === state.kind && state.groups && state.groups[img.group];
    if (s && s[1] < s[0]) { s[1]++; scheduleNav(); }
}

let navTimer = null;
function scheduleNav() {
    if (navTimer) return;
    navTimer = setTimeout(() => { navTimer = null; renderNav(); }, 400);
}

let lbTile = null;
function openLightbox(tile) {
    if (!tile || !tile.dataset.url) return;
    lbTile = tile;
    $('lightbox').querySelector('img').src = tile.dataset.url;
    $('lightbox').querySelector('b').textContent = tile.querySelector('b').textContent;
    $('lightbox').querySelector('figcaption span').textContent = tile.title;
    $('lightbox').classList.add('on');
}
function stepLightbox(dir) {
    let el = lbTile;
    do { el = dir < 0 ? el?.previousElementSibling : el?.nextElementSibling; } while (el && !el.dataset.url);
    if (el) openLightbox(el);
}
const closeLightbox = () => { $('lightbox').classList.remove('on'); lbTile = null; };
$('lb-prev').onclick = () => stepLightbox(-1);
$('lb-next').onclick = () => stepLightbox(1);
$('lb-close').onclick = closeLightbox;
$('lightbox').onclick = (e) => { if (e.target === $('lightbox') || e.target.tagName === 'FIGURE') closeLightbox(); };
document.addEventListener('keydown', (e) => {
    if (!lbTile) return;
    if (e.key === 'Escape') closeLightbox();
    if (e.key === 'ArrowLeft') stepLightbox(-1);
    if (e.key === 'ArrowRight') stepLightbox(1);
});

function setLang(lang) {
    LANG = lang === 'en' ? 'en' : 'fr';
    setSegment($('lang'), LANG);
    applyI18n();
    renderKinds();
    renderGroupPicks();
    renderNav();
    renderPicks();
    renderEstimate();
    renderSources();
    renderGame();
    renderCatHead();
    renderGallery();
    renderGo();
    setRefs(state.refs);
    setResource(state.resource);
    if (lastProgress) showProgress(lastProgress);
    else $('k-time-label').textContent = t('kpi.left');
    if ($('live').querySelector('.empty')) emptyState($('live'), 'spark', t(state.running ? 'run.inProgress' : 'live.empty'));
    renderUpdate();
}

document.querySelectorAll('.nav-link[data-view]').forEach((b) => b.onclick = () => setView(b.dataset.view));
$('run-chip').onclick = () => setView('render');
$('lang').querySelectorAll('button').forEach((b) => b.onclick = () => { setLang(b.dataset.value); save(); });
$('sex-switch').querySelectorAll('button').forEach((b) => b.onclick = () => {
    state.sex = b.dataset.sex;
    setSegment($('sex-switch'), state.sex);
    save();
    renderNav();
    loadCategory();
});
$('filter').querySelectorAll('button').forEach((b) => b.onclick = () => {
    state.filter = b.dataset.filter;
    setSegment($('filter'), state.filter);
    renderGallery();
});
$('find').addEventListener('input', renderGallery);
$('thumb').addEventListener('input', () => { document.documentElement.style.setProperty('--thumb', $('thumb').value + 'px'); save(); });
document.querySelectorAll('[data-option]').forEach((group) => group.querySelectorAll('button').forEach((b) => b.onclick = () => {
    state.opts[group.dataset.option] = b.dataset.value;
    setSegment(group, b.dataset.value);
    save();
    renderEstimate();
    if (group.dataset.option === 'colors' || group.dataset.option === 'naming') { loadSummary(); loadCategory(); }
}));
document.querySelectorAll('[data-all]').forEach((a) => a.onclick = () => {
    document.querySelectorAll(isObjects() ? '[name=group]' : '[name=pick]').forEach((b) => { b.checked = a.dataset.all === '1'; });
    save();
    renderEstimate();
});
document.addEventListener('change', (e) => {
    if (e.target.name === 'pick' || e.target.name === 'sex' || e.target.name === 'group') { save(); renderEstimate(); }
    if (e.target.id === 'out') { save(); loadSummary(); loadCategory(); }
});

$('journal-btn').onclick = () => {
    $('journal').classList.toggle('on');
    state.errors = 0;
    $('badge').classList.remove('on');
};
$('journal-close').onclick = () => $('journal').classList.remove('on');
$('journal-clear').onclick = () => { $('log').textContent = ''; };

$('game-toggle').onclick = () => {
    if (state.running) return;
    state.useGame = !state.useGame;
    loadGame();
    sourcesChanged();
};
$('game-pick').onclick = () => api('/api/pick', { kind: 'game', initial: state.gamePath }).then((r) => {
    if (!r.path) return;
    state.gamePath = r.path;
    state.useGame = true;
    loadGame();
    sourcesChanged();
});
$('game-detect').onclick = () => api('/api/game', { action: 'detect' }).then((g) => {
    state.game = g;
    if (g.path) { state.gamePath = g.path; state.useGame = true; }
    sourcesChanged();
});
$('add-src').onclick = () => api('/api/pick', { kind: 'source', initial: state.folders[state.folders.length - 1] }).then((r) => {
    if (r.path && !state.folders.includes(r.path)) { state.folders.push(r.path); sourcesChanged(); }
});
$('pick').onclick = () => api('/api/pick', { kind: 'out', initial: $('out').value }).then((r) => {
    if (!r.path) return;
    $('out').value = r.path;
    save();
    loadSummary();
    loadCategory();
});
$('refs-import').onclick = () => api('/api/pick', { kind: 'refs', initial: $('out').value }).then((r) => {
    if (!r.path) return;
    api('/api/refs', { path: r.path, out: $('out').value.trim() }).then((x) => {
        if (x.error) return toast(errorText(x.error), 'err');
        setRefs(!!x.refs);
        toast(t('refs.imported'), 'ok');
        loadSummary();
        loadCategory();
    });
});
$('res-install').onclick = () => {
    toast(t('res.pick'));
    api('/api/pick', { kind: 'resources', initial: state.resource }).then((r) => {
        if (!r.path) return;
        api('/api/resource', { path: r.path }).then((x) => {
            if (x.error) return toast(errorText(x.error), 'err');
            setResource(x.resource);
            setRefs(!!x.refs);
            toast(t('res.done', { path: x.resource }), 'ok');
        });
    });
};
$('res-copy').onclick = () => navigator.clipboard.writeText($('res-cfg').textContent).then(() => toast(t('res.copied'), 'ok'));
$('open').onclick =() => api('/api/open', { path: $('out').value.trim() });

function start(o) {
    if (!o.sexes.length || !o.cats.length) return toast(t('run.pickOne'), 'err');
    api('/api/start', o).then((r) => {
        if (r.error) return toast(errorText(r.error), 'err');
        if (!r.total) return toast(t('run.nothing'));
        emptyState($('live'), 'spark', t('run.inProgress'));
        setRunning(true);
        toast(t('run.started', { n: fmt(r.total) }));
    });
}
$('go').onclick = () => {
    if (state.running) {
        $('go').disabled = true;
        return api('/api/stop');
    }
    start(options());
};
$('render-cat').onclick = () => start({ ...options(), sexes: [state.sex], cats: [state.cat], groups: [state.groupByKind[state.kind] || ''] });

function markdown(text) {
    const esc = (v) => v.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
    const inline = (v) => esc(v).replace(/\*\*(.+?)\*\*/g, '<b>$1</b>').replace(/`(.+?)`/g, '<code>$1</code>');
    let html = '', list = false;
    for (const raw of text.replace(/\r/g, '').split('\n')) {
        const line = raw.trim();
        const item = /^[-*] (.*)/.exec(line);
        if (list && !item) { html += '</ul>'; list = false; }
        if (item) { if (!list) { html += '<ul>'; list = true; } html += '<li>' + inline(item[1]) + '</li>'; }
        else if (/^#+ /.test(line)) html += '<h4>' + inline(line.replace(/^#+ /, '')) + '</h4>';
        else if (line) html += '<p>' + inline(line) + '</p>';
    }
    return html + (list ? '</ul>' : '');
}

function notesFor(text) {
    const parts = (text || '').split(/^## (FR|EN)\s*$/m);
    if (parts.length < 3) return text || '';
    const i = parts.indexOf(LANG.toUpperCase());
    return (i > 0 ? parts[i + 1] : parts[2]).trim();
}

function renderUpdate() {
    const u = state.update;
    $('update').classList.toggle('on', !!u && !state.updateHidden);
    if (!u) return;
    $('upd-title').textContent = t(u.state === 'installed' ? 'upd.installed' : 'upd.available', { version: 'v' + u.version });
    $('upd-state').textContent = u.state === 'installed' ? '' : t(state.running && u.state === 'ready' ? 'upd.busy' : 'upd.' + u.state);
    $('upd-notes').innerHTML = markdown(notesFor(u.notes));
    const go = { ready: 'upd.restart', manual: 'upd.page', downloading: 'upd.downloading' }[u.state];
    $('upd-go').textContent = go ? t(go) : '';
    $('upd-go').disabled = u.state === 'downloading' || (u.state === 'ready' && state.running);
    $('upd-later').textContent = t(u.state === 'installed' ? 'upd.close' : 'upd.later');
}

function setUpdate(u) {
    if (!state.update || state.update.state !== u.state) state.updateHidden = false;
    state.update = u;
    renderUpdate();
}

const hideUpdate = () => { state.updateHidden = true; renderUpdate(); };
$('upd-close').onclick = hideUpdate;
$('upd-later').onclick = hideUpdate;
$('upd-go').onclick = () => {
    const u = state.update;
    if (u.state === 'manual') window.open(u.url);
    else if (u.state === 'ready') api('/api/restart');
};

const win = (action, edge) => window.chrome.webview.postMessage({ action, edge: edge || 0 });
$('win-min').onclick = () => win('min');
$('win-max').onclick = () => win('max');
$('win-close').onclick = () => win('close');
document.querySelectorAll('.edges i').forEach((el) => {
    el.onmousedown = (e) => { if (e.button === 0) win('resize', +el.dataset.edge); };
});
document.addEventListener('mousedown', (e) => {
    if (e.button !== 0 || e.target.closest('button, input, a, .run-chip, .edges, .modal, .lightbox')) return;
    if (e.clientY > document.querySelector('.topbar').getBoundingClientRect().bottom) return;
    win(e.detail === 2 ? 'max' : 'drag');
});

const handlers = {
    win: (w) => {
        document.body.classList.toggle('maximized', w.max);
        $('win-max').querySelector('use').setAttribute('href', w.max ? '#i-restore' : '#i-max');
    },
    update: setUpdate,
    progress: showProgress,
    image: onImage,
    log: (m) => log(t('log.' + m.key, m), m.key === 'failed'),
    end: () => {
        setRunning(false);
        toast(t('run.finished'), 'ok');
        loadSummary();
    },
    refs: () => {
        setRefs(true);
        toast(t('refs.imported'), 'ok');
        loadSummary();
        loadCategory();
    },
    game: (g) => {
        const wasLoaded = state.game && state.game.loaded;
        state.game = g;
        renderGame();
        if (g.loaded && !wasLoaded) sourcesChanged();
        if (g.error && g.error !== 'game_invalid') log(t('game.error', { error: g.error }), true);
    },
};
window.chrome.webview.addEventListener('message', (e) => {
    const h = handlers[e.data.type];
    if (h) h(e.data.data);
});

fetch('/api/state').then((r) => r.json()).then((st) => {
    const s = st.settings;
    applyBranding(st.branding);
    $('version').textContent = 'v' + st.version;
    state.categories = st.categories;
    state.folders = s.folders || [];
    state.useGame = s.useGame;
    state.gamePath = s.gamePath || '';
    state.game = st.game;
    $('out').value = s.out || '';
    state.opts = { colors: s.colors, naming: s.naming, size: String(s.size), force: String(s.force), head: String(s.head) };
    if (ui) {
        state.sex = ui.sex || 'male';
        state.cat = ui.cat || 'jbib';
        document.querySelectorAll('[name=sex]').forEach((b) => { b.checked = ui.sexes.includes(b.value); });
        if (ui.thumb) $('thumb').value = ui.thumb;
        state.kind = KINDS.includes(ui.kind) ? ui.kind : 'clothes';
        state.groupByKind = ui.groupByKind || {};
        state.groupsPicked = ui.groupsPicked || {};
    }
    document.querySelectorAll('[data-option]').forEach((g) => setSegment(g, state.opts[g.dataset.option]));
    setSegment($('sex-switch'), state.sex);
    document.documentElement.style.setProperty('--thumb', $('thumb').value + 'px');
    setRefs(st.refs);
    state.resource = st.resource;
    emptyState($('live'), 'spark', '');
    setLang(s.lang || (navigator.language.startsWith('fr') ? 'fr' : 'en'));
    sourcesChanged();
    if (st.progress) {
        showProgress(st.progress);
        if (!st.progress.finished) setRunning(true);
    }
    if (st.update) setUpdate(st.update);
});
