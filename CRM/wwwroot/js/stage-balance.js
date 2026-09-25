let grid;
let allRows = [];
let viewModal = null;

const rowMap = {};
const flts = {};

const $ = id => document.getElementById(id);

const SB_CONFIG = {
    title: 'Stage Balance',
    controller: 'StageBalance',
    columns: [
        { key: 'docno', label: 'DOC NO', type: 'plain' },
        { key: 'date', label: 'DATE', type: 'date' },
        { key: 'sizename', label: 'SIZE', type: 'plain' },
        { key: 'cat1', label: 'CATEGORY', type: 'plain' },
        { key: 'name', label: 'NAME', type: 'bold' },
        { key: 'colno', label: 'COL NO', type: 'plain' },
        { key: 'stagename', label: 'STAGE', type: 'plain' },
        { key: 'r_qty', label: 'R. QTY', type: 'qty' },
        { key: 'i_qty', label: 'I. QTY', type: 'qty' },
        { key: 'balqty', label: 'BAL. QTY', type: 'qty' },
        { key: 'asale_jc_no', label: 'ASALE JC NO', type: 'plain' }
    ],
    filters: [
        { key: 'stagename', mount: 'fsd-stage', ph: 'Stage', accent: '', icon: 'ri-scissors-fill' },
        { key: 'cat1', mount: 'fsd-cat1', ph: 'Category', accent: '', icon: 'ri-price-tag-3-fill' }
    ],
    stats: [
        { el: 'stat-total', label: 'Total Records', kind: 'count' },
        { el: 'stat-rcvd', label: 'Received Qty', kind: 'sum', key: 'r_qty' },
        { el: 'stat-issued', label: 'Issued Qty', kind: 'sum', key: 'i_qty' },
        { el: 'stat-bal', label: 'Balance Qty', kind: 'sum', key: 'balqty' }
    ],
    searchFields: [],
    numericKeys: ['r_qty', 'i_qty', 'balqty']
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
    if (Array.isArray(window.__SB_COLUMNS__) && window.__SB_COLUMNS__.length) return window.__SB_COLUMNS__;
    return SB_CONFIG.columns.map(c => c.key);
}

function isDateLike(s) {
    return /^\d{4}-\d{2}-\d{2}/.test(String(s));
}

function cellText(cell) {
    if (cell == null || cell === '') return '-';
    if (typeof cell === 'number' || !isNaN(Number(cell))) return esc(String(cell));
    if (isDateLike(cell)) return String(cell).slice(0, 10);
    return esc(cell);
}

function cellDate(cell) {
    return cell ? String(cell).slice(0, 10) : '-';
}

function isNumberLike(v) {
    return v != null && v !== '' && !isNaN(Number(v));
}

