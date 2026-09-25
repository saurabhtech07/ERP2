let grid;
let allRows = [];
let viewModal = null;

const rowMap = {};
const ctx = {};
const flts = {};
const $ = id => document.getElementById(id);

const PAGE_TITLE = 'Job Card Issue Report';
const MAX_FLT_DISTINCT = 500;
const FILTER_KEYS = ['jobcardno', 'name', 'unit'];

const meta = {
    all: [],
    numSet: {},
    dateSet: {},
    dateKeys: [],
    dateKey: null,
    idKey: 'rowid',
    stats: [],
    filters: []
};

let exportCols = [];

const ACCENTS = [
    { c: 'orange', hex: '#ff8c00', icon: 'ri-database-2-line' },
    { c: 'blue', hex: '#3b82f6', icon: 'ri-external-link-line' },
    { c: 'grey', hex: '#64748b', icon: 'ri-box-3-line' },
    { c: 'green', hex: '#10b981', icon: 'ri-checkbox-circle-line' },
    { c: 'purple', hex: '#8b5cf6', icon: 'ri-funds-box-line' },
    { c: 'teal', hex: '#0d9488', icon: 'ri-timer-flash-line' },
    { c: 'pink', hex: '#ec4899', icon: 'ri-pie-chart-2-line' },
    { c: 'red', hex: '#ef4444', icon: 'ri-error-warning-line' }
];

const FILTER_ICONS = [
    'ri-file-list-3-line', 'ri-user-3-line', 'ri-cup-line', 'ri-price-tag-3-fill',
    'ri-archive-drawer-line', 'ri-route-line', 'ri-building-2-line', 'ri-cake-3-line',
    'ri-stack-line', 'ri-flag-2-line', 'ri-markup-line', 'ri-shape-line'
];

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
    return Array.isArray(window.__JC_COLUMNS__) ? window.__JC_COLUMNS__ : [];
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

function displayValue(cell) {
    if (cell == null || cell === '') return '-';
    if (typeof cell === 'number') return scell(cell);
    if (isDateLike(cell)) return formatDateTime(cell);
    return esc(cell);
}

function scell(cell) {
    return String(cell == null ? '' : cell);
}

function fmtNum(n) {
    const v = Math.round(n * 100) / 100;
    return v.toLocaleString('en-IN', { maximumFractionDigits: 2 });
}

function unique(vals) {
    return [...new Set(vals.filter(Boolean))].sort(function (a, b) {
        return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
    });
}

function looksLikeCode(k) {
    return /(^id$|_id$|no$|code|ref|no\b)/i.test(k);
}

function FilterSelect(mountId, cfg) {
    const mount = $(mountId);
    if (!mount) return null;
    const self = this;
    this.icon = cfg.icon || '';
    this.placeholder = cfg.placeholder || 'Select';
    this.label = cfg.label || '';
    this._opts = [];
    this._value = '';
    this._search = '';
    this._active = -1;
    this._handlers = [];
    this._open = false;

    this.root = document.createElement('div');
    this.root.className = 'fsd';
    this.root.innerHTML =
        '<div class="fsd-box" role="combobox" tabindex="0">' +
        '  <span class="fsd-ic"><i class="' + this.icon + '"></i></span>' +
        '  <div class="fsd-txt">' +
        '    <span class="fsd-lbl">' + esc(this.label) + '</span>' +
        '    <span class="fsd-cur"></span>' +
        '  </div>' +
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
    const sv = v == null ? '' : String(v);
    if (this._value === sv) return;
    this._value = sv;
    this._cur.textContent = sv ? sv : this.placeholder;
    if (sv) this.root.classList.add('picked'); else this.root.classList.remove('picked');
    if (fire) this._emit();
};

FilterSelect.prototype.setOptions = function (opts) {
    this._opts = opts.map(o => String(o)).filter(Boolean);
    this._search = '';
    this._active = -1;
    this.render();
};

