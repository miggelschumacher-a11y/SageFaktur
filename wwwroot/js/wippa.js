// Fortschritts-Popup: modal, mit Balken.
// - Export an Sage: echter Fortschritt (PA für PA), siehe unten.
// - Positionen nachladen: laufender Balken, wird nach der Antwort (oder dem Fehler) geschlossen.
// - Anmelden, "Neu laden": laufender Balken; der Server liefert danach eine neue Seite
//   (mit Daten oder Fehlermeldung) - damit ist das Popup automatisch wieder weg.
const WipProgress = (function () {
    const dialog = document.getElementById('progressDialog');
    const text = document.getElementById('progressText');
    const actions = document.getElementById('progressActions');
    const cancelButton = document.getElementById('progressCancel');
    let onCancel = null;
    if (cancelButton) cancelButton.addEventListener('click', () => { if (onCancel) onCancel(); });
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
        cancelable(null);
    }

    // Knopf "Abbrechen" im Popup: mit Funktion einblenden (nur beim Export an Sage), mit null ausblenden.
    // disabled = true: Knopf sichtbar, aber gesperrt (Abbruch ist schon bestätigt)
    function cancelable(handler, disabled) {
        onCancel = handler;
        if (actions) actions.hidden = !handler;
        if (cancelButton) cancelButton.disabled = !!disabled;
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

    return { show: show, hide: hide, cancelable: cancelable };
})();

