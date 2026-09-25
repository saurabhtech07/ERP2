let grid;
let allRows = [];
let itemModal = null;
let viewModal = null;

const rowMap = {};
const ctx = { cat1: [], cat2: [], cat2ByCat1: {}, units: [] };
const $ = id => document.getElementById(id);

const flts = {};
const autoFltDone = {};

const IM_CONFIG = {
    idKey: 'id',
    title: 'Item Master',
    columns: [
        { key: 'id', label: 'ID', type: 'badge' },
        { key: 'name', label: 'NAME', type: 'bold' },
        { key: 'category1', label: 'CATEGORY1', type: 'plain' },
        { key: 'category2', label: 'CATEGORY2', type: 'plain' },
        { key: 'unit', label: 'UNIT', type: 'plain' },
        { key: 'status', label: 'STATUS', type: 'status' },
        { key: 'hsncode', label: 'HSN CODE', type: 'plain' },
        { key: 'modifyDate', label: 'MODIFY DATE', type: 'date' }
    ],
    filters: [
        { key: 'status', mount: 'fsd-status', ph: 'Status', accent: '', icon: 'ri-checkbox-circle-fill', fixed: ['Active', 'Inactive'] },
        { key: 'category1', mount: 'fsd-cat1', ph: 'Category 1', accent: '', icon: 'ri-price-tag-3-fill', ctx: 'cat1' },
        { key: 'category2', mount: 'fsd-cat2', ph: 'Category 2', accent: '', icon: 'ri-layout-grid-fill', parent: 'category1', ctx: 'cat2' }
    ],
    stats: [
        { el: 'stat-total', label: 'Total Items', kind: 'count' },
        { el: 'stat-fabric', label: 'Fabric', kind: 'eq', key: 'category1', value: 'FABRIC', ci: true },
        { el: 'stat-raw', label: 'Raw Material', kind: 'eq', key: 'category1', value: 'RAW MATERIAL(PRIMARY)', ci: true },
        { el: 'stat-active', label: 'Active', kind: 'eq', key: 'status', value: 'active', ci: true }
    ],
    viewFields: [
        { key: 'name', label: 'Name', el: 'v-name' },
        { key: 'category1', label: 'Category 1', el: 'v-cat1' },
        { key: 'category2', label: 'Category 2', el: 'v-cat2' },
        { key: 'unit', label: 'Unit', el: 'v-unit' },
        { key: 'hsncode', label: 'HSN Code', el: 'v-hsn' },
        { key: 'status', label: 'Status', el: 'v-status', badge: true },
        { key: 'modifyDate', label: 'Modify Date', el: 'v-date', date: true }
    ],
    searchFields: [],
    autoFilterLimit: 30,
    skipAutoFilter: ['id', 'actions', 'modifyDate', 'unit']
};

function esc(v) {
    return String(v == null ? '' : v).replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
}

function titleCase(k) {
    return String(k).replace(/[_-]+/g, ' ').replace(/\w\S*/g, w => w.charAt(0).toUpperCase() + w.slice(1).toLowerCase());
}

function rowGet(r, key) {
    return (r && key && key in r) ? r[key] : null;
}

function allColumnKeys() {
    return Array.isArray(window.__ITEM_COLUMNS__) ? window.__ITEM_COLUMNS__ : IM_CONFIG.columns.map(c => c.key);
}

function isDateLike(s) {
    return /^\d{4}-\d{2}-\d{2}/.test(String(s));
}

function formatDateTime(v) {
    let s = String(v);
    s = s.slice(0, 5) === '0000-' ? s.slice(5) : s;
    const m = /^(\d{4}-\d{2}-\d{2})[T ]?(\d{2}:\d{2})(:\d{2})?/.exec(s);
    if (m && m[2] !== '00:00') return m[1] + ' ' + m[2];
    if (m) return m[1];
    return s.slice(0, 10);
}

function cellText(cell) {
    if (cell == null || cell === '') return '-';
    if (typeof cell === 'number') return scell(cell);
    if (isDateLike(cell)) return String(cell).slice(0, 10);
    return esc(cell);
}

function scell(cell) {
    return String(cell == null ? '' : cell);
}