FilterSelect.prototype.render = function () {
    const self = this;
    this._list.innerHTML = '';
    const rows = this._opts.filter(o => !this._search || o.toLowerCase().indexOf(this._search) >= 0);
    if (!rows.length) {
        this._list.innerHTML = '<div class="fsd-empty">No matches found</div>';
        return;
    }
    rows.forEach(function (o, i) {
        const opt = document.createElement('div');
        opt.className = 'fsd-opt' + (o === self._value ? ' sel' : '');
        opt.setAttribute('data-value', o);
        opt.innerHTML = '<span class="fsd-opt-t"></span>' +
            (o === self._value ? '<i class="ri-check-line fsd-check"></i>' : '');
        opt.querySelector('.fsd-opt-t').textContent = o;
        self._list.appendChild(opt);
    });
};

FilterSelect.prototype.move = function (dir) {
    const opts = this._list.querySelectorAll('.fsd-opt');
    if (!opts.length) return;
    this._active = Math.min(Math.max(this._active + dir, 0), opts.length - 1);
    opts[this._active].className = 'fsd-opt act';
};

FilterSelect.prototype.pick = function () {
    const opts = this._list.querySelectorAll('.fsd-opt');
    if (!opts.length) return;
    this.select(opts[this._active >= 0 ? this._active : 0].getAttribute('data-value'));
};

FilterSelect.prototype.select = function (v) {
    this.setValue(v, true);
    this.close();
};

FilterSelect.prototype.open = function () {
    if (!this._opts.length) return;
    this._open = true;
    this._panel.hidden = false;
    this.root.classList.add('open');
    this.render();
    setTimeout(() => this._input.focus(), 10);
};

FilterSelect.prototype.close = function () {
    this._open = false;
    this._panel.hidden = true;
    this.root.classList.remove('open');
};