function qtyCell(cell) {
    const n = isNumberLike(cell) ? Number(cell) : cell;
    const text = isNumberLike(cell) ? String(n) : String(cell == null ? '' : cell);
    let cls = '';
    if (typeof cell === 'number') {
        cls = cell === 0 ? 'qty-zero' : (cell > 0 ? 'qty-pending' : '');
    }
    return gridjs.html('<span class="qty-cell ' + cls + '">' + esc(text) + '</span>');
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
        filtered.forEach(function (o) {
            const sel = (o.value === self._value) ? ' sel' : '';
            html += '<div class="fsd-opt' + sel + '" data-value="' + esc(o.value) + '">' +
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
    return [...new Set(vals.filter(v => v != null && v !== ''))].sort();
}

function syncFilterOptions() {
    SB_CONFIG.filters.forEach(function (f) {
        const flt = flts[f.key];
        if (!flt) return;
        const cur = flt.value();
        const vals = unique(allRows.map(r => rowGet(r, f.key)));
        flt.setOptions(vals);
        flt.setValue(cur, false);
    });
}

function inDateRange(r) {
    const dFrom = $('from-date') ? $('from-date').value : '';
    const dTo = $('to-date') ? $('to-date').value : '';
    if (!dFrom && !dTo) return true;
    const v = rowGet(r, 'date');
    if (v == null || v === '') return false;
    const d = String(v).slice(0, 10);
    if (dFrom && d < dFrom) return false;
    if (dTo && d > dTo) return false;
    return true;
}

function getFiltered() {
    const q = $('search-client').value.trim().toLowerCase();
    return allRows.filter(function (r) {
        for (let i = 0; i < SB_CONFIG.filters.length; i++) {
            const f = SB_CONFIG.filters[i];
            const v = flts[f.key] ? flts[f.key].value() : '';
            if (!v) continue;
            if (String(rowGet(r, f.key) || '').toLowerCase() !== String(v).toLowerCase()) return false;
        }
        if (!inDateRange(r)) return false;
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
    refreshTable(getFiltered());
}

function computeStats(rows) {
    SB_CONFIG.stats.forEach(function (s) {
        const el = $(s.el);
        if (!el) return;
        let n = 0;
        if (s.kind === 'count') {
            n = rows.length;
        } else if (s.kind === 'sum') {
            n = rows.reduce(function (acc, r) {
                const v = rowGet(r, s.key);
                const num = (v != null && v !== '' && !isNaN(Number(v))) ? Number(v) : 0;
                return acc + num;
            }, 0);
        }
        animateCount(el, n);
    });
}

function animateCount(el, target) {
    if (!el) return;
    const start = performance.now();
    const dur = 600;
    const isFloat = !Number.isInteger(target);
    function step(now) {
        const p = Math.min((now - start) / dur, 1);
        const v = target * (1 - Math.pow(1 - p, 3));
        el.textContent = isFloat ? v.toFixed(2) : String(Math.round(v));
        if (p < 1) requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
}

function applyData(rows) {
    allRows = rows;
    rowMap.length = 0;
    rows.forEach(function (r, i) {
        r.__rowMapIndex = i;
        rowMap[i] = r;
    });
    syncFilterOptions();
    computeStats(rows);
    refreshTable(getFiltered());
    $('loading').classList.add('d-none');
    $('sb-grid').classList.remove('d-none');
}

function loadData() {
    $('loading').classList.remove('d-none');
    $('sb-grid').classList.add('d-none');

    if (Array.isArray(window.__SB_DATA__)) {
        applyData(window.__SB_DATA__);
        return;
    }
    if (window.__SB_ERROR__) {
        $('loading').classList.add('d-none');
        $('sb-grid').classList.remove('d-none');
        toast('Data load failed: ' + window.__SB_ERROR__, true);
        return;
    }

    fetch('/' + SB_CONFIG.controller + '/Items').then(r => r.json())
        .then((res) => {
            const rows = (res && Array.isArray(res.rows)) ? res.rows : null;
            if (!rows) {
                throw new Error((res && res.message) ? res.message : 'Invalid response from server');
            }
            window.__SB_COLUMNS__ = (res && Array.isArray(res.columns) && res.columns.length)
                ? res.columns : window.__SB_COLUMNS__;
            buildGrid();
            applyData(rows);
        })
        .catch(err => {
            $('loading').classList.add('d-none');
            $('sb-grid').classList.remove('d-none');
            toast('Data load failed: ' + err.message, true);
        });
}

function buildGridColumns() {
    const keys = allColumnKeys();
    const known = {};
    SB_CONFIG.columns.forEach(c => known[c.key] = c);

    const cols = [];
    keys.forEach(function (key) {
        const k = String(key).toLowerCase();
        if (k === 'actions' || k === '__rowmapindex') return;
        const kc = known[k];
        const label = kc ? kc.label : titleCase(k);
        const col = { name: label, data: r => rowGet(r, k), sort: true };

        if (kc && kc.type === 'bold') col.formatter = cell => gridjs.html('<span class="fw-semibold">' + esc(cell) + '</span>');
        else if (kc && kc.type === 'date') col.formatter = cell => cellDate(cell);
        else if (kc && kc.type === 'qty') col.formatter = cell => qtyCell(cell);
        else col.formatter = cell => cellText(cell);

        cols.push({ colDef: col, key: k, label: label, icol: { name: label, data: col.data, sort: col.sort, formatter: col.formatter } });
    });
    return cols;
}

let exportCols = [];

function buildGrid() {
    const built = buildGridColumns();
    exportCols = built.map(function (b) { return { key: b.key, label: b.label }; });

    const columns = built.map(function (b) { return b.icol; });
    columns.push({
        name: 'ACTIONS', width: '60px', data: r => rowGet(r, '__rowMapIndex'), sort: false,
        formatter: cell => gridjs.html(
            '<button type="button" class="btn btn-sm btn-soft-info" onclick="viewItem(' + cell + ')" title="View"><i class="ri-eye-line"></i></button>'
        )
    });

    grid.updateConfig({ columns: columns });
    grid.forceRender();
}

function viewItem(idx) {
    const r = rowMap[idx];
    if (!r) return;
    const detail = $('view-detail');
    detail.innerHTML = '';

    const keys = allColumnKeys();
    let firstVal = '';
    keys.forEach(function (key) {
        const k = String(key).toLowerCase();
        if (k === 'actions' || k === '__rowmapindex') return;
        if (!firstVal) firstVal = rowGet(r, k);
        const kc = (SB_CONFIG.columns.find(c => c.key === k) || {});
        const label = kc.label || titleCase(k);
        const val = rowGet(r, k);
        const display = (kc.type === 'date') ? cellDate(val)
            : (val == null || val === '') ? '-'
            : (kc.type === 'qty' && isNumberLike(val)) ? String(Number(val))
            : String(val);

        const colHtml = document.createElement('div');
        colHtml.className = 'col-6';
        colHtml.innerHTML = '<div class="view-field"><label>' + esc(label) + '</label><p>' + esc(display) + '</p></div>';
        detail.appendChild(colHtml);
    });

    const title = firstVal != null ? 'Stage Balance - ' + firstVal : 'Stage Balance';
    $('viewModalLabel').textContent = title;
    $('v-id').textContent = keys.length ? keys.length + ' fields' : '';
    viewModal.show();
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
    download('StageBalance.xls', '\uFEFF' + headerRows(), 'application/vnd.ms-excel');
}

function headerRows() {
    const rows = getFiltered();
    const head = ['Sr.No'].concat(exportCols.map(c => esc(c.label))).map(h => '<th>' + h + '</th>').join('');
    const body = rows.map(function (r, i) {
        const cells = exportCols.map(function (c) {
            let v = rowGet(r, c.key);
            if (v != null && isDateLike(v)) v = String(v).slice(0, 10);
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
    w.document.write('<!DOCTYPE html><html><head><title>' + esc(SB_CONFIG.title) + '</title>');
    w.document.write('<style>body{font-family:Arial,sans-serif;padding:20px}h2{margin-bottom:10px}' +
        'table{border-collapse:collapse;width:100%;font-size:12px}th,td{border:1px solid #ddd;padding:5px 8px;text-align:left}' +
        'th{background:#1e3a5f;color:#fff}</style></head><body>');
    w.document.write('<h2>' + esc(SB_CONFIG.title) + ' Report</h2>');
    w.document.write('<p>' + rows.length + ' of ' + allRows.length + ' records</p>');
    w.document.write(headerRows());
    w.document.write('</body></html>');
    w.document.close();
    w.focus();
    w.print();
}

$('btn-clear').addEventListener('click', function () {
    SB_CONFIG.filters.forEach(function (f) {
        if (flts[f.key]) flts[f.key].setValue('', false);
    });
    $('search-client').value = '';
    $('from-date').value = '';
    $('to-date').value = '';
    applyFilters();
});

$('from-date').addEventListener('change', function () { refreshTable(getFiltered()); });
$('to-date').addEventListener('change', function () { refreshTable(getFiltered()); });

$('btn-excel').addEventListener('click', function () { exportExcel(getFiltered()); });
$('btn-csv').addEventListener('click', function () { exportCsv(getFiltered(), 'StageBalance.csv'); });
$('btn-print').addEventListener('click', printTable);

SB_CONFIG.filters.forEach(function (f) {
    flts[f.key] = new FilterSelect(f.mount, {
        placeholder: f.ph, accent: f.accent, icon: f.icon
    });
    if (flts[f.key]) flts[f.key].onChange(applyFilters);
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

grid = new gridjs.Grid({
    columns: [],
    data: [],
    sort: true,
    search: false,
    pagination: { limit: 10 },
    className: { table: 'table table-bordered table-striped table-hover align-middle m-0' }
}).render($('sb-grid'));

viewModal = new bootstrap.Modal($('viewModal'));
buildGrid();
loadData();