function cat1Key() {
    return (IM_CONFIG.filters.find(f => f.ctx === 'cat1') || {}).key || 'category1';
}

function cat2Key() {
    return (IM_CONFIG.filters.find(f => f.parent) || {}).key || 'category2';
}

function unitKey() {
    return 'unit';
}

function FilterSelect(mountId, cfg) {
    const mount = $(mountId);
    if (!mount) return null;
    const self = this;
    this.accent = cfg.accent || '';
    this.icon = cfg.icon || '';
    this.placeholder = cfg.placeholder || 'Select';
    this._opts = [];
    this._value = '';
    this._search = '';
    this._active = -1;
    this._handlers = [];
    this._open = false;

    this.root = document.createElement('div');
    this.root.className = 'fsd' + (this.accent ? ' ' + this.accent : '');
    this.root.innerHTML =
        '<div class="fsd-box" role="combobox" tabindex="0">' +
        '  <span class="fsd-ic"><i class="' + this.icon + '"></i></span>' +
        '  <span class="fsd-cur"></span>' +
        '  <span class="fsd-chev"><i class="ri-arrow-down-s-line"></i></span>' +
        '</div>' +
        '<div class="fsd-panel" hidden>' +
        '  <div class="fsd-search"><i class="ri-search-line"></i>' +
        '    <input type="text" class="fsd-input" autocomplete="off" placeholder="Search..."></div>' +
        '  <div class="fsd-list"></div>' +
        '</div>';

    const box = this.root.querySelector('.fsd-box');
    this._panel = this.root.querySelector('.fsd-panel');
    this._cur = this.root.querySelector('.fsd-cur');
    this._input = this.root.querySelector('.fsd-input');
    this._list = this.root.querySelector('.fsd-list');

    box.addEventListener('click', function () { self._open ? self.close() : self.open(); });
    box.addEventListener('keydown', function (e) {
        if (!self._open) return;
        if (e.key === 'Escape') { self.close(); e.preventDefault(); }
        if (e.key === 'Enter') { e.preventDefault(); self.pick(); }
        if (e.key === 'ArrowDown') { e.preventDefault(); self.move(1); }
        if (e.key === 'ArrowUp') { e.preventDefault(); self.move(-1); }
    });
    this._input.addEventListener('click', function (e) { e.stopPropagation(); });
    this._input.addEventListener('input', function () {
        self._search = this.value.toLowerCase();
        self._active = -1;
        self.render();
    });
    this._input.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') { self.close(); e.stopPropagation(); }
        if (e.key === 'Enter') { e.preventDefault(); self.pick(); }
        if (e.key === 'ArrowDown') { e.preventDefault(); self.move(1); }
        if (e.key === 'ArrowUp') { e.preventDefault(); self.move(-1); }
    });
    this._list.addEventListener('mousedown', function (e) {
        const row = e.target.closest('.fsd-opt');
        if (row) { e.preventDefault(); self.select(row.getAttribute('data-value')); }
    });
    document.addEventListener('click', function (e) {
        if (self._open && !self.root.contains(e.target)) self.close();
    });

    mount.appendChild(this.root);
    this.render();
}

FilterSelect.prototype.onChange = function (fn) { this._handlers.push(fn); return this; };
FilterSelect.prototype._emit = function () { this._handlers.forEach(f => f(this._value)); };
FilterSelect.prototype.value = function () { return this._value; };

FilterSelect.prototype.setValue = function (v, fire) {
    const nv = v == null ? '' : String(v);
    this._value = this._opts.some(o => o.value === nv) ? nv : '';
    this.render();
    if (fire !== false) this._emit();
    return this;
};

FilterSelect.prototype.setOptions = function (vals) {
    const self = this;
    this._opts = (vals || []).map(function (v) {
        const s = String(v == null ? '' : v);
        return { value: s, label: s };
    }).sort(function (a, b) { return a.label.localeCompare(b.label); });
    if (this._value && !this._opts.some(o => o.value === this._value)) this._value = '';
    this._search = '';
    this._input.value = '';
    this._active = -1;
    this.render();
    return this;
};

