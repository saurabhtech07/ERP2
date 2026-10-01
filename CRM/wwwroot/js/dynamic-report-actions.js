
(function () {
    'use strict';

    var state = window.__DRC_STATE__ = window.__DRC_STATE__ || {
        tableName: '',
        keyColumn: '',
        displayColumn: 'Name',
        isEditable: false,
        columns: [],
        columnTypes: [],
        rows: []
    };

    // Matches the property Index.cshtml stamps on every row object.
    var ROW_KEY = '__drcRowKey';

    var rowMap = {};
    var recordModal = null;
    var deleteModal = null;
    var deleteKey = null;


    // ---------- helpers ----------

    function esc(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // Row keys are lower-cased by DataAccess.NormalizeColumnName(),
    // so every lookup must be lower-cased too or it returns undefined.
    function normKey(name) {
        return String(name || '').toLowerCase();
    }

    function getKeyColumn() {
        return normKey(state.keyColumn || 'id');
    }

    function humanize(name) {
        var s = String(name || '').replace(/[_-]+/g, ' ').trim();
        return s.charAt(0).toUpperCase() + s.slice(1);
    }

    function columnTypeOf(name) {
        var idx = state.columns.findIndex(function (c) {
            return normKey(c) === normKey(name);
        });
        var type = idx !== -1 ? (state.columnTypes[idx] || '') : '';
        var t = String(type).toLowerCase();
        if (t.indexOf('datetime') !== -1 || t.indexOf('date') !== -1) return 'date';
        if (t.indexOf('bool') !== -1) return 'checkbox';
        if (t.indexOf('int') !== -1 || t.indexOf('decimal') !== -1 ||
            t.indexOf('double') !== -1 || t.indexOf('single') !== -1) return 'number';
        return 'text';
    }

    function toDateInput(value) {
        if (!value) return '';
        var d = new Date(value);
        if (isNaN(d.getTime())) return String(value);
        return d.toISOString().slice(0, 10);
    }

    function toast(text, isError) {
        if (typeof Toastify === 'undefined') {
            if (!isError) return;
            console.error(text);
            return;
        }
        Toastify({
            text: text,
            duration: isError ? 6000 : 2500,
            close: true,
            gravity: 'top',
            position: 'right',
            backgroundColor: isError ? '#f06548' : '#10b981'
        }).showToast();
    }

    function getModal(id) {
        var el = document.getElementById(id);
        if (!el || typeof bootstrap === 'undefined' || !bootstrap.Modal) return null;
        return bootstrap.Modal.getOrCreateInstance(el);
    }

    // ---------- actions column ----------

    // Exactly the same shape as Item Master's actionsHtml(id). Reports that are
    // not editable masters only get the View button.
    function actionsHtml(key, isEditable) {
        if (key === '' || key === null || key === undefined) {
            return '<span class="text-muted small">-</span>';
        }

        var editable = isEditable === undefined ? !!state.isEditable : !!isEditable;
        var dataKey = esc(String(key));

        var html = '<div class="drc-actions">';
        if (editable) {
            html += '<button type="button" class="btn btn-sm btn-soft-primary me-1 drc-action-btn" title="Edit" data-key="' + dataKey + '" onclick="drcEditRecord(this.dataset.key)"><i class="ri-pencil-fill"></i></button>';
            html += '<button type="button" class="btn btn-sm btn-soft-danger me-1 drc-action-btn" title="Delete" data-key="' + dataKey + '" onclick="drcDeleteRecord(this.dataset.key)"><i class="ri-delete-bin-fill"></i></button>';
        }
        html += '<button type="button" class="btn btn-sm btn-soft-info drc-action-btn" title="View" data-key="' + dataKey + '" onclick="drcViewRecord(this.dataset.key)"><i class="ri-eye-line"></i></button>';
        return html + '</div>';
    }

    // Called by Index.cshtml renderGrid() as the ACTIONS formatter.
    window.drcActionsHtml = actionsHtml;

    window.drcSetState = function (next) {
        Object.assign(state, next || {});
        rowMap = {};
        (state.rows || []).forEach(function (row) {
            var k = row ? row[ROW_KEY] : null;
            if (k === null || k === undefined || k === '') return;
            rowMap[String(k)] = row;
        });
    };

    // Reports are not guaranteed to have a "Name" column, so fall back to the
    // first non-empty value to title the modal with something recognisable.
    function resolveLabel(row) {
        var display = normKey(state.displayColumn);
        if (display && row[display] !== undefined && row[display] !== null &&
            String(row[display]).trim() !== '') {
            return row[display];
        }
        var columns = state.columns || [];
        for (var i = 0; i < columns.length; i++) {
            var v = row[normKey(columns[i])];
            if (v !== undefined && v !== null && String(v).trim() !== '') return v;
        }
        return '';
    }

    // ---------- view / edit ----------

    function openRecord(key, editMode) {
        var row = rowMap[String(key)];
        var body = document.getElementById('record-modal-body');
        var saveBtn = document.getElementById('record-save-btn');
        var title = document.getElementById('recordModalLabel');
        if (!row || !body) return;

        // A report that is not an editable master can only ever be viewed.
        var editable = !!state.isEditable;
        if (!editable) editMode = false;

        var keyCol = getKeyColumn();
        var label = resolveLabel(row);

        if (title) {
            title.textContent = (editMode ? 'Edit' : 'View') + (label ? ' ' + label : ' Record');
        }
        if (saveBtn) {
            saveBtn.style.display = editMode ? '' : 'none';
            saveBtn.setAttribute('data-key', String(key));
        }

        var html = '';
        // The synthetic row id is meaningless to the user, so only show the key
        // banner where there is a real key column behind it.
        if (editable) {
            html += '<div class="drc-key-row">' +
                '<div class="drc-field-label">' + esc(humanize(state.keyColumn || keyCol)) + '</div>' +
                '<div class="fw-semibold">' + esc(label || key) + '</div>' +
                '</div>';
        }
        html += '<div class="row g-3">';

        (state.columns || []).forEach(function (col) {
            var name = String(col);
            var raw = row[normKey(name)];
            var value = raw === null || raw === undefined ? '' : raw;
            var isKey = normKey(name) === keyCol;

            html += '<div class="col-md-6">' +
                '<label class="drc-field-label" for="drc_fld_' + esc(name) + '">' + esc(humanize(name)) + '</label>';

            if (!editMode || isKey) {
                html += '<div class="drc-readonly-value">' +
                    (value === '' ? '<span class="text-muted">-</span>' : esc(value)) + '</div>';
            } else {
                var type = columnTypeOf(name);
                if (type === 'checkbox') {
                    html += '<input type="checkbox" class="form-check-input" id="drc_fld_' + esc(name) +
                        '" data-col="' + esc(name) + '"' + (value ? ' checked' : '') + '>';
                } else {
                    html += '<input type="' + type + '" class="form-control" id="drc_fld_' + esc(name) +
                        '" data-col="' + esc(name) + '" value="' +
                        esc(type === 'date' ? toDateInput(value) : value) + '">';
                }
            }
            html += '</div>';
        });

        html += '</div>';

        if (editMode) {
            html += '<div class="drc-notice mt-3"><i class="ri-information-line"></i> ' +
                'Saving writes straight to the master table. If you see a permission error, ' +
                'your database login needs UPDATE access.</div>';
        }

        body.innerHTML = html;

        var modal = getModal('recordModal');
        if (modal) modal.show();
    }

    window.drcViewRecord = function (key) { openRecord(key, false); };
    window.drcEditRecord = function (key) { openRecord(key, !!state.isEditable); };

    function collectValues() {
        var values = {};
        document.querySelectorAll('#record-modal-body [data-col]').forEach(function (input) {
            var col = input.getAttribute('data-col');
            if (input.type === 'checkbox') {
                values[col] = input.checked ? '1' : '0';
            } else {
                values[col] = input.value;
            }
        });
        return values;
    }

    function handleSave(event) {
        event.preventDefault();

        var saveBtn = document.getElementById('record-save-btn');
        var key = saveBtn ? saveBtn.getAttribute('data-key') : '';
        if (!key || !state.isEditable) return;

        var original = saveBtn.innerHTML;
        saveBtn.disabled = true;
        saveBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Saving...';

        fetch('/DynamicReport/UpdateRecord', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                tableName: state.tableName,
                key: key,
                values: collectValues()
            })
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (data.success) {
                    toast('Record updated.', false);
                    var modal = getModal('recordModal');
                    if (modal) modal.hide();
                    if (typeof window.drcReload === 'function') window.drcReload();
                } else {
                    toast(data.message || 'Update failed.', true);
                }
            })
            .catch(function () { toast('Error connecting to server.', true); })
            .finally(function () {
                saveBtn.disabled = false;
                saveBtn.innerHTML = original;
            });
    }

    // ---------- delete ----------

    window.drcDeleteRecord = function (key) {
        if (!state.isEditable) return;
        var row = rowMap[String(key)];
        var label = document.getElementById('delete-label-text');
        if (label && row) {
            label.textContent = resolveLabel(row) || key;
        }
        deleteKey = String(key);
        var modal = getModal('deleteModal');
        if (modal) modal.show();
    };

    function handleDelete() {
        var btn = document.getElementById('delete-confirm-btn');
        if (!btn || !deleteKey || !state.isEditable) return;

        var original = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm"></span>';

        fetch('/DynamicReport/DeleteRecord', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                tableName: state.tableName,
                key: deleteKey
            })
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (data.success) {
                    toast('Record deleted.', false);
                    var modal = getModal('deleteModal');
                    if (modal) modal.hide();
                    if (typeof window.drcReload === 'function') window.drcReload();
                } else {
                    toast(data.message || 'Delete failed.', true);
                }
            })
            .catch(function () { toast('Error connecting to server.', true); })
            .finally(function () {
                btn.disabled = false;
                btn.innerHTML = original;
                deleteKey = null;
            });
    }

    // ---------- wire up ----------

    document.addEventListener('DOMContentLoaded', function () {
        var form = document.getElementById('record-form');
        if (form) form.addEventListener('submit', handleSave);

        var delBtn = document.getElementById('delete-confirm-btn');
        if (delBtn) delBtn.addEventListener('click', handleDelete);
    });
})();
