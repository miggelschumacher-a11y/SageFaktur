// Fortschritts-Popup: modal, mit Balken.
// - Export an Sage: echter Fortschritt (PA für PA), siehe unten.
// - Positionen nachladen: laufender Balken, wird nach der Antwort (oder dem Fehler) geschlossen.
// - Anmelden, "Neu laden": laufender Balken; der Server liefert danach eine neue Seite
//   (mit Daten oder Fehlermeldung) - damit ist das Popup automatisch wieder weg.
const WipProgress = (function () {
    const dialog = document.getElementById('progressDialog');
    const text = document.getElementById('progressText');
    const bar = dialog && dialog.querySelector('.progress-bar');
    const fill = dialog && dialog.querySelector('.progress-bar-fill');

    // Ohne done/total: laufender Balken. Mit done/total: Balken zeigt den erledigten Anteil.
    function show(message, done, total) {
        if (!dialog) return;
        text.textContent = message || 'Bitte warten …';
        const determinate = typeof total === 'number' && total > 0;
        bar.classList.toggle('determinate', determinate);
        fill.style.width = determinate ? Math.round(done / total * 100) + '%' : '';
        if (determinate) {
            bar.setAttribute('aria-valuemin', '0');
            bar.setAttribute('aria-valuemax', String(total));
            bar.setAttribute('aria-valuenow', String(done));
        } else {
            bar.removeAttribute('aria-valuenow');
        }
        dialog.hidden = false;
    }
    function hide() {
        if (dialog) dialog.hidden = true;
    }

    // Navigation innerhalb der App (Neu laden, Anmelden): die Sperren sollen dabei NICHT freigegeben werden
    document.querySelectorAll('form[data-progress]').forEach(form =>
        form.addEventListener('submit', () => { window.wipInAppNavigation = true; show(form.dataset.progress); }));

    document.querySelectorAll('a[data-progress]').forEach(link =>
        link.addEventListener('click', e => {
            // Nur bei normalem Klick - Strg/Shift/Mittelklick öffnen einen neuen Tab
            if (e.button === 0 && !e.ctrlKey && !e.shiftKey && !e.metaKey && !e.altKey) {
                window.wipInAppNavigation = true;
                show(link.dataset.progress);
            }
        }));

    // Zurück-Taste des Browsers holt die Seite aus dem Cache - dann darf kein Popup stehen bleiben
    window.addEventListener('pageshow', hide);

    return { show: show, hide: hide };
})();

// Rückfrage-Popup mit OK und Abbrechen (id="confirmDialog"): WipConfirm.ask(text) liefert ein Promise,
// das mit true (OK) oder false (Abbrechen, Escape) erfüllt wird. Es gibt immer nur eine Rückfrage zugleich.
const WipConfirm = (function () {
    const dialog = document.getElementById('confirmDialog');
    if (!dialog) return { ask: () => Promise.resolve(false) };

    const text = document.getElementById('confirmText');
    const ok = document.getElementById('confirmOk');
    const cancel = document.getElementById('confirmCancel');
    let answer = null;

    function finish(value) {
        if (!answer) return;
        const resolve = answer;
        answer = null;
        dialog.hidden = true;
        resolve(value);
    }

    ok.addEventListener('click', () => finish(true));
    cancel.addEventListener('click', () => finish(false));
    document.addEventListener('keydown', e => { if (e.key === 'Escape' && !dialog.hidden) finish(false); });
    // Zurück-Taste holt die Seite aus dem Cache - dann darf keine Rückfrage stehen bleiben
    window.addEventListener('pageshow', () => finish(false));

    return {
        ask: function (message) {
            finish(false);
            text.textContent = message;   // textContent: kein HTML aus Meldungen
            dialog.hidden = false;
            cancel.focus();   // Abbrechen ist vorbelegt: Enter löscht nicht aus Versehen eine Datei
            return new Promise(resolve => { answer = resolve; });
        }
    };
})();

// Anmelde-Popup: "Abbrechen" (oder Escape) schließt es, der Button "Anmelden" in der Leiste öffnet es wieder.
// Ohne Anmeldung bleiben "Neu laden" und "An Sage übergeben" gesperrt.
(function () {
    const dialog = document.getElementById('loginDialog');
    if (!dialog) return;

    const cancel = document.getElementById('loginCancel');
    const open = document.getElementById('loginOpen');

    function close() { dialog.hidden = true; if (open) open.focus(); }
    function show() {
        dialog.hidden = false;
        const first = dialog.querySelector('input:not([type=hidden])');
        if (first) first.focus();
    }

    if (cancel) cancel.addEventListener('click', close);
    if (open) open.addEventListener('click', show);
    document.addEventListener('keydown', e => { if (e.key === 'Escape' && !dialog.hidden) close(); });
})();