// Rückfrage-Popup mit OK und Abbrechen (id="confirmDialog"): WipConfirm.ask(text, optionen) liefert ein Promise,
// das mit true (OK) oder false (Abbrechen, Escape) erfüllt wird. Es gibt immer nur eine Rückfrage zugleich.
// optionen (alle wahlfrei): titel, ok, abbrechen - sonst gelten die Texte aus der View.
const WipConfirm = (function () {
    const dialog = document.getElementById('confirmDialog');
    if (!dialog) return { ask: () => Promise.resolve(false) };

    const text = document.getElementById('confirmText');
    const ok = document.getElementById('confirmOk');
    const cancel = document.getElementById('confirmCancel');
    const title = document.getElementById('confirmTitle');
    // Texte aus der View als Vorgabe merken - jede Rückfrage kann sie für sich ändern
    const vorgabe = { titel: title.textContent, ok: ok.textContent, abbrechen: cancel.textContent };
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
        ask: function (message, optionen) {
            finish(false);
            const o = optionen || {};
            title.textContent = o.titel || vorgabe.titel;
            ok.textContent = o.ok || vorgabe.ok;
            cancel.textContent = o.abbrechen || vorgabe.abbrechen;
            text.textContent = message;   // textContent: kein HTML aus Meldungen
            dialog.hidden = false;
            cancel.focus();   // Abbrechen ist vorbelegt: Enter übergibt nicht aus Versehen erneut an Sage
            return new Promise(resolve => { answer = resolve; });
        },
        // Offene Rückfrage schließen, als hätte der Benutzer "Abbrechen" gewählt
        close: function () { finish(false); }
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

// Filter neben der Überschrift: Dropdown-Listen mit Mehrfachauswahl (PAs, Kunden-Nr., Kunden, Status).
// Die Tabelle zeigt nur Zeilen, die zu JEDER Liste passen. "Alle" setzt eine Liste zurück.
// PAs, Kunden-Nr. und Kunden sind bearbeitbar: der Text im Feld sucht in den Einträgen der Liste (enthält,
// ohne Groß-/Kleinschreibung). Bei ihnen nimmt ein Haken den Eintrag dazu (nichts angehakt = alle Zeilen),
// und "Alle" setzt die Auswahl zurück.
// Dazu kommen die Suchfilter in der zweiten Kopfzeile der Tabelle (Kunden-Nr., Kunde, Sperrflag) - auch sie
// müssen passen, damit eine Zeile angezeigt wird.
(function () {
    const rows = Array.from(document.querySelectorAll('tr.pa-row'));
    const empty = document.getElementById('filterEmpty');

    const filters = Array.from(document.querySelectorAll('.pa-filter')).map(box => ({
        box: box,
        attr: box.dataset.rowAttr,          // data-Attribut der Tabellenzeile: "pa", "kundenr", "kunde" oder "status"
        name: box.dataset.name,
        toggle: box.querySelector('.pa-filter-toggle'),
        search: box.querySelector('.pa-filter-search'),   // nur bei bearbeitbaren Listen
        panel: box.querySelector('.pa-filter-panel'),
        label: box.querySelector('.pa-filter-label'),
        leer: box.querySelector('.pa-filter-leer'),
        weitere: box.querySelector('.pa-filter-weitere'),
        // Zusatz (Kunden-Nr.): Haken nehmen Einträge dazu - nichts angehakt = alle Zeilen, sonst nur die angehakten
        zusatz: !!box.dataset.zusatz,
        items: Array.from(box.querySelectorAll('input.pa-filter-item'))
    }));

    // Suchfilter der zweiten Kopfzeile: PA, Kunden-Nr., Kunde - die Zeile passt, wenn ihr Wert die Eingabe enthält.
    // Sperrflag und Rechnungszeit sind Dropdowns mit Mehrfachauswahl (.col-combo): Pfeil oder Klick ins Feld öffnet die
    // Liste mit allen Werten, Tippen sucht darin. Nichts angehakt = alle Zeilen, sonst nur die angehakten Werte.
    const columns = Array.from(document.querySelectorAll('input.col-filter')).map(input => {
        const combo = input.closest('.col-combo');
        const options = combo ? Array.from(combo.querySelectorAll('.col-combo-option')) : [];
        return {
            input: input,
            attr: input.dataset.colAttr,
            match: input.dataset.match,
            combo: combo,
            panel: combo ? combo.querySelector('.col-combo-panel') : null,
            toggle: combo ? combo.querySelector('.col-combo-toggle') : null,
            leer: combo ? combo.querySelector('.col-combo-leer') : null,
            options: options,
            items: combo ? Array.from(combo.querySelectorAll('input.col-combo-item')) : [],
            alle: combo ? combo.querySelector('.col-combo-alle') : null
        };
    });
    if (filters.length === 0 && columns.length === 0) return;

    const wrap = document.querySelector('.table-wrap');
    let eigenesScrollen = false;   // true: das nächste scroll-Ereignis stammt von apply()

    // Anfangszustand beim Laden erzwingen: Firefox stellt beim Neu laden Häkchen und Feldinhalte wieder her, ohne dass
    // ein change-Ereignis kommt - die Filter würden dann mit alten, unsichtbaren Haken arbeiten (z. B. "alle PAs
    // angehakt" aus einer früheren Version). Deshalb: Zusatz-Listen ohne Haken, sonst alle angehakt, Suchfelder leer.
    filters.forEach(f => {
        f.items.forEach(i => { i.checked = !f.zusatz; });
        if (f.search) f.search.value = '';
    });
    columns.forEach(c => {
        c.input.value = '';
        c.items.forEach(i => { i.checked = false; });
    });

    // Nur Ziffern zulassen (PAs, Kunden-Nr.) - auch beim Einfügen aus der Zwischenablage
    function nurZiffern(input) {
        if (input.dataset.ziffern && /\D/.test(input.value)) input.value = input.value.replace(/\D/g, '');
    }

    function passt(column, row) {
        const value = (row.dataset[column.attr] || '').trim();
        if (column.combo) {
            // Mehrfachauswahl: nichts angehakt = alle; "(leer)" steht für Zeilen ohne Wert
            if (column.chosen.size === 0) return true;
            return column.chosen.has(value === '' ? '(leer)' : value);
        }
        const query = column.input.value.trim();
        return query === '' || value.toLowerCase().indexOf(query.toLowerCase()) >= 0;
    }

    function apply() {
        filters.forEach(f => {
            f.chosen = new Set(f.items.filter(i => i.checked).map(i => i.value));
            const text = f.chosen.size === f.items.length || (f.zusatz && f.chosen.size === 0) ? 'Alle ' + f.name + ' (' + f.items.length + ')'
                : f.chosen.size === 0 ? 'Keine ' + f.name + ' ausgewählt'
                : f.chosen.size + ' von ' + f.items.length + ' ' + f.name;
            // Bearbeitbare Liste: Auswahl als Platzhalter im Suchfeld (sichtbar, solange nichts eingetippt ist)
            if (f.search) f.search.placeholder = text; else if (f.label) f.label.textContent = text;
            weitereAnzeigen(f);
        });
        columns.filter(c => c.combo).forEach(c => {
            const werte = c.items.filter(i => i.checked).map(i => i.value);
            c.chosen = new Set(werte);
            // Auswahl als Platzhalter im Feld: "Alle", bis zu drei Werte oder "n ausgewählt"
            c.input.placeholder = werte.length === 0 ? 'Alle' : werte.length <= 3 ? werte.join(', ') : werte.length + ' ausgewählt';
            if (c.alle) c.alle.classList.toggle('aktiv', werte.length === 0);
        });

        let shown = 0;
        rows.forEach(row => {
            // Zeilen ohne Wert (keine PA-Nummer / kein Kundenname) lassen sich in der jeweiligen Liste
            // nicht abwählen und werden von ihr nicht ausgeblendet
            const hide = filters.some(f => {
                if (f.zusatz && f.chosen.size === 0) return false;   // nichts angehakt = alle
                const value = row.dataset[f.attr];
                return value !== '' && value !== undefined && !f.chosen.has(value);
            }) || columns.some(c => !passt(c, row));
            row.hidden = hide;
            if (!hide) shown++;

            const detail = row.nextElementSibling;
            if (detail && detail.classList.contains('detail-row')) {
                const button = row.querySelector('button.row-toggle');
                detail.hidden = hide || !(button && button.getAttribute('aria-expanded') === 'true');
            }
        });
        if (empty) empty.hidden = rows.length === 0 || shown > 0;

        // Nach jeder Filteränderung an den Anfang der Tabelle: war sie heruntergescrollt, lägen die ersten Treffer
        // sonst verdeckt oberhalb des sichtbaren Bereichs (die Kopfzeilen bleiben oben stehen)
        if (wrap && wrap.scrollTop > 0) {
            eigenesScrollen = true;
            wrap.scrollTop = 0;
        }

        document.dispatchEvent(new Event('wip:filter'));   // Zähler und "Alle auswählen" nachziehen
    }

    // Einträge der Liste nach dem Suchtext ein- und ausblenden
    function suchen(f) {
        const query = f.search ? f.search.value.trim().toLowerCase() : '';
        let treffer = 0;
        f.items.forEach(i => {
            const option = i.closest('.pa-filter-option');
            const zeigen = query === '' || option.textContent.trim().toLowerCase().indexOf(query) >= 0;
            option.hidden = !zeigen;
            if (zeigen) treffer++;
        });
        if (f.leer) f.leer.hidden = treffer > 0;
        weitereAnzeigen(f);
    }

    // Zusatz-Listen: angehakte Einträge, die die Suche gerade ausblendet, als Hinweis in der Liste nennen -
    // sonst wirkt der Filter, als zeige er Zeilen, die gar nicht angehakt sind
    function weitereAnzeigen(f) {
        if (!f.weitere) return;
        const versteckt = f.zusatz ? f.items.filter(i => i.checked && i.closest('.pa-filter-option').hidden).length : 0;
        f.weitere.hidden = versteckt === 0;
        f.weitere.textContent = 'Außerdem angehakt: ' + versteckt + ' (nicht in der Suche)';
    }

    // Einträge, die die Suche gerade zeigt (für Listen ohne Zusatz: "Alle" hakt nur die gefundenen an)
    function sichtbareItems(f) {
        return f.items.filter(i => !i.closest('.pa-filter-option').hidden);
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
            if (show && f.search) f.search.focus();
        });
        if (f.search) {
            // Tippen im Feld öffnet die Liste und sucht darin
            f.search.addEventListener('focus', () => {
                if (f.ohneOeffnen) { f.ohneOeffnen = false; return; }   // Escape: Fokus zurück, Liste bleibt zu
                filters.forEach(other => { if (other !== f) open(other, false); });
                open(f, true);
            });
            f.search.addEventListener('input', () => {
                nurZiffern(f.search);
                suchen(f);
                filters.forEach(other => { if (other !== f) open(other, false); });   // immer nur eine Liste offen
                open(f, true);
            });
        }
        f.items.forEach(i => i.addEventListener('change', apply));
        // "Alle" gibt es nicht bei jeder Liste (beim Status nicht)
        const alle = f.box.querySelector('[id$=All]');
        if (alle) alle.addEventListener('click', () => {
            if (f.zusatz) {
                // Zusatz-Listen: Auswahl zurücksetzen - keine Haken, Suche leer, die Tabelle zeigt alle Zeilen
                f.items.forEach(i => { i.checked = false; });
                if (f.search) f.search.value = '';
                suchen(f);
            } else {
                sichtbareItems(f).forEach(i => { i.checked = true; });
            }
            apply();
        });
    });

    // Dropdown einer Spalte (Sperrflag, Rechnungszeit). Die Liste liegt fest über der Seite (position: fixed) - im scrollenden
    // Tabellenbereich würde sie sonst bei wenigen Zeilen abgeschnitten.
    function comboOpen(c, show) {
        if (!c.panel) return;
        if (show) {
            const rect = c.combo.getBoundingClientRect();
            c.panel.style.top = (rect.bottom + 2) + 'px';
            c.panel.style.left = rect.left + 'px';
            c.panel.style.minWidth = rect.width + 'px';
        } else if (!c.panel.hidden) {
            // Beim Schließen den Suchtext leeren - das Feld zeigt dann wieder die Auswahl
            c.input.value = '';
            comboSuchen(c, '');
        }
        c.panel.hidden = !show;
        c.toggle.setAttribute('aria-expanded', show ? 'true' : 'false');
    }

    // Liste nach dem Suchtext einschränken. Ohne Argument: der Text im Feld (beim Tippen);
    // beim Öffnen wird '' übergeben - dann stehen immer alle Werte zur Wahl, auch wenn schon einer gewählt ist
    function comboSuchen(c, text) {
        const query = (text === undefined ? c.input.value : text).trim().toLowerCase();
        let treffer = 0;
        c.options.forEach(o => {
            // "Alle" steht immer oben in der Liste
            const zeigen = o.dataset.value === '' || query === '' || query === 'alle' || o.textContent.trim().toLowerCase().indexOf(query) >= 0;
            o.hidden = !zeigen;
            if (zeigen && o.dataset.value !== '') treffer++;
        });
        if (c.leer) c.leer.hidden = treffer > 0 || query === '' || query === 'alle';
    }

    const combos = columns.filter(c => c.combo);
    combos.forEach(c => {
        c.input.addEventListener('focus', () => {
            c.input.value = '';
            comboSuchen(c, '');
            comboOpen(c, true);
        });
        c.input.addEventListener('input', () => { comboSuchen(c); comboOpen(c, true); });
        c.toggle.addEventListener('click', () => {
            const show = c.panel.hidden;
            comboOpen(c, show);
            if (show) { comboSuchen(c, ''); c.input.focus(); }
        });
        // Häkchen: die Liste bleibt offen, damit weitere Werte dazugenommen werden können
        c.options.forEach(o => o.addEventListener('mousedown', e => e.preventDefault()));   // Fokus bleibt im Feld
        c.items.forEach(i => i.addEventListener('change', apply));
        // "Alle": alle Haken entfernen - die Tabelle zeigt wieder alle Zeilen
        if (c.alle) c.alle.addEventListener('mousedown', () => {
            c.items.forEach(i => { i.checked = false; });
            comboOpen(c, false);
            apply();
        });
    });
    // Beim Scrollen des Tabellenbereichs oder Ändern der Fenstergröße passt die feste Position nicht mehr: Liste schließen.
    // Ausnahme: das Zurückscrollen nach einer Filteränderung (apply) - die Kopfzeile mit der Liste bleibt dabei stehen.
    if (wrap) wrap.addEventListener('scroll', () => {
        if (eigenesScrollen) { eigenesScrollen = false; return; }
        combos.forEach(c => comboOpen(c, false));
    });
    window.addEventListener('resize', () => combos.forEach(c => comboOpen(c, false)));

    // Suchfelder PA, Kunden-Nr., Kunde filtern beim Tippen die Tabelle (bei den Dropdowns sucht das Tippen nur in der Liste)
    columns.filter(c => !c.combo).forEach(c => {
        c.input.addEventListener('input', () => { nurZiffern(c.input); apply(); });
    });

    // Klick außerhalb oder Escape schließt die offene Liste
    document.addEventListener('click', e => {
        filters.forEach(f => { if (!f.panel.hidden && !f.box.contains(e.target)) open(f, false); });
        combos.forEach(c => { if (!c.panel.hidden && !c.combo.contains(e.target)) comboOpen(c, false); });
    });
    document.addEventListener('keydown', e => {
        if (e.key !== 'Escape') return;
        combos.forEach(c => comboOpen(c, false));
        filters.forEach(f => {
            if (f.panel.hidden) return;
            open(f, false);
            if (f.search) { f.ohneOeffnen = document.activeElement !== f.search; f.search.focus(); } else f.toggle.focus();
        });
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
        // Abbrechen im Fortschritts-Popup: erst nachfragen. Solange die Frage offen ist, wird der laufende PA noch
        // fertig übergeben, aber kein neuer begonnen (rueckfrage). Bestätigt: nach dem laufenden PA aufhören.
        let abgebrochen = false, rueckfrage = null, laufenderPa = null, fortschritt = null;
        function abbrechenFragen() {
            if (rueckfrage || abgebrochen) return;
            rueckfrage = WipConfirm.ask(
                'Soll der Export an Sage abgebrochen werden?' +
                (laufenderPa ? '\n\nPA ' + laufenderPa + ' wird noch fertig übergeben, danach werden keine weiteren PAs gesendet.' : ''),
                { titel: 'Export abbrechen?', ok: 'Export abbrechen', abbrechen: 'Weiter übergeben' })
                .then(ja => {
                    rueckfrage = null;
                    if (!ja) return;
                    abgebrochen = true;
                    WipProgress.cancelable(abbrechenFragen, true);   // Knopf bleibt sichtbar, aber gesperrt
                    if (fortschritt) WipProgress.show('Abbruch: ' + (laufenderPa ? 'PA ' + laufenderPa + ' wird noch abgeschlossen …' : 'wird beendet …'), fortschritt[0], fortschritt[1]);
                });
        }
        try {
            WipProgress.show('Export wird vorbereitet …');
            WipProgress.cancelable(abbrechenFragen);
            const start = await post(form.dataset.exportStartUrl, pas.map(pa => ['pas', pa]));

            for (let i = 0; i < start.pas.length; i++) {
                if (rueckfrage) await rueckfrage;   // Rückfrage offen: mit dem nächsten PA warten
                if (abgebrochen) break;
                const pa = start.pas[i];
                laufenderPa = pa;
                fortschritt = [i, start.pas.length];
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
                laufenderPa = null;
            }
            if (rueckfrage) WipConfirm.close();   // Export ist fertig - eine noch offene Rückfrage erübrigt sich
            if (abgebrochen) {
                WipProgress.hide();
                const rest = start.pas.length - steps.length;
                WipMessage.show('Der Export wurde abgebrochen.' +
                    (rest > 0 ? ' ' + rest + ' PA(s) wurden nicht mehr an Sage gesendet und bleiben ausgewählt.' : '') +
                    '\n\n' + summary(steps), true);
                return;
            }
            WipProgress.show('Export abgeschlossen.', start.pas.length, start.pas.length);
        } catch (error) {
            if (rueckfrage) WipConfirm.close();
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