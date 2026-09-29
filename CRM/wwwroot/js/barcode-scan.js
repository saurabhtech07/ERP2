/* ============================================================
   Barcode Scan page - client script
   Same backend calls as before (GetScanned / ScanBarcode /
   Clear / Export). Only display concerns added: search-clear
   button, client-side paging, print and the filter collapse.
   ============================================================ */

(function () {
    'use strict';

    /* One place to change if the page ever moves. */
    var BASE = '/BarcodeScan';

    var $ = function (id) { return document.getElementById(id); };
    var state = { columns: [], rows: [], view: [], page: 0, sortCol: null, sortDir: 1 };

    /* --------------------------------------------------------- utils */

    function text(v) { return v === null || v === undefined ? '' : String(v); }
    function lower(v) { return text(v).toLowerCase(); }

    function prettyKey(key) {
        var s = text(key).replace(/[_\-]+/g, ' ').trim();
        return s.charAt(0).toUpperCase() + s.slice(1);
    }

    function escHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function toast(message, isError) {
        if (typeof Toastify === 'function') {
            Toastify({
                text: message,
                duration: 3000,
                gravity: 'top',
                position: 'right',
                className: 'rounded',
                backgroundColor: isError ? '#f06548' : '#10b981'
            }).showToast();
            return;
        }
        window.alert(message);
    }

    function say(message, kind) {
        var box = $('lastScanMsg');
        box.textContent = message;
        box.className = 'scan-msg show ' + (kind || '');
    }

    /* ------------------------------------------------- last scanned */

    /* The procedure returns Alert, Message, barcode, Name, Category1,
       Color, Category3 and Username. Anything it grows later shows up
       too - the card renders whatever the SP handed back. */
    var CARD_KEYS = ['barcode', 'Name', 'Category1', 'Color', 'Category3', 'Username'];
    var SKIP_KEYS = { alert: 1, message: 1 };

    function renderCard(row, alert) {
        var card = $('lastScannedCard');
        var grid = $('dynamicFields');
        var badge = $('alertBadge');
        grid.innerHTML = '';

        var keys = [];
        CARD_KEYS.forEach(function (k) {
            if (row && row[k] != null && text(row[k]).trim() !== '' && keys.indexOf(k) === -1) keys.push(k);
        });
        Object.keys(row || {}).forEach(function (k) {
            if (SKIP_KEYS[lower(k)] || keys.indexOf(k) !== -1) return;
            if (row[k] == null || text(row[k]).trim() === '') return;
            keys.push(k);
        });

        if (keys.length === 0) { card.classList.remove('show'); return; }

        keys.forEach(function (k) {
            var cell = document.createElement('div');
            cell.className = 'ls-field';

            var label = document.createElement('div');
            label.className = 'ls-field-label';
            label.textContent = prettyKey(k);

            var value = document.createElement('div');
            value.className = 'ls-field-value' + (lower(k) === 'barcode' ? ' mono' : '');
            value.textContent = text(row[k]);

            cell.appendChild(label);
            cell.appendChild(value);
            grid.appendChild(cell);
        });

        var flag = text(alert).trim().toUpperCase();
        badge.textContent = flag || '-';
        badge.className = 'mbx-alert' + (flag === 'Y' ? ' ok' : flag === 'N' ? ' err' : '');
        card.classList.add('show');
    }

    /* --------------------------------------------------------- table */

    function pageSize() {
        var n = parseInt($('page-size').value, 10);
        return isNaN(n) ? 10 : n;
    }

    /* Headers and cells both come from the keys the query returned.
       No column is named in the markup, so a column added to the
       view tomorrow shows up on its own. */
    function renderTable() {
        var head = $('tableHead');
        var body = $('tableBody');
        head.innerHTML = '';
        body.innerHTML = '';

        $('record-count').textContent = state.view.length + ' records';

        /* Columns come straight from the server so a field added to the
           SP or the query shows up here on its own - nothing is named. */
        var cols = state.columns.length
            ? state.columns.slice()
            : (state.view.length ? Object.keys(state.view[0]) : []);

        var headRow = document.createElement('tr');
        cols.forEach(function (c) {
            var th = document.createElement('th');
            var isSorted = state.sortCol === c;
            if (isSorted) th.className = 'sorted';
            th.title = sortAria(c);
            th.setAttribute('aria-sort', isSorted ? (state.sortDir === 1 ? 'ascending' : 'descending') : 'none');
            th.setAttribute('data-col', c);

            var label = document.createElement('span');
            label.className = 'th-label';
            label.appendChild(document.createTextNode(prettyKey(c)));

            var ic = document.createElement('i');
            ic.className = 'ri ' + sortIconFor(c) + ' sort-ic';
            label.appendChild(ic);

            th.appendChild(label);
            th.addEventListener('click', function () { setSort(c); });
            headRow.appendChild(th);
        });
        if (cols.length) head.appendChild(headRow);

        if (!state.view.length) {
            var tr = document.createElement('tr');
            var td = document.createElement('td');
            td.className = 'mbx-empty';
            td.colSpan = 99;
            td.textContent = state.rows.length
                ? 'No row matches the search.'
                : 'No scans yet - Scan 160407 to insert via SP_InsertWebBarcode';
            tr.appendChild(td);
            body.appendChild(tr);
            updatePager();
            return;
        }

        var size = pageSize();
        var pages = size > 0 ? Math.ceil(state.view.length / size) : 1;
        if (state.page >= pages) state.page = pages - 1;
        if (state.page < 0) state.page = 0;

        var slice = size > 0 ? state.view.slice(state.page * size, state.page * size + size) : state.view;

        slice.forEach(function (row) {
            var tr = document.createElement('tr');

            cols.forEach(function (c) {
                var td = document.createElement('td');
                var v = row[c];
                var key = lower(c);

                if (key === 'barcode') {
                    td.className = 'mbx-barcode-cell';
                    var value = text(v).trim();
                    if (value) {
                        var strip = document.createElement('span');
                        strip.className = 'barcode-strip';
                        strip.textContent = value;
                        td.appendChild(strip);
                    }
                } else if (key === 'alert') {
                    var flag = text(v).trim().toUpperCase();
                    var chip = document.createElement('span');
                    chip.className = 'mbx-chip ' + (flag === 'Y' ? 'ok' : 'err');
                    chip.textContent = flag || '-';
                    td.appendChild(chip);
                } else if (key === 'scandate') {
                    td.className = 'mono';
                    td.textContent = text(v);
                } else if (key.indexOf('photo') !== -1 && v) {
                    var img = document.createElement('img');
                    img.className = 'mbx-thumb';
                    img.src = text(v);
                    img.alt = 'Photo';
                    td.appendChild(img);
                } else if (key === 'color' && text(v).trim() !== '') {
                    var dot = document.createElement('span');
                    dot.className = 'mbx-swatch';
                    dot.style.setProperty('--sw', swatch(text(v)));
                    td.appendChild(dot);
                    td.appendChild(document.createTextNode(text(v)));
                } else {
                    td.textContent = text(v);
                }

                tr.appendChild(td);
            });

            body.appendChild(tr);
        });

        updatePager();
    }

    /* ---- sorting ---- */

    /* Dates arrive as "29-09-2026 15:04:22" or ISO. They are normalised to
       a sortable key first, otherwise a date would sort alphabetically. */
    function sortValue(v) {
        var s = String(v == null ? '' : v).trim();
        if (!s) return { n: 1, s: '' };

        var m = s.match(/^(\d{4})-(\d{2})-(\d{2})[T ]/);
        if (m) return { n: 0, s: m[1] + m[2] + m[3] + s.slice(m[0].length) };

        m = s.match(/^(\d{2})-(\d{2})-(\d{4})[T ]/);
        if (m) return { n: 0, s: m[3] + m[2] + m[1] + s.slice(m[0].length) };

        var num = Number(s.replace(/,/g, ''));
        if (/\d/.test(s) && isFinite(num)) return { n: 0, s: '', num: num };

        return { n: 1, s: s.toLowerCase() };
    }

    function compareValues(a, b) {
        if (a.n !== b.n) return a.n - b.n;          /* numbers and dates before text */
        if (a.n === 0) {
            if (a.s && b.s) return a.s < b.s ? -1 : a.s > b.s ? 1 : 0;
            return (a.num || 0) - (b.num || 0);
        }
        return a.s < b.s ? -1 : a.s > b.s ? 1 : 0;
    }

    function applySort() {
        if (!state.sortCol) return;
        var col = state.sortCol;
        state.view.sort(function (ra, rb) {
            return state.sortDir * compareValues(sortValue(ra[col]), sortValue(rb[col]));
        });
    }

    /* First click sorts ascending, second descending, third clears the sort. */
    function setSort(col) {
        if (state.sortCol === col) {
            if (state.sortDir === 1) state.sortDir = -1;
            else if (state.sortDir === -1) { state.sortCol = null; state.sortDir = 1; }
        } else {
            state.sortCol = col;
            state.sortDir = 1;
        }
        applySort();
        renderTable();
    }

    function sortIconFor(col) {
        if (state.sortCol !== col) return 'ri-arrow-up-down-line';
        return state.sortDir === 1 ? 'ri-arrow-up-line' : 'ri-arrow-down-line';
    }

    function sortAria(col) {
        if (state.sortCol !== col) return 'Sort by ' + prettyKey(col);
        return 'Sorted ' + (state.sortDir === 1 ? 'ascending' : 'descending');
    }

    function updatePager() {
        var size = pageSize();
        var total = state.view.length;
        var pages = size > 0 ? Math.max(1, Math.ceil(total / size)) : 1;

        $('pagePrev').disabled = state.page <= 0;
        $('pageNext').disabled = state.page >= pages - 1;

        var info = $('pageInfo');
        if (info) {
            info.textContent = total
                ? 'Page ' + (state.page + 1) + ' of ' + pages
                : 'No records';
        }
    }

    /* Colours arrive as names ("BLACK / GREY"), so the dot uses a
       keyword map and falls back to a neutral swatch. */
    function swatch(value) {
        var v = lower(value);
        var map = {
            black: '#18181b', white: '#ffffff', grey: '#9ca3af', gray: '#9ca3af',
            red: '#dc2626', maroon: '#7f1d1d', blue: '#2563eb', navy: '#1e3a8a',
            green: '#16a34a', olive: '#4d7c0f', yellow: '#eab308', orange: '#f97316',
            brown: '#92400e', beige: '#e7d8c9', cream: '#fdf6e3', pink: '#ec4899',
            purple: '#7c3aed', violet: '#8b5cf6', skin: '#e8c39e', mustard: '#ca8a04'
        };
        for (var key in map) {
            if (Object.prototype.hasOwnProperty.call(map, key) && v.indexOf(key) !== -1) return map[key];
        }
        return '#f4f4f5';
    }

    function filter() {
        var q = $('searchBox').value.trim().toLowerCase();
        if (!q) {
            state.view = state.rows.slice();
        } else {
            state.view = state.rows.filter(function (r) {
                return Object.keys(r).some(function (k) {
                    return lower(r[k]).indexOf(q) !== -1;
                });
            });
        }
        applySort();
        state.page = 0;
        renderTable();
    }

    /* ---------------------------------------------------------- load */

    function loadScanned() {
        return fetch(BASE + '/GetScanned', { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (!data || !data.success) {
                    toast((data && data.message) || 'Could not load scanned rows.', true);
                    return;
                }
                state.rows = data.rows || [];
                state.columns = data.columns || [];
                filter();
            })
            .catch(function () { toast('Could not reach the server.', true); });
    }

    /* ---------------------------------------------------------- scan */

    function scan(barcode) {
        say('Scanning ' + barcode + '...', 'busy');
        $('scanBtn').disabled = true;
        return fetch(BASE + '/ScanBarcode', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ Barcode: barcode })
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                var ok = data && data.success;
                say(data && data.message ? data.message : (ok ? 'Scanned.' : 'Scan failed.'), ok ? 'ok' : 'err');
                renderCard((data && data.row) || {}, data && data.alert);
                return loadScanned();
            })
            .catch(function () { say('Scan failed - could not reach the server.', 'err'); })
            .then(function () { $('scanBtn').disabled = false; });
    }

    function runScan() {
        var box = $('barcodeInput');
        var value = box.value.trim();
        if (!value) { box.focus(); return; }
        box.value = '';
        scan(value);
    }

    /* Enter key still works - a scanner that types the code then sends
       Enter should not need the button. */
    $('barcodeInput').addEventListener('keydown', function (e) {
        if (e.key !== 'Enter') return;
        e.preventDefault();
        runScan();
    });

    $('scanBtn').addEventListener('click', runScan);

    /* ---- search + clear pill (same behaviour as Dynamic Report) ---- */

    var searchTimer = null;

    $('searchBox').addEventListener('input', function () {
        $('searchClear').style.display = this.value ? 'inline-flex' : 'none';
        clearTimeout(searchTimer);
        searchTimer = setTimeout(filter, 250);
    });

    $('searchClear').addEventListener('click', function () {
        $('searchBox').value = '';
        $('searchClear').style.display = 'none';
        $('searchBox').focus();
        clearTimeout(searchTimer);
        filter();
    });

    $('page-size').addEventListener('change', function () {
        this.blur();
        state.page = 0;
        renderTable();
    });

    $('pagePrev').addEventListener('click', function () {
        if (state.page > 0) { state.page--; renderTable(); }
    });

    $('pageNext').addEventListener('click', function () {
        var size = pageSize();
        var pages = size > 0 ? Math.ceil(state.view.length / size) : 1;
        if (state.page < pages - 1) { state.page++; renderTable(); }
    });

    /* Wipes the page as well as the table, so nothing is left half-cleared:
       the typed barcode, the search box, any highlighted text and the last
       scanned card all go back to their empty state. */
    function resetPage() {
        $('barcodeInput').value = '';
        $('searchBox').value = '';
        $('searchClear').style.display = 'none';
        $('lastScannedCard').classList.remove('show');
        $('dynamicFields').innerHTML = '';
        $('alertBadge').textContent = '—';
        $('alertBadge').className = 'mbx-alert';
        $('lastScanMsg').textContent = '';
        $('lastScanMsg').className = 'scan-msg';
        state.page = 0;
        state.sortCol = null;
        state.sortDir = 1;
        try { window.getSelection().removeAllRanges(); } catch (e) { /* older browsers */ }
    }

    $('clearBtn').addEventListener('click', function () {
        resetPage();
        clearTimeout(searchTimer);
        filter();
    });

    /* ---- export / print : same logic the Dynamic Report uses ---- */

    function gridColumns() {
        if (state.columns.length) return state.columns.slice();
        return state.view.length ? Object.keys(state.view[0]) : [];
    }

    function reportTitle() {
        var el = $('reportName');
        return el ? el.textContent.trim() : 'Report';
    }

    /* Dates come as "29-09-2026 15:04:22" - exports only need the day part. */
    function csvVal(v) {
        var s = String(v == null ? '' : v);
        if (/^\d{4}-\d{2}-\d{2}[T ]/.test(s)) s = s.slice(0, 10);
        else if (/^\d{2}-\d{2}-\d{4}[T ]/.test(s)) s = s.slice(0, 10);
        return '"' + s.replace(/"/g, '""') + '"';
    }

    function download(filename, content, type) {
        var blob = new Blob([content], { type: type + ';charset=utf-8;' });
        var a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(a.href);
    }

    function exportCsv() {
        var cols = gridColumns();
        var header = ['Sr.No'].concat(cols.map(prettyKey)).join(',');
        var lines = state.view.map(function (r, i) {
            return (i + 1) + ',' + cols.map(function (c) { return csvVal(r[c]); }).join(',');
        });
        download(reportTitle().replace(/[^\w-]+/g, '_') + '_' + new Date().toISOString().slice(0, 10) + '.csv',
            '\uFEFF' + header + '\n' + lines.join('\n'), 'text/csv');
    }

    function exportExcel() {
        var cols = gridColumns();
        var head = ['Sr.No'].concat(cols.map(function (c) { return '<th>' + escHtml(prettyKey(c)) + '</th>'; })).join('');
        var body = state.view.map(function (r, i) {
            var cells = cols.map(function (c) { return '<td>' + escHtml(r[c]) + '</td>'; });
            return '<tr><td>' + (i + 1) + '</td>' + cells.join('') + '</tr>';
        }).join('');
        download(reportTitle().replace(/[^\w-]+/g, '_') + '_' + new Date().toISOString().slice(0, 10) + '.xls',
            '\uFEFF' + '<table border="1"><tr>' + head + '</tr>' + body + '</table>', 'application/vnd.ms-excel');
    }

    function printTable() {
        var cols = gridColumns();
        var w = window.open('', '_blank');
        if (!w) { toast('Popup blocked. Allow popups to print.', true); return; }

        var head = ['Sr.No'].concat(cols.map(function (c) { return '<th>' + escHtml(prettyKey(c)) + '</th>'; })).join('');
        var body = state.view.map(function (r, i) {
            var cells = cols.map(function (c) {
                var v = r[c];
                var s = String(v == null ? '' : v);
                if (/^\d{4}-\d{2}-\d{2}[T ]/.test(s) || /^\d{2}-\d{2}-\d{4}[T ]/.test(s)) s = s.slice(0, 10);
                return '<td>' + escHtml(s) + '</td>';
            });
            return '<tr><td>' + (i + 1) + '</td>' + cells.join('') + '</tr>';
        }).join('');

        w.document.write('<!DOCTYPE html><html><head><title>' + escHtml(reportTitle()) + '</title>');
        w.document.write('<style>body{font-family:Arial,sans-serif;padding:20px}h2{margin-bottom:10px}' +
            'table{border-collapse:collapse;width:100%;font-size:12px}th,td{border:1px solid #ddd;padding:5px 8px;text-align:left}' +
            'th{background:#1e3a5f;color:#fff}</style></head><body>');
        w.document.write('<h2>' + escHtml(reportTitle()) + ' Report</h2>');
        w.document.write('<p>' + state.view.length + ' of ' + state.rows.length + ' records</p>');
        w.document.write('<table border="1"><tr>' + head + '</tr>' + body + '</table>');
        w.document.write('</body></html>');
        w.document.close();
        w.focus();
        w.print();
    }

    $('exportCsvBtn').addEventListener('click', exportCsv);
    $('exportExcelBtn').addEventListener('click', exportExcel);
    $('printBtn').addEventListener('click', printTable);

    /* --------------------------------------------------------- modal */

    var modal = $('masterModal');
    var drop = $('photoDrop');
    var file = $('photoInput');
    var preview = $('photoPreview');
    var chosen = null;

    function openModal() { modal.classList.add('show'); }
    function closeModal() { modal.classList.remove('show'); }

    function resetPhoto() {
        chosen = null;
        file.value = '';
        preview.removeAttribute('src');
        preview.classList.remove('show');
        $('photoPreviewArea').style.display = '';
        $('removePhoto').classList.remove('show');
    }

    function takePhoto(f) {
        if (!f) return;
        if (!/^image\//.test(f.type)) { toast('Only image files are allowed.', true); return; }
        if (f.size > 5 * 1024 * 1024) { toast('Image is larger than 5MB.', true); return; }
        chosen = f;
        var reader = new FileReader();
        reader.onload = function (e) {
            preview.src = e.target.result;
            preview.classList.add('show');
            $('photoPreviewArea').style.display = 'none';
            $('removePhoto').classList.add('show');
        };
        reader.readAsDataURL(f);
    }

    $('addMasterBtn').addEventListener('click', openModal);
    $('closeModal').addEventListener('click', closeModal);
    $('cancelMaster').addEventListener('click', closeModal);
    $('modalOverlay').addEventListener('click', closeModal);
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && modal.classList.contains('show')) closeModal();
    });

    drop.addEventListener('click', function () { file.click(); });
    drop.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); file.click(); }
    });
    file.addEventListener('change', function () { takePhoto(file.files && file.files[0]); });

    ['dragenter', 'dragover'].forEach(function (name) {
        drop.addEventListener(name, function (e) { e.preventDefault(); drop.classList.add('over'); });
    });
    ['dragleave', 'drop'].forEach(function (name) {
        drop.addEventListener(name, function (e) { e.preventDefault(); drop.classList.remove('over'); });
    });
    drop.addEventListener('drop', function (e) {
        var list = e.dataTransfer && e.dataTransfer.files;
        if (list && list.length) takePhoto(list[0]);
    });

    $('removePhoto').addEventListener('click', function (e) {
        e.stopPropagation();
        resetPhoto();
    });

    $('saveMaster').addEventListener('click', function () {
        var barcode = $('fBarcode').value.trim();
        if (!barcode) { toast('Barcode is required.', true); $('fBarcode').focus(); return; }

        var body = new FormData();
        body.append('Barcode', barcode);
        body.append('Name', $('fName').value.trim());
        body.append('Category1', $('fCategory1').value.trim());
        body.append('Color', $('fColor').value.trim());
        body.append('Category3', $('fCategory3').value.trim());
        if (chosen) body.append('Photo', chosen);

        $('saveMaster').disabled = true;
        fetch(BASE + '/SaveMaster', { method: 'POST', body: body })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (data && data.success) {
                    toast(data.message || 'Saved.', false);
                    closeModal();
                    ['fBarcode', 'fName', 'fCategory1', 'fColor', 'fCategory3'].forEach(function (id) {
                        $(id).value = '';
                    });
                    resetPhoto();
                    return loadScanned();
                }
                toast((data && data.message) || 'Save failed.', true);
            })
            .catch(function () { toast('Save failed.', true); })
            .then(function () { $('saveMaster').disabled = false; });
    });

    loadScanned();
})();