// Auswahl-Checkboxen: "Alle auswählen", Zeilen-Markierung und Zähler.
// Gezählt und per "Alle auswählen" geschaltet werden nur die Zeilen, die die Filter (PAs, Kunden) gerade anzeigen.
(function () {
    const selectAll = document.getElementById('selectAll');
    const counter = document.getElementById('selectedCount');
    const boxes = Array.from(document.querySelectorAll('input.row-select'));

    // Gesperrte PAs (Checkbox disabled) zählen nicht mit und werden von "Alle auswählen" nicht berührt.
    // Schon übergebene PAs (data-uebergeben) auch nicht: ihr Haken fragt erst nach (siehe "Neu erstellen" unten)
    function visible() { return boxes.filter(b => !b.disabled && !b.dataset.uebergeben && !b.closest('tr').hidden); }

    // Der Browser kann einen angehakten Zustand wiederherstellen (Zurück, F5) - ohne Rückfrage gilt er nicht
    boxes.forEach(b => { if (b.dataset.uebergeben) b.checked = false; });

    function refresh() {
        const shown = visible();
        const checked = shown.filter(b => b.checked).length;
        boxes.forEach(b => b.closest('tr').classList.toggle('selected', b.checked));
        if (counter) counter.textContent = checked;
        if (selectAll) {
            selectAll.checked = shown.length > 0 && checked === shown.length;
            selectAll.indeterminate = checked > 0 && checked < shown.length;
        }
    }

    if (selectAll) {
        selectAll.addEventListener('change', () => {
            visible().forEach(b => { b.checked = selectAll.checked; });
            refresh();
        });
    }
    boxes.forEach(b => b.addEventListener('change', refresh));
    document.addEventListener('wip:filter', refresh);

    refresh();
})();