FilterSelect.prototype.render = function () {
    const self = this;
    let html = '';
    const filtered = this._opts.filter(function (o) {
        return !self._search || o.label.toLowerCase().indexOf(self._search) >= 0;
    });
    if (!filtered.length) {
        html = '<div class="fsd-empty">No results found</div>';
    } else {
        filtered.forEach(function (o, i) {
            const lv = o.value.toLowerCase();
            const cls = (lv === 'active' || lv === 'inactive') ? ' s-' + lv : '';
            const sel = (o.value === self._value) ? ' sel' : '';
            html += '<div class="fsd-opt' + cls + sel + '" data-value="' + esc(o.value) + '">' +
                '<span class="fsd-opt-t">' + esc(o.label) + '</span>' +
                (sel ? '<i class="ri-check-line fsd-check"></i>' : '') + '</div>';
        });
    }
    this._list.innerHTML = html;
    this._cur.textContent = this._value ? esc(this._value) : this.placeholder;
    this._cur.classList.toggle('ph', !this._value);
};

FilterSelect.prototype.open = function () {
    this._open = true;
    this.root.querySelector('.fsd-box').classList.add('open');
    this._panel.hidden = false;
    this._search = '';
    this._input.value = '';
    this._active = -1;
    this.render();
    this._input.focus();
};

FilterSelect.prototype.close = function () {
    this._open = false;
    this.root.querySelector('.fsd-box').classList.remove('open');
    this._panel.hidden = true;
};

FilterSelect.prototype.move = function (d) {
    const self = this;
    const rows = this._list.querySelectorAll('.fsd-opt');
    if (!rows.length) return;
    if (this._active < 0) this._active = d > 0 ? 0 : rows.length - 1;
    else {
        this._active += d;
        if (this._active < 0) this._active = rows.length - 1;
        if (this._active >= rows.length) this._active = 0;
    }
    rows.forEach(function (r, i) { r.classList.toggle('act', i === self._active); });
};

FilterSelect.prototype.pick = function () {
    const rows = this._list.querySelectorAll('.fsd-opt');
    if (!rows.length) return;
    let idx = (this._active >= 0 && this._active < rows.length) ? this._active : 0;
    this.select(rows[idx].getAttribute('data-value'));
};

FilterSelect.prototype.select = function (val) {
    const hit = this._opts.filter(o => o.value === val);
    this._value = hit.length ? hit[0].value : '';
    this.render();
    this.close();
    this._emit();
};

function toast(msg, isError) {
    Toastify({
        text: msg,
        duration: 3000,
        gravity: 'top',
        position: 'right',
        className: 'rounded',
        backgroundColor: isError ? '#f06548' : '#10b981'
    }).showToast();
}

function unique(vals) {
    return [...new Set(vals.filter(Boolean))].sort();
}

function statusClass(v) {
    const lv = String(v).toLowerCase();
    return (lv === 'active' || lv === 'inactive') ? ' status-' + lv : '';
}

function populateOptions(select, vals, selected, allLabel) {
    select.innerHTML = '<option value="">' + (allLabel || 'All') + '</option>' +
        vals.map(v => {
            const cls = statusClass(v);
            return '<option value="' + esc(v) + '" class="' + cls + '">' + esc(v) + '</option>';
        }).join('');
    if (selected && vals.indexOf(selected) >= 0) select.value = selected;
}

function populateFixedOptions(select, selected, def) {
    select.innerHTML = def ? '<option value="">' + def + '</option>' : '';
    ['Active', 'Inactive'].forEach(v => {
        const o = document.createElement('option');
        o.value = v;
        o.textContent = v;
        o.className = 'status-' + v.toLowerCase();
        select.appendChild(o);
    });
    if (selected) select.value = selected;
}

function statBadge(s) {
    const on = s && String(s).toLowerCase() === 'active';
    return '<span class="badge ' + (on ? 'bg-success-subtle text-success' : 'bg-danger-subtle text-danger') + '">' + esc(s || '-') + '</span>';
}

function actionsHtml(id) {
    return gridjs.html(
        '<button type="button" class="btn btn-sm btn-soft-primary me-1" onclick="editItem(' + id + ')" title="Edit"><i class="ri-pencil-fill"></i></button>' +
        '<button type="button" class="btn btn-sm btn-soft-danger me-1" onclick="deleteItem(' + id + ')" title="Delete"><i class="ri-delete-bin-fill"></i></button>' +
        '<button type="button" class="btn btn-sm btn-soft-info" onclick="viewItem(' + id + ')" title="View"><i class="ri-eye-line"></i></button>'
    );
}

