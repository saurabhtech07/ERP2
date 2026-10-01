/* =============================================================================
   category-page.js
   Drives the Category screens. Kept out of the view because the panel is
   re-rendered when the user switches group from the sidebar - init() tears the
   old Grid instance down and wires the new markup, which is only possible if
   the logic can run more than once.
   ============================================================================= */
(function () {
    'use strict';

    var state = {
        groups: [],
        activeTable: '',
        columns: [],
        sourceColumns: [],
        columnMeta: [],
        rows: [],
        keyColumn: 'id',
        search: '',
        grid: null
    };

    var panel = null;
    var els = {};
    var searchTimer = null;

    function $(id) { return document.getElementById(id); }

    function esc(value) {
        if (value === null || value === undefined) return '';
        return String(value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function toast(message, type) {
        if (typeof Toastify !== 'undefined') {
            Toastify({
                text: message,
                duration: 3600,
                gravity: 'bottom',
                position: 'right',
                style: {
                    background: type === 'error' ? '#e11d48' : '#1f9d55',
                    color: '#fff',
                    borderRadius: '10px',
                    boxShadow: '0 12px 28px -14px rgba(0,0,0,.6)'
                }
            }).showToast();
        } else if (window.console) {
            console[type === 'error' ? 'error' : 'log'](message);
        }
    }

    /* ------------------------------------------------------------- loading */

    function showLoader() {
        els.loading.hidden = false;
        els.body.setAttribute('aria-busy', 'true');
    }

    function hideLoader() {
        els.loading.hidden = true;
        els.body.removeAttribute('aria-busy');
    }

    /* ------------------------------------------------------------ the grid */

    function activeMeta() {
        var table = null, i;
        for (i = 0; i < state.groups.length; i++) {
            var found = (state.groups[i].tables || []).filter(function (t) {
                return t.tableName === state.activeTable;
            })[0];
            if (found) { table = found; break; }
        }
        return table;
    }

    function metaFor(column) {
        return (state.columnMeta || []).filter(function (m) {
            return m.name === column;
        })[0] || {};
    }

    function cellText(row, column) {
        var v = row[column];
        if (v === null || v === undefined) return '';
        if (v instanceof Date) return v.toISOString().slice(0, 10);
        if (typeof v === 'object' && v !== null && 'D' in v) {
            // Microsoft.Data.SqlClient returns date/time as { D: "2024-01-01T00:00:00" }
            return String(v.D).replace('T', ' ').slice(0, 19);
        }
        return String(v);
    }

    // Row limit for the exports. The SP is capped at 5000, so anything above that
    // would silently export a partial file - say so instead.
    var EXPORT_LIMIT = 5000;

    function visibleRows() {
        var term = state.search.trim().toLowerCase();
        if (!term) return state.rows;
        return state.rows.filter(function (row) {
            return state.columns.some(function (c) {
                return cellText(row, c).toLowerCase().indexOf(term) !== -1;
            });
        });
    }

    // The Edit button for one row. Returned as raw HTML on purpose, but it must be
    // wrapped in gridjs.html() before a formatter hands it over - Grid.js escapes a
    // plain string, which is what made the markup show up as visible text in the id
    // column. This mirrors what DynamicReport/Index.cshtml does.
    function actionsHtml(id) {
        var rowId = parseInt(id, 10);
        if (isNaN(rowId)) return '';
        return '<div class="cat-actions">' +
            '<button type="button" class="cat-act" data-edit-id="' + rowId +
            '" title="Edit" aria-label="Edit row ' + rowId + '">' +
            '<i class="ri-edit-line"></i></button></div>';
    }

    function isDateColumn(column) {
        var meta = metaFor(column);
        return !!meta.isDate;
    }

    // Flattens one server row into the plain object Grid.js maps by column id.
    function gridRow(row) {
        var obj = {};
        state.columns.forEach(function (column) {
            obj[column] = cellText(row, column);
        });
        obj.__catRowKey = cellText(row, state.keyColumn);
        return obj;
    }

    function buildGrid() {
        if (state.grid) {
            state.grid.destroy();
            state.grid = null;
        }

        var columns = state.columns.map(function (column, index) {
            var config = {
                id: column,
                name: (state.sourceColumns || [])[index] || column
            };
            // Dates arrive from JSON as a full ISO string; trim them to something
            // readable. This returns plain text, so it needs no gridjs.html().
            if (isDateColumn(column)) {
                config.formatter = function (cell) {
                    var text = String(cell === null || cell === undefined ? '' : cell);
                    return text.length > 10 ? text.slice(0, 10) : text;
                };
            }
            return config;
        });

        columns.push({
            name: 'ACTIONS',
            id: '__catActions',
            data: function (row) { return row ? (row.__catRowKey || '') : ''; },
            width: '64px',
            sort: false,
            formatter: function (cell) {
                return window.gridjs.html(actionsHtml(cell));
            }
        });

        state.grid = new gridjs.Grid({
            columns: columns,
            data: visibleRows().map(gridRow),
            sort: true,
            pagination: { limit: 10, summary: true },
            search: false
        });
        state.grid.render(els.grid);

        els.count.textContent = String(state.rows.length);
    }

    function repaintGrid() {
        if (!state.grid) return;
        state.grid.updateConfig({
            data: visibleRows().map(gridRow)
        }).forceRender();
        els.count.textContent = String(state.rows.length);
    }

    /* --------------------------------------------------------- data loading */

    function loadTable(tableName) {
        if (!tableName) return Promise.resolve();

        state.activeTable = tableName;
        showLoader();

        return fetch('/Category/GetRows?tableName=' + encodeURIComponent(tableName))
            .then(function (r) { return r.json(); })
            .then(function (data) {
                hideLoader();
                if (!data || !data.success) {
                    state.rows = [];
                    buildGrid();
                    toast((data && data.error) || 'Could not load.', 'error');
                    return;
                }

                state.columns = data.columns || [];
                state.sourceColumns = data.sourceColumns || [];
                state.columnMeta = data.columnMeta || [];
                state.rows = data.rows || [];
                state.keyColumn = data.keyColumn || 'id';
                state.search = '';
                els.search.value = '';
                els.searchClear.hidden = true;

                buildGrid();
                updateMeta();
            })
            .catch(function () {
                hideLoader();
                toast('Could not reach the server.', 'error');
            });
    }

    function updateMeta() {
        var table = activeMeta();
        els.meta.textContent = table
            ? table.label + '  \u00b7  ' + table.rowCount + ' record' + (table.rowCount === 1 ? '' : 's')
            : 'Select a category to begin';
    }

    // After a save the group JSON still holds the old row count. Adjusting it here
    // keeps the label honest until the panel is re-rendered.
    function bumpRowCount(action) {
        var table = activeMeta();
        if (!table) return;
        table.rowCount += action === 'INSERT' ? 1 : 0;
        updateMeta();
    }

    function fillSelect() {
        els.select.innerHTML = '';
        state.groups.forEach(function (group) {
            (group.tables || []).forEach(function (table) {
                var option = document.createElement('option');
                option.value = table.tableName;
                option.textContent = table.label + '  (' + table.rowCount + ')';
                els.select.appendChild(option);
            });
        });
        if (state.activeTable) els.select.value = state.activeTable;
    }

    /* -------------------------------------------------------------- exports */

    function download(filename, content, type) {
        var blob = new Blob([content], { type: type + ';charset=utf-8;' });
        var link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        setTimeout(function () { URL.revokeObjectURL(link.href); }, 1500);
    }

    function stamp() {
        return new Date().toISOString().slice(0, 10);
    }

    function exportCsv() {
        var rows = visibleRows();
        if (!rows.length) { toast('Nothing to export.', 'error'); return; }
        if (rows.length > EXPORT_LIMIT) {
            toast('Only the first ' + EXPORT_LIMIT + ' rows can be exported.', 'error');
            rows = rows.slice(0, EXPORT_LIMIT);
        }
        var head = state.columns.map(function (c, i) {
            return esc(state.sourceColumns[i] || c);
        });
        var body = rows.map(function (r) {
            return state.columns.map(function (c) {
                return '"' + cellText(r, c).replace(/"/g, '""') + '"';
            }).join(',');
        });
        download(
            (state.activeTable || 'category') + '_' + stamp() + '.csv',
            '\uFEFF' + head.join(',') + '\n' + body.join('\n'),
            'text/csv'
        );
    }

    function exportExcel() {
        var rows = visibleRows();
        if (!rows.length) { toast('Nothing to export.', 'error'); return; }
        if (rows.length > EXPORT_LIMIT) {
            toast('Only the first ' + EXPORT_LIMIT + ' rows can be exported.', 'error');
            rows = rows.slice(0, EXPORT_LIMIT);
        }
        var head = state.columns.map(function (c, i) {
            return '<th>' + esc(state.sourceColumns[i] || c) + '</th>';
        }).join('');
        var body = rows.map(function (r) {
            return '<tr>' + state.columns.map(function (c) {
                return '<td>' + esc(cellText(r, c)) + '</td>';
            }).join('') + '</tr>';
        }).join('');
        download(
            (state.activeTable || 'category') + '_' + stamp() + '.xls',
            '\uFEFF<table border="1">' +
            '<thead><tr><th>Sr.No</th>' + head + '</tr></thead><tbody>' +
            body + '</tbody></table>',
            'application/vnd.ms-excel'
        );
    }

    function printTable() {
        var rows = visibleRows();
        if (!rows.length) { toast('Nothing to print.', 'error'); return; }

        var win = window.open('', '_blank');
        if (!win) { toast('Popup blocked.', 'error'); return; }

        var accent = getComputedStyle(document.documentElement)
            .getPropertyValue('--vz-primary').trim() || '#405189';

        var head = state.columns.map(function (c, i) {
            return '<th>' + esc(state.sourceColumns[i] || c) + '</th>';
        }).join('');
        var body = rows.map(function (r, i) {
            return '<tr><td>' + (i + 1) + '</td>' + state.columns.map(function (c) {
                return '<td>' + esc(cellText(r, c)) + '</td>';
            }).join('') + '</tr>';
        }).join('');

        win.document.write(
            '<html><head><title>' + esc(state.activeTable) + '</title><style>' +
            'body{font-family:Segoe UI,Arial;padding:16px;}' +
            'table{border-collapse:collapse;width:100%;font-size:12px;}' +
            'th,td{border:1px solid #999;padding:5px 7px;text-align:left;}' +
            'th{background:' + esc(accent) + ';color:#fff;}' +
            '</style></head><body>' +
            '<h4>' + esc(state.activeTable) + '</h4><table><thead><tr><th>Sr.No</th>' +
            head + '</tr></thead><tbody>' + body + '</tbody></table></body></html>');
        win.document.close();
        win.focus();
        win.print();
    }

    /* ---------------------------------------------------------------- modal */

    function modal() {
        return window.bootstrap ? bootstrap.Modal.getOrCreateInstance($('catModal')) : null;
    }

    function openForm(id) {
        var isEdit = !!id;
        $('catModalLabel').textContent = isEdit ? 'Edit Category' : 'Add Category';
        $('catModalBody').innerHTML =
            '<div class="cat-modal-spinner"><div class="spinner-border text-primary" role="status">' +
            '<span class="visually-hidden">Loading&hellip;</span></div></div>';
        $('catModalFooter').hidden = true;

        var instance = modal();
        if (instance) instance.show();

        var url = '/Category/ModalForm?tableName=' +
            encodeURIComponent(state.activeTable) + '&id=' + encodeURIComponent(id || 0);

        fetch(url)
            .then(function (r) { return r.text(); })
            .then(function (html) {
                $('catModalBody').innerHTML = html;
                var broken = $('catModalBody').querySelector('.alert-danger');
                if (!broken) {
                    $('catModalFooter').hidden = false;
                    wireForm();
                }
            })
            .catch(function () {
                $('catModalBody').innerHTML =
                    '<div class="alert alert-danger mb-0">Could not load the form.</div>';
            });
    }

    function validate(input) {
        var label = input.dataset.label || input.name;
        var max = parseInt(input.dataset.maxlength || '0', 10);
        var value = (input.value || '').trim();
        var error = input.parentElement.querySelector('[data-error-for]');

        var message = '';
        if (input.required && !value) {
            message = label + ' is required.';
        } else if (max > 0 && value.length > max) {
            message = label + ' allows up to ' + max + ' characters.';
        } else if (value && input.dataset.type === 'number' && isNaN(Number(value))) {
            message = label + ' must be a number.';
        }

        if (error) error.textContent = message;
        input.classList.toggle('is-invalid', !!message);
        return !message;
    }

    function wireForm() {
        var form = $('catForm');
        if (!form) return;

        var inputs = Array.prototype.slice.call(form.querySelectorAll('.cat-input'));

        inputs.forEach(function (input) {
            // Live counter next to the label.
            var counter = form.querySelector('[data-counter-for="' + input.id + '"]');
            if (counter) {
                var sync = function () {
                    var used = (input.value || '').length;
                    var max = parseInt(input.dataset.maxlength || '0', 10);
                    counter.textContent = max > 0 ? used + '/' + max : String(used);
                    counter.classList.toggle('is-over', max > 0 && used > max);
                };
                input.addEventListener('input', sync);
                sync();
            }

            input.addEventListener('blur', function () { validate(input); });
            input.addEventListener('input', function () {
                if (input.classList.contains('is-invalid')) validate(input);
            });
        });

        form.addEventListener('submit', function (e) {
            e.preventDefault();
            save(inputs);
        });

        $('catSaveBtn').onclick = function () { save(inputs); };

        var first = inputs[0];
        if (first) setTimeout(function () { first.focus(); }, 220);
    }

    function save(inputs) {
        var invalid = inputs.filter(function (i) { return !validate(i); });
        if (invalid.length) {
            invalid[0].focus();
            return;
        }

        var form = $('catForm');
        var values = {};
        inputs.forEach(function (input) {
            values[input.name] = (input.value || '').trim();
        });

        var button = $('catSaveBtn');
        var original = button.innerHTML;
        button.disabled = true;
        button.innerHTML =
            '<span class="spinner-border spinner-border-sm me-1"></span>Saving\u2026';

        fetch('/Category/Save', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                tableName: form.closest('.cat-form').dataset.table,
                id: parseInt($('catId').value, 10) || 0,
                values: values
            })
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                button.disabled = false;
                button.innerHTML = original;

                if (!data || !data.success) {
                    toast((data && data.message) || 'Could not save.', 'error');
                    return;
                }

                var instance = modal();
                if (instance) instance.hide();
                toast((data.message || 'Saved.') + '  [' + (data.action || '') +
                    ' id=' + (data.id || 0) + ']');

                bumpRowCount(data.action);
                return loadTable(state.activeTable);
            })
            .catch(function () {
                button.disabled = false;
                button.innerHTML = original;
                toast('Could not reach the server.', 'error');
            });
    }

    /* ------------------------------------------------------------ navigation */

    function groupFromUrl() {
        var match = window.location.search.match(/[?&]group=([^&]+)/i);
        return match ? decodeURIComponent(match[1]) : '';
    }

    // The sidebar links stay real hrefs so the page still works without JS, but
    // once it is loaded the same links are fetched instead of reloaded.
    function wireSidebar() {
        var links = document.querySelectorAll(
            '#navbar-nav a[href*="/Category"][href*="group="]');

        Array.prototype.forEach.call(links, function (link) {
            link.addEventListener('click', function (e) {
                if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
                var target = (link.getAttribute('href').match(/group=([^&]+)/i) || [])[1];
                if (!target) return;
                e.preventDefault();
                if (target === currentGroup) return;
                switchGroup(target, true);
            });
        });
    }

    var currentGroup = '';

    function switchGroup(group, push) {
        if (!group || group === currentGroup) return Promise.resolve();
        currentGroup = group;

        showLoader();

        return fetch('/Category/IndexPartial?group=' + encodeURIComponent(group))
            .then(function (r) {
                if (!r.ok) throw new Error('bad status');
                return r.text();
            })
            .then(function (html) {
                panel.innerHTML = html;
                init(panel);
                hideLoader();

                // The sidebar marks the current link itself; its own script only
                // ever runs on a full load, so it needs telling.
                document.dispatchEvent(new CustomEvent('sidebar:refresh'));

                if (push) {
                    var url = '/Category?group=' + encodeURIComponent(group);
                    if (window.location.pathname + window.location.search !== url) {
                        window.history.pushState({ group: group }, '', url);
                    }
                }
            })
            .catch(function () {
                hideLoader();
                toast('Could not load that group.', 'error');
            });
    }

    /* ------------------------------------------------------------------ init */

    function init(root) {
        panel = root;

        if (state.grid) { state.grid.destroy(); state.grid = null; }

        els = {
            json: root.querySelector('#catGroupsJson'),
            loading: root.querySelector('#catLoading'),
            body: root.querySelector('#catBody'),
            meta: root.querySelector('#catModuleMeta'),
            select: root.querySelector('#catModule'),
            search: root.querySelector('#catSearch'),
            searchClear: root.querySelector('#catSearchClear'),
            add: root.querySelector('#catAdd'),
            count: root.querySelector('#catCount'),
            grid: root.querySelector('#catGrid'),
            csv: root.querySelector('#catCsv'),
            excel: root.querySelector('#catExcel'),
            print: root.querySelector('#catPrint')
        };

        state.groups = els.json ? JSON.parse(els.json.textContent || '[]') : [];
        state.activeTable = '';
        state.rows = [];
        state.columns = [];
        state.columnMeta = [];

        fillSelect();
        updateMeta();

        els.select.onchange = function () { loadTable(els.select.value); };

        els.search.oninput = function () {
            var term = els.search.value;
            els.searchClear.hidden = !term;
            clearTimeout(searchTimer);
            searchTimer = setTimeout(function () {
                state.search = term;
                repaintGrid();
            }, 200);
        };

        els.searchClear.onclick = function () {
            els.search.value = '';
            els.searchClear.hidden = true;
            state.search = '';
            repaintGrid();
            els.search.focus();
        };

        els.add.onclick = function () { openForm(0); };

        els.csv.onclick = exportCsv;
        els.excel.onclick = exportExcel;
        els.print.onclick = printTable;

        // Delegated so the grid can be re-rendered any number of times.
        els.grid.onclick = function (e) {
            var button = e.target.closest('[data-edit-id]');
            if (!button) return;
            openForm(parseInt(button.dataset.editId, 10) || 0);
        };

        // Ripple on the Add button.
        els.add.addEventListener('pointerdown', function (e) {
            var rect = els.add.getBoundingClientRect();
            var size = Math.max(rect.width, rect.height);
            var ripple = document.createElement('span');
            ripple.className = 'cat-ripple';
            ripple.style.width = ripple.style.height = size + 'px';
            ripple.style.left = (e.clientX - rect.left - size / 2) + 'px';
            ripple.style.top = (e.clientY - rect.top - size / 2) + 'px';
            els.add.appendChild(ripple);
            setTimeout(function () { ripple.remove(); }, 620);
        });

        var firstGroup = state.groups[0] || {};
        var first = (firstGroup.tables || [])[0] || {};
        if (first.tableName) return loadTable(first.tableName);
        return Promise.resolve();
    }

    document.addEventListener('DOMContentLoaded', function () {
        var root = document.getElementById('catPanel');
        if (!root) return;

        currentGroup = groupFromUrl();
        init(root);
        wireSidebar();

        window.addEventListener('popstate', function () {
            var group = groupFromUrl();
            if (group && group !== currentGroup) switchGroup(group, false);
        });
    });
})();