// Filter neben der Überschrift: Dropdown-Listen mit Mehrfachauswahl (PAs, Kunden).
// Die Tabelle zeigt nur Zeilen, deren Wert in JEDER Liste angehakt ist. "Alle" / "Keine" schalten eine ganze Liste.
(function () {
    const rows = Array.from(document.querySelectorAll('tr.pa-row'));
    const empty = document.getElementById('filterEmpty');

    const filters = Array.from(document.querySelectorAll('.pa-filter')).map(box => ({
        box: box,
        attr: box.dataset.rowAttr,          // data-Attribut der Tabellenzeile: "pa" oder "kunde"
        name: box.dataset.name,
        toggle: box.querySelector('.pa-filter-toggle'),
        panel: box.querySelector('.pa-filter-panel'),
        label: box.querySelector('.pa-filter-label'),
        items: Array.from(box.querySelectorAll('input.pa-filter-item'))
    }));
    if (filters.length === 0) return;

    function apply() {
        filters.forEach(f => {
            f.chosen = new Set(f.items.filter(i => i.checked).map(i => i.value));
            f.label.textContent = f.chosen.size === f.items.length ? 'Alle ' + f.name + ' (' + f.items.length + ')'
                : f.chosen.size === 0 ? 'Keine ' + f.name + ' ausgewählt'
                : f.chosen.size + ' von ' + f.items.length + ' ' + f.name;
        });

        let shown = 0;
        rows.forEach(row => {
            // Zeilen ohne Wert (keine PA-Nummer / kein Kundenname) lassen sich in der jeweiligen Liste
            // nicht abwählen und werden von ihr nicht ausgeblendet
            const hide = filters.some(f => {
                const value = row.dataset[f.attr];
                return value !== '' && value !== undefined && !f.chosen.has(value);
            });
            row.hidden = hide;
            if (!hide) shown++;

            const detail = row.nextElementSibling;
            if (detail && detail.classList.contains('detail-row')) {
                const button = row.querySelector('button.row-toggle');
                detail.hidden = hide || !(button && button.getAttribute('aria-expanded') === 'true');
            }
        });
        if (empty) empty.hidden = rows.length === 0 || shown > 0;

        document.dispatchEvent(new Event('wip:filter'));   // Zähler und "Alle auswählen" nachziehen
    }

    function open(f, show) {
        f.panel.hidden = !show;
        f.toggle.setAttribute('aria-expanded', show ? 'true' : 'false');
    }

    filters.forEach(f => {
        f.toggle.addEventListener('click', () => {
            const show = f.panel.hidden;
            filters.forEach(other => open(other, false));   // immer nur eine Liste offen
            open(f, show);
        });
        f.items.forEach(i => i.addEventListener('change', apply));
        f.box.querySelector('[id$=All]').addEventListener('click', () => { f.items.forEach(i => { i.checked = true; }); apply(); });
        f.box.querySelector('[id$=None]').addEventListener('click', () => { f.items.forEach(i => { i.checked = false; }); apply(); });
    });

    // Klick außerhalb oder Escape schließt die offene Liste
    document.addEventListener('click', e => {
        filters.forEach(f => { if (!f.panel.hidden && !f.panel.contains(e.target) && !f.toggle.contains(e.target)) open(f, false); });
    });
    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape') return;
        filters.forEach(f => { if (!f.panel.hidden) { open(f, false); f.toggle.focus(); } });
    });

    apply();
})();
// Akkordeon: Pfeil in der PA-Zeile klappt die Positionen darunter auf und zu.
// Die Positionen eines PA werden beim ersten Aufklappen seiner Zeile geholt (ein Aufruf je PA)
// und danach aus dem Speicher der Seite gezeigt.
(function () {
    const table = document.querySelector('table.grid');
    const url = table && table.dataset.positionenUrl;

    function message(detail, text) {
        const panel = detail.querySelector('.detail-panel');
        panel.innerHTML = '';
        const div = document.createElement('div');
        div.className = 'detail-empty';
        div.textContent = text;
        panel.appendChild(div);
    }

    function fill(detail) {
        if (detail.dataset.loaded || detail.dataset.loading) return;
        const pa = detail.dataset.pa;
        if (!pa) {
            message(detail, 'Diese Zeile hat keine PA-Nummer - es gibt keine Positionen.');
            return;
        }

        detail.dataset.loading = '1';
        message(detail, 'Positionen werden geladen …');
        WipProgress.show('Positionen von PA ' + pa + ' werden geladen …');

        fetch(url + '?pa=' + encodeURIComponent(pa), { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(response => {
                if (response.status === 401) throw new Error('Die Sitzung ist abgelaufen. Bitte die Seite neu laden und erneut anmelden.');
                if (response.status === 403) throw new Error('Sie haben keine Berechtigung für die Fakturierung mit Sage.');
                if (!response.ok) throw new Error('Die Positionen konnten nicht geladen werden.');
                return response.text();
            })
            .then(html => {
                const doc = new DOMParser().parseFromString(html, 'text/html');
                const panel = doc.querySelector('.detail-panel[data-pa]');
                if (panel) {
                    detail.querySelector('.detail-panel').replaceWith(document.importNode(panel, true));
                } else {
                    message(detail, 'Zu diesem PA liegen keine abrechenbaren Positionen vor.');
                }
                detail.dataset.loaded = '1';
            })
            .catch(error => message(detail, error.message))   // ohne "loaded": beim nächsten Aufklappen erneut versuchen
            .then(() => {
                delete detail.dataset.loading;
                WipProgress.hide();   // Popup schließen - nach Erfolg und nach Fehler
            });
    }

    document.querySelectorAll('button.row-toggle').forEach(button => {
        const detail = document.getElementById(button.getAttribute('aria-controls'));
        if (!detail) return;

        button.addEventListener('click', () => {
            const open = button.getAttribute('aria-expanded') !== 'true';
            button.setAttribute('aria-expanded', open ? 'true' : 'false');
            detail.hidden = !open;
            button.closest('tr').classList.toggle('expanded', open);
            if (open) fill(detail);
        });
    });
})();
// Meldungs-Popup: OK-Button oder Escape schließt es.
// Kommt entweder fertig vom Server (id="messageDialog") oder wird per WipMessage.show() erzeugt.
const WipMessage = (function () {
    function wire(dialog) {
        function close() { if (dialog.parentNode) dialog.parentNode.removeChild(dialog); }
        dialog.querySelectorAll('[data-dialog-close]').forEach(b => b.addEventListener('click', close));
        document.addEventListener('keydown', e => { if (e.key === 'Escape') close(); });
    }

    function show(message) {
        const old = document.getElementById('messageDialog');
        if (old) old.parentNode.removeChild(old);

        const dialog = document.createElement('div');
        dialog.className = 'dialog-backdrop';
        dialog.id = 'messageDialog';
        dialog.innerHTML =
            '<div class="dialog" role="alertdialog" aria-modal="true" aria-labelledby="messageTitle" aria-describedby="messageText">' +
            '<h2 id="messageTitle" class="dialog-title">Hinweis</h2>' +
            '<p id="messageText" class="dialog-message"></p>' +
            '<div class="dialog-actions"><button type="button" class="btn btn-primary" data-dialog-close>OK</button></div>' +
            '</div>';
        dialog.querySelector('#messageText').textContent = message;   // textContent: kein HTML aus Meldungen
        document.body.appendChild(dialog);
        wire(dialog);
        dialog.querySelector('[data-dialog-close]').focus();
    }

    const existing = document.getElementById('messageDialog');
    if (existing) wire(existing);

    return { show: show };
})();

// Export an Sage mit echtem Fortschritt: erst vorbereiten (Ordner prüfen, Positionen laden),
// dann je PA ein Aufruf, der eine XML-Datei schreibt. Der Balken zeigt "PA x von n".
(function () {
    const form = document.getElementById('exportForm');
    if (!form || !window.fetch) return;   // ohne fetch bleibt es beim normalen Absenden des Formulars

    function post(url, fields) {
        const body = new URLSearchParams();
        body.append('__RequestVerificationToken', form.querySelector('input[name=__RequestVerificationToken]').value);
        fields.forEach(f => body.append(f[0], f[1]));
        return fetch(url, { method: 'POST', body: body, credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(response => {
                if (response.status === 401) throw new Error('Die Sitzung ist abgelaufen. Bitte die Seite neu laden und erneut anmelden.');
                if (response.status === 403) throw new Error('Sie haben keine Berechtigung für die Fakturierung mit Sage.');
                if (!response.ok) throw new Error('Der Server hat den Export abgelehnt (Status ' + response.status + ').');
                return response.json();
            })
            .then(result => {
                if (!result.ok) throw new Error(result.message);
                return result;
            });
    }

    // Ergebnis des Exports als Akkordeon über der Tabelle: Kopfzeile mit der Zusammenfassung,
    // darunter je erzeugter XML-Datei eine Zeile. Nach dem Export ist es aufgeklappt.
    function showResult(folder, files, skipped, aborted) {
        let result = document.getElementById('exportStatus');
        if (result) result.parentNode.removeChild(result);

        result = document.createElement('details');
        result.id = 'exportStatus';
        result.className = 'export-result';
        result.open = true;

        const summary = document.createElement('summary');
        summary.textContent = files.length + ' XML-Datei(en) für Sage geschrieben nach ' + folder +
            (aborted ? ' - der Export wurde danach abgebrochen' : '');
        result.appendChild(summary);

        const list = document.createElement('ul');
        files.forEach(file => {
            const item = document.createElement('li');
            item.textContent = file;
            list.appendChild(item);
        });
        skipped.forEach(pa => {
            const item = document.createElement('li');
            item.className = 'skipped';
            item.textContent = 'PA ' + pa + ': keine abrechenbaren Positionen, nicht exportiert';
            list.appendChild(item);
        });
        result.appendChild(list);

        form.parentNode.insertBefore(result, form);
    }

    // Übergebene PAs sofort als "übergeben" kennzeichnen - wie nach dem Neu laden. Der Haken bleibt anklickbar
    // (data-uebergeben): er fragt nach, ob die Datei noch einmal erstellt werden soll.
    function markExported(pa, file) {
        const now = new Date();
        const two = n => (n < 10 ? '0' : '') + n;
        const user = document.querySelector('.app-user');
        // Zwei Zeilen wie nach dem Neu laden: "übergeben am ..." und darunter "von ..."
        const zeilen = ['übergeben am ' + two(now.getDate()) + '.' + two(now.getMonth() + 1) + '.' + now.getFullYear() + ' ' + two(now.getHours()) + ':' + two(now.getMinutes())];
        if (user && user.textContent.trim()) zeilen.push('von ' + user.textContent.trim());
        document.querySelectorAll('tr.pa-row').forEach(row => {
            if (row.dataset.pa !== String(pa)) return;
            const box = row.querySelector('input.row-select');
            box.checked = false;
            box.disabled = false;
            box.dataset.uebergeben = '1';
            box.title = 'Haken setzen, um die XML-Datei noch einmal zu erstellen';
            row.classList.add('is-exported');
            const cell = row.querySelector('.col-status');
            cell.innerHTML = '<span class="status status-exported"><svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" d="M4.5 12.5l5 5L19.5 7" /></svg></span>';
            const badge = cell.firstChild;
            badge.title = file;
            const textBox = document.createElement('span');
            textBox.className = 'status-text';
            zeilen.forEach(z => {
                const zeile = document.createElement('span');
                zeile.textContent = z;
                textBox.appendChild(zeile);
            });
            badge.appendChild(textBox);
        });
        document.dispatchEvent(new Event('wip:filter'));   // Zähler "ausgewählt" nachziehen
        const submit = form.querySelector('.actions button[type=submit]');
        if (submit && !form.querySelector('input.row-select:not(:disabled):not([data-uebergeben])')) submit.disabled = true;
    }

    async function run(pas) {
        const files = [], skipped = [];
        let folder = '';
        try {
            WipProgress.show('Export wird vorbereitet …');
            const start = await post(form.dataset.exportStartUrl, pas.map(pa => ['pas', pa]));
            folder = start.folder;

            for (let i = 0; i < start.pas.length; i++) {
                const pa = start.pas[i];
                WipProgress.show('PA ' + pa + ' wird an Sage übergeben (' + (i + 1) + ' von ' + start.pas.length + ') …', i, start.pas.length);
                const step = await post(form.dataset.exportStepUrl, [['exportId', start.exportId], ['pa', pa]]);
                if (step.file) { files.push(step.file); markExported(pa, step.file); } else skipped.push(pa);
            }
            WipProgress.show('Export abgeschlossen.', start.pas.length, start.pas.length);
        } catch (error) {
            WipProgress.hide();
            const done = files.length ? '\n\nBereits geschrieben: ' + files.join(', ') : '';
            WipMessage.show(error.message + done);
            if (files.length) showResult(folder, files, skipped, true);
            return;
        }

        WipProgress.hide();
        showResult(folder, files, skipped, false);
    }

    form.addEventListener('submit', e => {
        e.preventDefault();
        // Nur angehakte Zeilen, die die Filter (PAs, Kunden) gerade anzeigen
        const pas = Array.from(form.querySelectorAll('input.row-select:checked'))
            .filter(box => !box.closest('tr').hidden && !box.dataset.uebergeben)
            .map(box => box.closest('td').querySelector('input[type=hidden][name$=".PA"]'))
            .filter(input => input && input.value !== '')
            .map(input => input.value);
        if (pas.length === 0) {
            WipMessage.show('Es wurde kein PA ausgewählt.');
            return;
        }
        run(pas);
    });
})();

// Neu erstellen: Bei einem schon übergebenen PA, den kein anderer Benutzer sperrt, bleibt der Haken aktiv
// (data-uebergeben). Beim Anhaken fragt ein Popup mit OK und Abbrechen, ob die Datei noch einmal erstellt werden soll.
// - Abbrechen: der Haken wird wieder entfernt, es passiert nichts.
// - OK: der Server setzt SageHDR.Datei und DateiErstelltAm auf NULL, löscht die alte XML-Datei aus dem Export-Ordner
//   (falls sie noch dort liegt) und sperrt den Satz für diesen Benutzer. Die Zeile ist danach ein ganz normaler,
//   angehakter PA und wird mit "An Sage übergeben" neu erzeugt.
(function () {
    const form = document.getElementById('exportForm');
    const url = form && form.dataset.neuErstellenUrl;
    if (!url || !window.fetch) return;

    function post(pa) {
        const body = new URLSearchParams();
        body.append('__RequestVerificationToken', form.querySelector('input[name=__RequestVerificationToken]').value);
        body.append('pa', pa);
        return fetch(url, { method: 'POST', body: body, credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(response => {
                if (response.status === 401) throw new Error('Die Sitzung ist abgelaufen. Bitte die Seite neu laden und erneut anmelden.');
                if (response.status === 403) throw new Error('Sie haben keine Berechtigung für die Fakturierung mit Sage.');
                if (!response.ok) throw new Error('Der Server hat die Anfrage abgelehnt (Status ' + response.status + ').');
                return response.json();
            })
            .then(result => {
                if (!result.ok) throw new Error(result.message);
                return result;
            });
    }

    // Aus der übergebenen Zeile eine normale, angehakte Zeile machen (wie nach dem Neu laden mit eigener Sperre)
    function release(row, box) {
        delete box.dataset.uebergeben;
        box.removeAttribute('title');
        box.checked = true;
        row.classList.remove('is-exported');
        row.querySelector('.col-status').innerHTML = '';
        const submit = form.querySelector('.actions button[type=submit]');
        if (submit) submit.disabled = false;
        document.dispatchEvent(new Event('wip:filter'));   // Zähler "ausgewählt" nachziehen
    }

    form.querySelectorAll('input.row-select').forEach(box => {
        box.addEventListener('change', async () => {
            if (!box.dataset.uebergeben || !box.checked) return;

            const row = box.closest('tr');
            const pa = row.dataset.pa;
            const status = row.querySelector('.status-exported');
            const datei = status && status.title ? '\n\nDatei: ' + status.title : '';
            const wann = status ? status.textContent.replace(/\s+/g, ' ').trim() : 'übergeben';

            box.disabled = true;   // keine zweite Rückfrage, während die erste läuft
            const confirmed = await WipConfirm.ask(
                'Für PA ' + pa + ' wurde bereits eine XML-Datei erstellt (' + wann + ').' + datei +
                '\n\nSoll die Datei noch einmal erstellt werden?\nMit OK wird die alte Datei im Export-Ordner gelöscht ' +
                '(falls sie noch dort liegt) und der PA für Sie gesperrt.');
            box.disabled = false;

            if (!confirmed) {
                box.checked = false;
                document.dispatchEvent(new Event('wip:filter'));
                return;
            }

            try {
                box.disabled = true;
                WipProgress.show('PA ' + pa + ' wird zum erneuten Erstellen freigegeben …');
                await post(pa);
                release(row, box);
            } catch (error) {
                box.checked = false;
                WipMessage.show(error.message);
            } finally {
                box.disabled = false;
                WipProgress.hide();
            }
        });
    });
})();

// Sperren der PAs in SageHDR: solange die Seite offen ist, jede Minute verlängern.
// Beim Schließen des Browsers oder Tabs (und beim Wechsel zur Hauptseite) werden sie aus SageHDR gelöscht;
// beim Neu laden innerhalb der App nicht. Kommt die Freigabe nicht an (Absturz), laufen sie nach
// 3 Minuten ab (SageSperre.Minuten) und werden beim nächsten Laden bzw. Verlängern gelöscht.
(function () {
    const table = document.querySelector('table.grid');
    const verlaengernUrl = table && table.dataset.sperreVerlaengernUrl;
    const freigebenUrl = table && table.dataset.sperreFreigebenUrl;
    const tokenInput = document.querySelector('#exportForm input[name=__RequestVerificationToken]');
    if (!verlaengernUrl || !freigebenUrl || !tokenInput) return;   // nicht angemeldet oder ohne Berechtigung

    function formData() {
        const data = new FormData();
        data.append('__RequestVerificationToken', tokenInput.value);
        return data;
    }

    setInterval(() => {
        fetch(verlaengernUrl, { method: 'POST', body: formData(), credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .catch(() => { /* nächster Versuch in 5 Minuten */ });
    }, 60 * 1000);

    // Abmelden über das Power-Symbol gibt die Sperren auf dem Server frei - kein zweiter Aufruf nötig
    document.querySelectorAll('form.app-logout').forEach(f => f.addEventListener('submit', () => { window.wipInAppNavigation = true; }));

    // Freigabe genau einmal schicken - je nach Browser kommt beim Schließen pagehide, beforeunload oder beides
    let freigegeben = false;
    function freigeben() {
        if (window.wipInAppNavigation || freigegeben) return;
        freigegeben = true;
        if (navigator.sendBeacon && navigator.sendBeacon(freigebenUrl, formData())) return;
        // Ohne sendBeacon: Anfrage, die das Schließen der Seite überlebt
        fetch(freigebenUrl, { method: 'POST', body: formData(), credentials: 'same-origin', keepalive: true }).catch(() => { });
    }
    window.addEventListener('pagehide', freigeben);
    window.addEventListener('beforeunload', freigeben);
    // Zurück-Taste: Seite kommt aus dem Cache, die Sperren wurden beim Verlassen freigegeben -> neu laden
    window.addEventListener('pageshow', e => { if (e.persisted) window.location.reload(); else freigegeben = false; });
})();