function animateCount(el, target) {
    if (!el) return;
    const start = performance.now();
    const dur = 600;
    function step(now) {
        const p = Math.min((now - start) / dur, 1);
        el.textContent = Math.round(target * (1 - Math.pow(1 - p, 3)));
        if (p < 1) requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
}

function computeStats(rows) {
    IM_CONFIG.stats.forEach(function (s) {
        const el = $(s.el);
        let n = 0;
        if (s.kind === 'count') {
            n = rows.length;
        } else if (s.kind === 'eq') {
            const want = String(s.value).toLowerCase();
            n = rows.filter(function (r) {
                const v = String(rowGet(r, s.key) || '').toLowerCase();
                return s.ci ? v === want : v === s.value;
            }).length;
        }
        animateCount(el, n);
    });
}

function buildContext(rows) {
    const key1 = cat1Key();
    const key2 = cat2Key();
    ctx.cat1 = unique(rows.map(r => rowGet(r, key1)));
    ctx.cat2 = unique(rows.map(r => rowGet(r, key2)));
    ctx.units = unique(rows.map(r => rowGet(r, unitKey())));
    ctx.cat2ByCat1 = {};
    rows.forEach(r => {
        const c1 = rowGet(r, key1);
        const c2 = rowGet(r, key2);
        if (!c1 || !c2) return;
        (ctx.cat2ByCat1[c1] = ctx.cat2ByCat1[c1] || []);
        if (ctx.cat2ByCat1[c1].indexOf(c2) < 0) ctx.cat2ByCat1[c1].push(c2);
    });
    Object.keys(ctx.cat2ByCat1).forEach(k => ctx.cat2ByCat1[k].sort());
}

function buildAutoFilters(rows) {
    const keys = allColumnKeys();
    const existing = {};
    IM_CONFIG.filters.forEach(f => existing[f.key] = true);

    keys.forEach(function (key) {
        const k = String(key).toLowerCase();
        if (existing[k]) return;
        if (IM_CONFIG.skipAutoFilter.indexOf(k) >= 0) return;

        const vals = unique(rows.map(r => rowGet(r, k)));
        if (vals.length === 0 || vals.length > IM_CONFIG.autoFilterLimit) return;

        const rgx = /^(status|category|stage|type)\d*$/.test(k);
        const accent = rgx ? 'acc-cat1' : 'acc-cat2';

        const mountId = 'fsd-auto-' + k;
        let mount = $(mountId);
        if (!mount) {
            const holder = document.createElement('div');
            holder.className = 'col-6 col-lg-2 fsd-auto-col';
            holder.innerHTML = '<label class="form-label"><span class="label-ic blue"><i class="ri-filter-3-fill"></i></span>' +
                esc(titleCase(k)) + '</label><div id="' + mountId + '" class="fsd-mount"></div>';
            const searchCol = $('search-client').closest('.col-12');
            searchCol.parentNode.insertBefore(holder, searchCol);
            mount = $(mountId);
        }
        if (flts[k] || autoFltDone[k]) return;
        autoFltDone[k] = true;

        const f = { key: k, mount: mountId, ph: titleCase(k), accent: accent, icon: 'ri-filter-3-fill', auto: true };
        IM_CONFIG.filters.push(f);
        flts[k] = new FilterSelect(mountId, {
            placeholder: f.ph, accent: f.accent, icon: f.icon
        });
        flts[k].setOptions(vals);
        flts[k].onChange(applyFilters);
    });
}

function syncFilterOptions() {
    IM_CONFIG.filters.forEach(function (f) {
        const flt = flts[f.key];
        if (!flt || f.auto) return;
        const cur = flt.value();
        if (f.fixed) {
            flt.setOptions(f.fixed);
        } else if (f.parent) {
            const pv = flts[f.parent] ? flts[f.parent].value() : '';
            const vals = (pv && ctx.cat2ByCat1[pv]) ? ctx.cat2ByCat1[pv] : ctx.cat2;
            flt.setOptions(vals);
        } else {
            flt.setOptions(ctx[f.ctx]);
        }
        flt.setValue(cur, false);
    });
}

function getFiltered() {
    const q = $('search-client').value.trim().toLowerCase();
    return allRows.filter(function (r) {
        for (let i = 0; i < IM_CONFIG.filters.length; i++) {
            const f = IM_CONFIG.filters[i];
            const v = flts[f.key] ? flts[f.key].value() : '';
            if (!v) continue;
            if (String(rowGet(r, f.key) || '').toLowerCase() !== String(v).toLowerCase()) return false;
        }
        if (q) {
            const found = Object.keys(r).some(function (k) {
                const v = rowGet(r, k);
                return v != null && String(v).toLowerCase().indexOf(q) >= 0;
            });
            if (!found) return false;
        }
        return true;
    });
}

function refreshTable(rows) {
    grid.updateConfig({ data: rows });
    grid.forceRender();
    $('record-count').textContent = rows.length + ' of ' + allRows.length + ' items';
}

function applyFilters() {
    syncFilterOptions();
    refreshTable(getFiltered());
}

function applyData(rows) {
    allRows = rows;
    Object.keys(rowMap).forEach(k => delete rowMap[k]);
    rows.forEach(r => rowMap[rowGet(r, IM_CONFIG.idKey)] = r);
    buildContext(rows);
    buildAutoFilters(rows);
    computeStats(rows);
    syncFilterOptions();
    refreshTable(getFiltered());
    $('loading').classList.add('d-none');
    $('item-grid').classList.remove('d-none');
}

function loadData() {
    $('loading').classList.remove('d-none');
    $('item-grid').classList.add('d-none');

    if (Array.isArray(window.__ITEM_DATA__)) {
        applyData(window.__ITEM_DATA__);
        return;
    }
    if (window.__ITEM_ERROR__) {
        $('loading').classList.add('d-none');
        $('item-grid').classList.remove('d-none');
        toast('Data load failed: ' + window.__ITEM_ERROR__, true);
        return;
    }

    fetch('/ItemMaster/Items').then(r => r.json())
        .then((res) => {
            const rows = (res && Array.isArray(res.rows)) ? res.rows : null;
            if (!rows) {
                throw new Error((res && res.message) ? res.message : 'Invalid response from server');
            }
            window.__ITEM_COLUMNS__ = (res && Array.isArray(res.columns) && res.columns.length)
                ? res.columns : window.__ITEM_COLUMNS__;
            buildGrid();
            applyData(rows);
        })
        .catch(err => {
            $('loading').classList.add('d-none');
            $('item-grid').classList.remove('d-none');
            toast('Data load failed: ' + err.message, true);
        });
}

function buildGridColumns() {
    const keys = allColumnKeys();
    const known = {};
    IM_CONFIG.columns.forEach(c => known[String(c.key).toLowerCase()] = c);

    const cols = [];
    keys.forEach(function (key) {
        const raw = String(key);
        const k = raw.toLowerCase();
        if (k === 'actions') return;
        const kc = known[k];
        const label = kc ? kc.label : titleCase(k);
        const col = { name: label, data: r => rowGet(r, raw), sort: true };

        if (kc && kc.type === 'badge') col.formatter = cell => gridjs.html('<span class="badge bg-dark">' + esc(cell) + '</span>');
        else if (kc && kc.type === 'bold') col.formatter = cell => gridjs.html('<span class="fw-semibold">' + esc(cell) + '</span>');
        else if (kc && kc.type === 'status') col.formatter = cell => gridjs.html(statBadge(cell));
        else if (kc && kc.type === 'date') col.formatter = cell => cell ? formatDateTime(cell) : '-';
        else col.formatter = cell => cellText(cell);

        cols.push({ colDef: col, key: raw, label: label, icol: { name: label, data: col.data, sort: col.sort, formatter: col.formatter } });
    });
    return cols;
}

let exportCols = [];

function buildGrid() {
    const built = buildGridColumns();
    exportCols = built.map(function (b) { return { key: b.key, label: b.label }; });

    const columns = built.map(function (b) { return b.icol; });
    columns.push({ name: 'ACTIONS', width: '165px', data: r => rowGet(r, IM_CONFIG.idKey), sort: false, formatter: cell => actionsHtml(cell) });

    grid.updateConfig({ columns: columns });
    grid.forceRender();
}

function populateFormFields(r, isAdd) {
    populateOptions($('f-cat1'), ctx.cat1, isAdd ? ctx.cat1[0] : rowGet(r, 'category1'), 'Select Category 1');
    syncModalCat2();
    $('f-cat2').value = isAdd ? ctx.cat2[0] : (rowGet(r, 'category2') || $('f-cat2').value);
    populateOptions($('f-unit'), ctx.units, isAdd ? ctx.units[0] : rowGet(r, 'unit'), 'Select Unit');
    populateFixedOptions($('f-status'), rowGet(r, 'status') || 'Active');
    $('f-name').value = (r && rowGet(r, 'name')) || '';
    $('f-hsn').value = (r && rowGet(r, 'hsncode')) || '';
}

function openAddModal() {
    $('itemModalLabel').textContent = 'Add New Item';
    $('item-form').reset();
    $('f-id').value = 0;
    populateFormFields(null, true);
    itemModal.show();
}

function syncModalCat2() {
    const c1 = $('f-cat1').value;
    const vals = (c1 && ctx.cat2ByCat1[c1]) ? ctx.cat2ByCat1[c1] : ctx.cat2;
    populateOptions($('f-cat2'), vals, vals[0], 'Select Category 2');
}

function editItem(id) {
    const r = rowMap[id];
    if (!r) return;
    $('itemModalLabel').textContent = 'Edit Item - ' + (rowGet(r, 'name') || '');
    $('f-id').value = rowGet(r, IM_CONFIG.idKey);
    populateFormFields(r, false);
    itemModal.show();
}

function viewItem(id) {
    const r = rowMap[id];
    if (!r) return;
    $('viewModalLabel').textContent = 'Item Details - ' + (rowGet(r, 'name') || '');
    $('v-id').textContent = '#' + rowGet(r, IM_CONFIG.idKey) + ' • ' + (rowGet(r, 'status') || '');

    const known = {};
    IM_CONFIG.viewFields.forEach(vf => known[vf.key] = vf);

    IM_CONFIG.viewFields.forEach(function (vf) {
        const el = $(vf.el);
        if (!el) return;
        const val = rowGet(r, vf.key);
        if (vf.badge) el.innerHTML = statBadge(val);
        else if (vf.date) el.textContent = val ? formatDateTime(val) : '-';
        else el.textContent = (val == null || val === '') ? '-' : esc(val);
    });

    const existing = document.querySelector('#viewModal .view-detail-extra');
    if (existing) existing.remove();

    const extra = allColumnKeys().filter(k => !known[k] && k.toLowerCase() !== 'id');
    if (extra.length) {
        let html = '<div class="view-detail-extra mt-2 pt-2 border-top">';
        extra.forEach(function (k) {
            const val = rowGet(r, k);
            html += '<div class="view-field mt-2">' +
                '<label>' + esc(titleCase(k)) + '</label>' +
                '<p>' + (val == null || val === '' ? '-' : esc(val)) + '</p></div>';
        });
        html += '</div>';
        document.querySelector('#viewModal .modal-body').insertAdjacentHTML('beforeend', html);
    }

    viewModal.show();
}

function deleteItem(id) {
    if (!confirm('Delete item ' + id + '?')) return;
    fetch('/ItemMaster/Delete?id=' + id, { method: 'DELETE' })
        .then(r => r.json())
        .then(res => {
            toast(res.message, !res.success);
            if (res.success) loadData();
        })
        .catch(err => toast('Delete failed: ' + err.message, true));
}

function csvRow(r) {
    return exportCols.map(function (c) {
        let v = rowGet(r, c.key);
        if (v != null && isDateLike(v)) v = String(v).slice(0, 10);
        return '"' + String(v == null ? '' : v).replace(/"/g, '""') + '"';
    }).join(',');
}

function download(filename, content, type) {
    const blob = new Blob([content], { type: type + ';charset=utf-8;' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(a.href);
}

function exportCsv(rows, filename) {
    const header = ['Sr.No'].concat(exportCols.map(c => c.label)).join(',');
    const lines = rows.map((r, i) => (i + 1) + ',' + csvRow(r));
    download(filename, '\uFEFF' + header + '\n' + lines.join('\n'), 'text/csv');
}

function exportExcel(rows) {
    download('ItemMaster.xls', '\uFEFF' + headerRows(), 'application/vnd.ms-excel');
}

function headerRows() {
    const rows = getFiltered();
    const head = ['Sr.No'].concat(exportCols.map(c => esc(c.label))).map(h => '<th>' + h + '</th>').join('');
    const body = rows.map(function (r, i) {
        const cells = exportCols.map(function (c) {
            let v = rowGet(r, c.key);
if (v != null && isDateLike(v)) v = formatDateTime(v);
            return '<td>' + esc(v == null ? '' : v) + '</td>';
        });
        return '<tr><td>' + (i + 1) + '</td>' + cells.join('') + '</tr>';
    }).join('');
    return '<table border="1"><tr>' + head + '</tr>' + body + '</table>';
}

function printTable() {
    const rows = getFiltered();
    const w = window.open('', '_blank');
    if (!w) {
        toast('Popup blocked. Allow popups to print.', true);
        return;
    }
    w.document.write('<!DOCTYPE html><html><head><title>' + esc(IM_CONFIG.title) + '</title>');
    w.document.write('<style>body{font-family:Arial,sans-serif;padding:20px}h2{margin-bottom:10px}' +
        'table{border-collapse:collapse;width:100%;font-size:12px}th,td{border:1px solid #ddd;padding:5px 8px;text-align:left}' +
        'th{background:#1e3a5f;color:#fff}</style></head><body>');
    w.document.write('<h2>' + esc(IM_CONFIG.title) + ' Report</h2>');
    w.document.write('<p>' + rows.length + ' of ' + allRows.length + ' items</p>');
    w.document.write(headerRows());
    w.document.write('</body></html>');
    w.document.close();
    w.focus();
    w.print();
}

$('btn-new').addEventListener('click', openAddModal);

$('btn-clear').addEventListener('click', function () {
    IM_CONFIG.filters.forEach(function (f) {
        if (flts[f.key]) flts[f.key].setValue('', false);
    });
    $('search-client').value = '';
    applyFilters();
});

$('btn-excel').addEventListener('click', exportExcel);
$('btn-csv').addEventListener('click', function () { exportCsv(getFiltered(), 'ItemMaster.csv'); });
$('btn-print').addEventListener('click', printTable);

IM_CONFIG.filters.forEach(function (f) {
    flts[f.key] = new FilterSelect(f.mount, {
        placeholder: f.ph, accent: f.accent, icon: f.icon
    });
    flts[f.key].onChange(applyFilters);
});

$('search-client').addEventListener('input', function () {
    refreshTable(getFiltered());
});

$('page-size').addEventListener('change', function () {
    grid.updateConfig({
        pagination: {
            enabled: true,
            limit: parseInt(this.value, 10) || 10
        }
    });
    grid.forceRender();
    this.blur();
});

$('f-cat1').addEventListener('change', syncModalCat2);

$('item-form').addEventListener('submit', function (e) {
    e.preventDefault();
    const payload = {
        id: parseInt($('f-id').value, 10) || 0,
        name: $('f-name').value.trim(),
        category1: $('f-cat1').value,
        category2: $('f-cat2').value,
        unit: $('f-unit').value,
        hsncode: $('f-hsn').value.trim(),
        status: $('f-status').value
    };
    fetch('/ItemMaster/Save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(r => r.json())
        .then(res => {
            toast(res.message, !res.success);
            if (res.success) {
                itemModal.hide();
                loadData();
            }
        })
        .catch(err => toast('Save failed: ' + err.message, true));
});

grid = new gridjs.Grid({
    columns: [],
    data: [],
    sort: true,
    search: false,
    pagination: { limit: 10 },
    className: { table: 'table table-bordered table-striped table-hover align-middle m-0' }
}).render($('item-grid'));

itemModal = new bootstrap.Modal($('itemModal'));
viewModal = new bootstrap.Modal($('viewModal'));
buildGrid();
loadData();