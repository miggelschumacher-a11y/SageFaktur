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
            cancel.focus();   // Abbrechen ist vorbelegt: Enter übergibt nicht aus Versehen erneut an Sage
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
// Gezählt und per "Alle auswählen" geschaltet werden nur die Zeilen, die die Filter (PAs, Kunden, Status) gerade anzeigen.
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

// Filter neben der Überschrift: Dropdown-Listen mit Mehrfachauswahl (PAs, Kunden, Status).
// Die Tabelle zeigt nur Zeilen, deren Wert in JEDER Liste angehakt ist. "Alle" / "Keine" schalten eine ganze Liste.
(function () {
    const rows = Array.from(document.querySelectorAll('tr.pa-row'));
    const empty = document.getElementById('filterEmpty');

    const filters = Array.from(document.querySelectorAll('.pa-filter')).map(box => ({
        box: box,
        attr: box.dataset.rowAttr,          // data-Attribut der Tabellenzeile: "pa", "kunde" oder "status"
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
        // "Alle"/"Keine" gibt es nicht bei jeder Liste (beim Status nicht)
        const alle = f.box.querySelector('[id$=All]'), keine = f.box.querySelector('[id$=None]');
        if (alle) alle.addEventListener('click', () => { f.items.forEach(i => { i.checked = true; }); apply(); });
        if (keine) keine.addEventListener('click', () => { f.items.forEach(i => { i.checked = false; }); apply(); });
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

    // still = true: ohne Fortschritts-Popup (Nachladen während des Exports, siehe wip:positionen-veraltet)
    function fill(detail, still) {
        if (detail.dataset.loaded || detail.dataset.loading) return;
        const pa = detail.dataset.pa;
        if (!pa) {
            message(detail, 'Diese Zeile hat keine PA-Nummer - es gibt keine Positionen.');
            return;
        }

        detail.dataset.loading = '1';
        message(detail, 'Positionen werden geladen …');
        if (!still) WipProgress.show('Positionen von PA ' + pa + ' werden geladen …');

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
                if (!still) WipProgress.hide();   // Popup schließen - nach Erfolg und nach Fehler
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

    // Nach der Übergabe eines PA an Sage sind seine gespeicherten Positionen veraltet (Status, Meldung und
    // OfficeLineHandle aus SagePos): beim nächsten Aufklappen neu laden - ist die Zeile gerade offen, sofort.
    document.addEventListener('wip:positionen-veraltet', e => {
        document.querySelectorAll('tr.detail-row').forEach(detail => {
            if (detail.dataset.pa !== String(e.detail)) return;
            delete detail.dataset.loaded;
            if (!detail.hidden) fill(detail, true);
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

    // wide = true: breiteres Popup (Ergebnis des Exports mit langer Liste)
    function show(message, wide) {
        const old = document.getElementById('messageDialog');
        if (old) old.parentNode.removeChild(old);

        const dialog = document.createElement('div');
        dialog.className = 'dialog-backdrop';
        dialog.id = 'messageDialog';
        dialog.innerHTML =
            '<div class="dialog' + (wide ? ' dialog-wide' : '') + '" role="alertdialog" aria-modal="true" aria-labelledby="messageTitle" aria-describedby="messageText">' +
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

// Export an Sage mit echtem Fortschritt: erst vorbereiten (Adresse der Sage-Schnittstelle prüfen),
// dann je PA ein Aufruf, der den Auftrag an Sage sendet. Der Balken zeigt "PA x von n".
// Am Ende fasst ein Meldungs-Popup zusammen, was übergeben, abgelehnt oder ohne Antwort geblieben ist.
(function () {
    const form = document.getElementById('exportForm');
    if (!form || !window.fetch) return;   // ohne fetch bleibt es beim normalen Absenden des Formulars

    function post(url, fields) {
        const body = new URLSearchParams();
        body.append('__RequestVerificationToken', form.querySelector('input[name=__RequestVerificationToken]').value);
        body.append('Seite', form.querySelector('input[name=Seite]').value);   // Kennung dieser Seite für die Sperren
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

    // Zusammenfassung für das Meldungs-Popup (gleicher Text wie WipPaController.Zusammenfassung).
    // Gleiche Meldungen stehen nur einmal, dahinter die PAs - bei Hunderten PAs bleibt die Liste lesbar.
    function summary(steps) {
        const uebergeben = steps.filter(s => s.ergebnis === 'Uebergeben');
        const zeilen = [uebergeben.length + ' PA(s) an Sage übergeben.'];
        // Je übergebenem PA die OfficeLineHandle des Auftrags und die OfficeLineVorgangsHandle aus der Antwort von Sage
        uebergeben.forEach(s => zeilen.push('PA ' + s.pa + ': OfficeLineHandle ' + (s.officeLineHandle || '-') +
            ', Vorgang ' + (s.officeLineVorgangsHandle || '-') + (s.meldung ? ' (' + s.meldung + ')' : '')));
        function block(titel, ergebnis) {
            const liste = steps.filter(s => s.ergebnis === ergebnis);
            if (liste.length === 0) return;
            const gruppen = new Map();
            liste.forEach(s => {
                // Zeilenumbrüche zusammenziehen, führende Satzzeichen weg (Sage liefert z. B. ", Fehler in ...")
                const text = (s.meldung || '').replace(/\s+/g, ' ').replace(/^[\s,;:\-]+/, '').trim();
                if (!gruppen.has(text)) gruppen.set(text, []);
                gruppen.get(text).push(s.pa);
            });
            zeilen.push('', titel + ' (' + liste.length + '):');
            // Je Meldung: "• Meldung" und darunter die PAs
            gruppen.forEach((pas, text) => { if (text) zeilen.push('• ' + text); zeilen.push('PA ' + pas.join(', ')); });
        }
        block('Von Sage abgelehnt', 'Abgelehnt');
        block('Nicht übergeben', 'NichtUebergeben');
        block('Keine Antwort von Sage', 'KeineAntwort');
        block('Keine abrechenbaren Positionen, nicht übergeben', 'KeinePositionen');
        return zeilen.join('\n');
    }

    function rowsOf(pa) {
        return Array.from(document.querySelectorAll('tr.pa-row')).filter(row => row.dataset.pa === String(pa));
    }

    // Zeile unter dem Status-Abzeichen: Status und Meldung von Sage (wie nach dem Neu laden)
    function setRueckmeldung(cell, text, fehler) {
        let line = cell.querySelector('.sage-rueckmeldung');
        if (!line) {
            line = document.createElement('div');
            cell.appendChild(line);
        }
        line.className = 'sage-rueckmeldung' + (fehler ? ' sage-fehler' : '');
        line.title = 'Status und Meldung von Sage (SageHDR)';
        line.textContent = text;
    }

    // Übergebene PAs sofort als "übergeben" kennzeichnen - wie nach dem Neu laden. Der Haken bleibt anklickbar
    // (data-uebergeben): er fragt nach, ob der PA noch einmal an Sage übergeben werden soll.
    // rueckmeldung: Zeile wie nach dem Neu laden ("Alles ok in Sage" bzw. "XML erzeugt: <Hinweis>"), handle: OfficeLineHandle
    function markExported(pa, handle, vorgang, rueckmeldung, fehler) {
        const now = new Date();
        const two = n => (n < 10 ? '0' : '') + n;
        const user = document.querySelector('.app-user');
        // Zwei Zeilen wie nach dem Neu laden: "übergeben am ..." und darunter "von ..."
        const zeilen = ['übergeben am ' + two(now.getDate()) + '.' + two(now.getMonth() + 1) + '.' + now.getFullYear() + ' ' + two(now.getHours()) + ':' + two(now.getMinutes())];
        if (user && user.textContent.trim()) zeilen.push('von ' + user.textContent.trim());
        rowsOf(pa).forEach(row => {
            const box = row.querySelector('input.row-select');
            box.checked = false;
            box.disabled = false;
            box.dataset.uebergeben = '1';
            box.title = 'Haken setzen, um den PA noch einmal an Sage zu übergeben';
            row.classList.remove('selected');
            row.classList.add('is-exported');
            const cell = row.querySelector('.col-status');
            cell.innerHTML = '<span class="status status-exported"><svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" d="M4.5 12.5l5 5L19.5 7" /></svg></span>';
            const badge = cell.firstChild;

            const textBox = document.createElement('span');
            textBox.className = 'status-text';
            zeilen.forEach(z => {
                const zeile = document.createElement('span');
                zeile.textContent = z;
                textBox.appendChild(zeile);
            });
            badge.appendChild(textBox);
            if (rueckmeldung) setRueckmeldung(cell, rueckmeldung, fehler);
            document.dispatchEvent(new CustomEvent('wip:positionen-veraltet', { detail: pa }));
            if (handle) {
                const line = document.createElement('div');
                line.className = 'sage-handle';
                line.title = 'OfficeLineHandle des Auftrags / OfficeLineVorgangsHandle des Vorgangs in Sage (SageHDR)';
                line.textContent = 'Sage-Handle ' + handle + (vorgang ? ' / Vorgang ' + vorgang : '');
                cell.appendChild(line);
            }
        });
        document.dispatchEvent(new Event('wip:filter'));   // Zähler "ausgewählt" nachziehen
        const submit = form.querySelector('.actions button[type=submit]');
        // data-uebergeben über dataset prüfen: Razor schreibt bei nicht übergebenen Zeilen ein leeres Attribut
        // (data-uebergeben=""), ein CSS-Selektor [data-uebergeben] würde also jede Zeile treffen
        const nochFrei = Array.from(form.querySelectorAll('input.row-select:not(:disabled)')).some(b => !b.dataset.uebergeben);
        if (submit && !nochFrei) submit.disabled = true;
    }

    // Von Sage abgelehnt oder übersprungen: der PA bleibt übergebbar, wird aber abgehakt und zeigt die Meldung
    function markRejected(pa, text) {
        rowsOf(pa).forEach(row => {
            row.querySelector('input.row-select').checked = false;
            row.classList.remove('selected');
            setRueckmeldung(row.querySelector('.col-status'), text, true);
            document.dispatchEvent(new CustomEvent('wip:positionen-veraltet', { detail: pa }));
        });
        document.dispatchEvent(new Event('wip:filter'));
    }

    async function run(pas) {
        const steps = [];
        try {
            WipProgress.show('Export wird vorbereitet …');
            const start = await post(form.dataset.exportStartUrl, pas.map(pa => ['pas', pa]));

            for (let i = 0; i < start.pas.length; i++) {
                const pa = start.pas[i];
                WipProgress.show('PA ' + pa + ' wird an Sage übergeben (' + (i + 1) + ' von ' + start.pas.length + ') …', i, start.pas.length);
                const step = await post(form.dataset.exportStepUrl, [['exportId', start.exportId], ['pa', pa]]);
                step.pa = pa;
                steps.push(step);
                // Status für den Status-Filter nachziehen (2 = Alles ok, 3 = Fehler in Sage, 1 = XML erzeugt: nicht filterbar).
                // Ausgeblendet wird erst bei der nächsten Änderung eines Filters - die Zeile springt nicht während des Exports weg.
                const status = { Uebergeben: '2', Abgelehnt: '3', KeineAntwort: '' }[step.ergebnis];
                if (status !== undefined) rowsOf(pa).forEach(row => { row.dataset.status = status; });
                if (step.ergebnis === 'Uebergeben') markExported(pa, step.officeLineHandle, step.officeLineVorgangsHandle, 'Alles ok in Sage' + (step.meldung ? ': ' + step.meldung : ''), false);
                else if (step.ergebnis === 'KeineAntwort') markExported(pa, null, null, 'XML erzeugt: ' + step.meldung, true);
                else if (step.ergebnis === 'Abgelehnt') markRejected(pa, 'Fehler in Sage: ' + step.meldung);
                else if (step.ergebnis === 'NichtUebergeben') markRejected(pa, 'Nicht übergeben: ' + step.meldung);
            }
            WipProgress.show('Export abgeschlossen.', start.pas.length, start.pas.length);
        } catch (error) {
            WipProgress.hide();
            WipMessage.show(error.message + (steps.length ? '\n\nBis dahin:\n' + summary(steps) : ''), steps.length > 0);
            return;
        }

        WipProgress.hide();
        WipMessage.show(summary(steps), true);
    }

    form.addEventListener('submit', e => {
        e.preventDefault();
        // Nur angehakte Zeilen, die die Filter (PAs, Kunden, Status) gerade anzeigen
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
// (data-uebergeben). Beim Anhaken fragt ein Popup mit OK und Abbrechen, ob der PA noch einmal an Sage übergeben werden soll.
// - Abbrechen: der Haken wird wieder entfernt, es passiert nichts.
// - OK: der Server setzt SageHDR.Status auf "neu" und sperrt den Satz für diesen Benutzer.
//   Die Zeile ist danach ein ganz normaler, angehakter PA und wird mit "An Sage übergeben" erneut gesendet.
(function () {
    const form = document.getElementById('exportForm');
    const url = form && form.dataset.neuErstellenUrl;
    if (!url || !window.fetch) return;

    function post(pa) {
        const body = new URLSearchParams();
        body.append('__RequestVerificationToken', form.querySelector('input[name=__RequestVerificationToken]').value);
        body.append('Seite', form.querySelector('input[name=Seite]').value);   // Kennung dieser Seite für die Sperren
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
        row.dataset.status = '0';   // wieder "neu" (Status-Filter)
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

            const wann = status ? status.textContent.replace(/\s+/g, ' ').trim() : 'übergeben';

            box.disabled = true;   // keine zweite Rückfrage, während die erste läuft
            const confirmed = await WipConfirm.ask(
                'PA ' + pa + ' wurde bereits an Sage übergeben (' + wann + ').' +
                '\n\nSoll der PA noch einmal an Sage übergeben werden?\nBitte vorher in Sage prüfen, dass der Auftrag dort nicht ' +
                'schon angelegt ist. Mit OK wird der PA für Sie gesperrt und kann erneut übergeben werden.');
            box.disabled = false;

            if (!confirmed) {
                box.checked = false;
                document.dispatchEvent(new Event('wip:filter'));
                return;
            }

            try {
                box.disabled = true;
                WipProgress.show('PA ' + pa + ' wird zur erneuten Übergabe freigegeben …');
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
// Beim Verlassen der Seite (Schließen, F5, Wechsel zur Hauptseite) werden die Sperren dieser Seite aus SageHDR
// gelöscht - nur ihre eigenen (Kennung "Seite"): eine schon neu geladene Seite hat ihre PAs mit einer neuen
// Kennung gesperrt und behält sie, auch wenn die Freigabe erst danach ankommt. Kommt die Freigabe nicht an (Absturz), laufen sie nach
// 3 Minuten ab (SageSperre.Minuten) und werden beim nächsten Laden bzw. Verlängern gelöscht.
(function () {
    const table = document.querySelector('table.grid');
    const verlaengernUrl = table && table.dataset.sperreVerlaengernUrl;
    const freigebenUrl = table && table.dataset.sperreFreigebenUrl;
    const tokenInput = document.querySelector('#exportForm input[name=__RequestVerificationToken]');
    const seiteInput = document.querySelector('#exportForm input[name=Seite]');
    if (!verlaengernUrl || !freigebenUrl || !tokenInput) return;   // nicht angemeldet oder ohne Berechtigung

    function formData() {
        const data = new FormData();
        data.append('__RequestVerificationToken', tokenInput.value);
        // Kennung dieser Seite: die Freigabe löscht nur ihre Sperren, nicht die einer schon neu geladenen Seite (F5)
        if (seiteInput) data.append('Seite', seiteInput.value);
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