function animateCount(el, target) {
    if (!el) return;
    const start = performance.now();
    const dur = 600;
    function step(now) {
        const p = Math.min((now - start) / dur, 1);
        el.textContent = fmtNum(target * (1 - Math.pow(1 - p, 3)));
        if (p < 1) requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
}

/* ============ Fully dynamic: metadata from SP schema + data ============ */

function buildMeta(rows) {
    meta.all = allColumnKeys().filter(k => String(k).toLowerCase() !== 'actions');
    meta.numSet = {};
    meta.dateSet = {};
    meta.dateKeys = [];
    meta.idKey = meta.all[0] || 'rowid';

    meta.all.forEach(k => {
        const t = (window.__JC_TYPES__ && window.__JC_TYPES__[k]) || detectTypeFromData(rows, k);
        const kl = k.toLowerCase();
        if (t === 'number') meta.numSet[kl] = true;
        else if (t === 'date') { meta.dateSet[kl] = true; meta.dateKeys.push(k); }
    });
    meta.all.forEach(k => {
        const kl = k.toLowerCase();
        if (!meta.dateSet[kl] && /date|edate|time$/.test(kl) && !meta.numSet[kl]) {
            meta.dateSet[kl] = true;
            meta.dateKeys.push(k);
        }
    });
    meta.dateKey = meta.dateKeys[0] || null;

    meta.stats = [{ el: 'stat-count', label: 'Total Records', kind: 'count' }];
    const sumKeys = meta.all.filter(k => {
        const kl = k.toLowerCase();
        return meta.numSet[kl] && k !== meta.idKey;
    }).slice(0, 3);
    sumKeys.forEach(k => {
        const kl = k.toLowerCase();
        meta.stats.push({ el: 'stat-' + kl, label: 'Total ' + titleCase(k), kind: 'sum', key: k });
    });

    meta.filters = [];
    meta.all.forEach(k => {
        const kl = k.toLowerCase();
        if (FILTER_KEYS.indexOf(kl) === -1) return;
        const values = rows.map(r => rowGet(r, k)).filter(v => v != null && v !== '');
        if (!values.length) return;
        meta.filters.push({ key: k });
    });
}

function detectTypeFromData(rows, k) {
    let numeric = 0, total = 0;
    rows.slice(0, 50).forEach(r => {
        const v = rowGet(r, k);
        if (v == null || v === '') return;
        total++;
        if (!isNaN(Number(v))) numeric++;
    });
    if (total && numeric / total >= 0.95) return 'number';
    if (total && rows.slice(0, 50).some(r => isDateLike(rowGet(r, k)))) return 'date';
    return 'text';
}

/* ============ Stat cards ============ */

function statChartSvg(hex) {
    return '<svg viewBox="0 0 24 24" fill="none">' +
        '<rect x="0" y="12" width="4" height="12" rx="1.5" fill="' + hex + '" opacity="0.35"/>' +
        '<rect x="6" y="7" width="4" height="17" rx="1.5" fill="' + hex + '" opacity="0.65"/>' +
        '<rect x="12" y="3" width="4" height="21" rx="1.5" fill="' + hex + '"/>' +
        '<rect x="18" y="9" width="4" height="15" rx="1.5" fill="' + hex + '" opacity="0.5"/>' +
        '</svg>';
}

function buildStatCards() {
    const container = $('stat-cards');
    if (!container) return;
    container.innerHTML = '';
    meta.stats.forEach(function (s, i) {
        const a = ACCENTS[i % ACCENTS.length];
        const el = document.createElement('div');
        el.className = 'col-xl-3 col-md-6';
        el.innerHTML =
            '<div class="card stat-card h-100 stat-' + a.c + '">' +
            '  <div class="card-body d-flex align-items-center justify-content-between">' +
            '    <div class="d-flex align-items-center gap-2" style="min-width:0">' +
            '      <span class="avatar-title rounded-xl icon-44 bg-solid-' + a.c + '">' +
            '        <i class="' + a.icon + ' text-white"></i>' +
            '      </span>' +
            '      <div style="min-width:0">' +
            '        <h4 class="fw-bold mb-1 stat-label" title="' + esc(s.label) + '">' + esc(s.label) + '</h4>' +
            '        <div class="stat-value text-truncate" id="' + esc(s.el) + '">0</div>' +
            '      </div>' +
            '    </div>' +
            '    <div class="chart-mini">' + statChartSvg(a.hex) + '</div>' +
            '  </div>' +
            '</div>';
        container.appendChild(el);
    });
}

function computeStats(rows) {
    meta.stats.forEach(function (s) {
        const el = $(s.el);
        let n = 0;
        if (s.kind === 'count') {
            n = rows.length;
        } else if (s.kind === 'sum') {
            n = rows.reduce(function (sum, r) {
                const v = parseFloat(rowGet(r, s.key));
                return sum + (isNaN(v) ? 0 : v);
            }, 0);
        }
        animateCount(el, n);
    });
}

/* ============ Filters ============ */

function buildAutoFilters() {
    const wrap = $('auto-filters');
    if (!wrap) return;
    wrap.innerHTML = '';
    meta.filters.forEach(function (f, i) {
        const col = document.createElement('div');
        col.className = 'flt-col';
        col.innerHTML = '<div id="fsd-' + esc(f.key) + '" class="fsd-mount"></div>';
        wrap.appendChild(col);
        const icon = FILTER_ICONS[i % FILTER_ICONS.length];
        flts[f.key] = new FilterSelect('fsd-' + f.key, { placeholder: 'All', icon: icon, label: titleCase(f.key) });
        flts[f.key].onChange(applyFilters);
    });
    const searchWrap = document.createElement('div');
    searchWrap.className = 'flt-search-wrap position-relative';
    searchWrap.innerHTML =
        '<input type="text" id="search-client" class="form-control ps-5" placeholder="Search job card / item / supplier / qty...">' +
        '<i class="ri-search-line position-absolute top-50 translate-middle-y ms-3 search-icon"></i>';
    wrap.appendChild(searchWrap);
    const searchInput = $('search-client');
    if (searchInput && !searchInput.dataset.bound) {
        searchInput.dataset.bound = '1';
        searchInput.addEventListener('input', function () { refreshTable(getFiltered()); });
    }
    if (!meta.filters.length) {
        wrap.innerHTML = '<div class="text-muted fs-13 py-1">No filter options detected.</div>';
    }
}

/* ============ Row context + filtering ============ */

function buildContext(rows) {
    Object.keys(ctx).forEach(k => delete ctx[k]);
    meta.filters.forEach(function (f) {
        ctx[f.key] = unique(rows.map(r => rowGet(r, f.key)));
    });
}

function syncFilterOptions() {
    meta.filters.forEach(function (f) {
        const flt = flts[f.key];
        if (!flt) return;
        const cur = flt.value();
        flt.setOptions(ctx[f.key] || []);
        flt.setValue(cur, false);
    });
}

function getFiltered() {
    const q = $('search-client').value.trim().toLowerCase();
    const from = $('filter-from').value;
    const to = $('filter-to').value;
    const dateKey = meta.dateKey;

    return allRows.filter(function (r) {
        for (let i = 0; i < meta.filters.length; i++) {
            const f = meta.filters[i];
            const v = flts[f.key] ? flts[f.key].value() : '';
            if (!v) continue;
            if (String(rowGet(r, f.key) || '').toLowerCase() !== String(v).toLowerCase()) return false;
        }
        if (from && dateKey) {
            const d = String(rowGet(r, dateKey) || '').slice(0, 10);
            if (!d || d < from) return false;
        }
        if (to && dateKey) {
            const d = String(rowGet(r, dateKey) || '').slice(0, 10);
            if (!d || d > to) return false;
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
    $('record-count').textContent = rows.length + ' of ' + allRows.length + ' records';
}

function applyFilters() {
    syncFilterOptions();
    refreshTable(getFiltered());
}

/* ============ Data + grid ============ */

function applyData(rows) {
    allRows = rows;
    buildMeta(rows);
    buildStatCards();
    buildAutoFilters();
    buildGrid();
    if (meta.dateKey) {
        const blk = $('date-range-block');
        if (blk) blk.classList.remove('d-none');
    } else {
        const blk = $('date-range-block');
        if (blk) blk.classList.add('d-none');
    }
    Object.keys(rowMap).forEach(k => delete rowMap[k]);
    const seen = {};
    rows.forEach(function (r, i) {
        const idv = String(rowGet(r, meta.idKey) == null ? '' : rowGet(r, meta.idKey));
        let key = idv || ('r' + i);
        if (key in seen) key = key + '_' + i;
        seen[key] = true;
        r.__jckey = key;
        rowMap[key] = r;
    });
    buildContext(rows);
    computeStats(rows);
    syncFilterOptions();
    refreshTable(getFiltered());
    $('loading').classList.add('d-none');
    $('item-grid').classList.remove('d-none');
}

function loadData() {
    $('loading').classList.remove('d-none');
    $('item-grid').classList.add('d-none');

    if (Array.isArray(window.__JC_DATA__)) {
        applyData(window.__JC_DATA__);
        return;
    }
    if (window.__JC_ERROR__) {
        $('loading').classList.add('d-none');
        $('item-grid').classList.remove('d-none');
        toast('Data load failed: ' + window.__JC_ERROR__, true);
        return;
    }

    fetch('/JobCardIssue/GetData').then(r => r.json())
        .then((res) => {
            const rows = (res && Array.isArray(res.rows)) ? res.rows : null;
            if (!rows) {
                throw new Error((res && res.message) ? res.message : 'Invalid response from server');
            }
            window.__JC_COLUMNS__ = (res && Array.isArray(res.columns) && res.columns.length)
                ? res.columns : window.__JC_COLUMNS__;
            if (res && res.types) window.__JC_TYPES__ = res.types;
            applyData(rows);
        })
        .catch(err => {
            $('loading').classList.add('d-none');
            $('item-grid').classList.remove('d-none');
            toast('Data load failed: ' + err.message, true);
        });
}

/* ============ Grid / columns ============ */

function isDateColumn(k) {
    return !!meta.dateSet[k.toLowerCase()];
}

function isNumericColumn(k) {
    return !!meta.numSet[k.toLowerCase()];
}

function buildGridColumns() {
    const keys = allColumnKeys();
    const cols = [];
    keys.forEach(function (key) {
        const raw = String(key);
        const k = raw.toLowerCase();
        if (k === 'actions') return;
        const label = titleCase(k);
        const col = { name: label, data: r => rowGet(r, raw), sort: true };

        if (isNumericColumn(k)) col.formatter = cell => gridjs.html(cell == null || cell === ''
            ? '<span class="text-end d-block text-muted">-</span>'
            : '<span class="text-end d-block fw-semibold">' + scell(cell) + '</span>');
        else if (isDateColumn(k)) col.formatter = cell => cell ? formatDateTime(cell) : '-';

        cols.push({ colDef: col, key: raw, label: label, icol: { name: label, data: col.data, sort: col.sort, formatter: col.formatter } });
    });
    return cols;
}

function buildGrid() {
    const built = buildGridColumns();
    exportCols = built.map(function (b) { return { key: b.key, label: b.label }; });

    const columns = built.map(function (b) { return b.icol; });
    columns.push({ name: 'ACTIONS', width: '90px', data: r => String(rowGet(r, '__jckey') || ''), sort: false, formatter: cell => actionsHtml(cell) });

    grid.updateConfig({ columns: columns });
    grid.forceRender();
}

function actionsHtml(key) {
    return gridjs.html(
        '<button type="button" class="btn btn-sm btn-soft-info" onclick="viewItem(\'' + esc(key) + '\')" title="View"><i class="ri-eye-line"></i></button>'
    );
}

function viewItem(key) {
    const r = rowMap[key];
    if (!r) return;

    const idVal = rowGet(r, meta.idKey);
    $('viewModalLabel').textContent = 'Record Details';
    $('v-id').textContent = idVal == null || idVal === '' ? 'Record #' : 'Record #' + idVal;

    const keys = allColumnKeys();
    let html = '';
    keys.forEach(function (k) {
        if (String(k).toLowerCase() === 'actions') return;
        const val = rowGet(r, k);
        html += '<div class="col-md-6 col-xl-4">' +
            '<div class="view-field">' +
            '<label>' + esc(titleCase(k)) + '</label>' +
            '<p>' + displayValue(val) + '</p></div></div>';
    });
    $('jc-view-body').innerHTML = html || '<div class="col-12 text-center text-muted py-3">No fields found.</div>';

    viewModal.show();
}

/* ============ Export / print ============ */

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
    download('JobCardIssueReport.xls', '\uFEFF' + headerRows(), 'application/vnd.ms-excel');
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
    w.document.write('<!DOCTYPE html><html><head><title>' + esc(PAGE_TITLE) + '</title>');
    w.document.write('<style>body{font-family:Arial,sans-serif;padding:20px}h2{margin-bottom:10px}' +
        'table{border-collapse:collapse;width:100%;font-size:12px}th,td{border:1px solid #ddd;padding:5px 8px;text-align:left}' +
        'th{background:#1e3a5f;color:#fff}</style></head><body>');
    w.document.write('<h2>' + esc(PAGE_TITLE) + '</h2>');
    w.document.write('<p>' + rows.length + ' of ' + allRows.length + ' records</p>');
    w.document.write(headerRows());
    w.document.write('</body></html>');
    w.document.close();
    w.focus();
    w.print();
}

/* ============ Events ============ */

$('btn-clear').addEventListener('click', function () {
    Object.keys(flts).forEach(function (k) {
        if (flts[k]) flts[k].setValue('', false);
    });
    $('search-client').value = '';
    $('filter-from').value = '';
    $('filter-to').value = '';
    applyFilters();
});

$('btn-excel').addEventListener('click', function () { exportExcel(getFiltered()); });
$('btn-csv').addEventListener('click', function () { exportCsv(getFiltered(), 'JobCardIssueReport.csv'); });
$('btn-print').addEventListener('click', printTable);

$('filter-from').addEventListener('change', function () {
    if (this.value && $('filter-to').value && this.value > $('filter-to').value) $('filter-to').value = '';
    applyFilters();
});

$('filter-to').addEventListener('change', function () {
    if (this.value && $('filter-from').value && this.value < $('filter-from').value) $('filter-from').value = '';
    applyFilters();
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

grid = new gridjs.Grid({
    columns: [],
    data: [],
    sort: true,
    search: false,
    pagination: { limit: 10 },
    className: { table: 'table table-bordered table-hover align-middle m-0' }
}).render($('item-grid'));

viewModal = new bootstrap.Modal($('viewModal'));
loadData();