using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WipSageWeb.Data;
using WipSageWeb.Models;

namespace WipSageWeb.Controllers
{
    // Seiten und Antworten nicht im Browser zwischenspeichern: sie enthalten frische Daten und Sicherheits-Token.
    // Sonst kann der Browser (z. B. Firefox beim Zurück-Gehen oder Wiederherstellen eines Tabs) eine alte Seite
    // zeigen, die nicht mehr zu den aktuellen Skripten passt - dann reagieren z. B. die Anmeldefelder nicht mehr.
    // Skripte, Stile und Bilder bleiben im Cache; sie tragen eine Versionskennung (asp-append-version).
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class WipPaController : Controller
    {
        private readonly IWipSageRepository _repository;
        private readonly ILoginService _loginService;
        private readonly ISageExportService _exportService;
        private readonly IMemoryCache _cache;
        private readonly LoginOptions _loginOptions;
        private readonly ILogger<WipPaController> _logger;

        public WipPaController(
            IWipSageRepository repository,
            ILoginService loginService,
            ISageExportService exportService,
            IMemoryCache cache,
            IOptions<LoginOptions> loginOptions,
            ILogger<WipPaController> logger)
        {
            _repository = repository;
            _loginService = loginService;
            _exportService = exportService;
            _cache = cache;
            _loginOptions = loginOptions.Value;
            _logger = logger;
        }

        // GET /WipPa
        // Ohne Anmeldung wird nur die leere Seite mit dem Anmelde-Popup geliefert - keine Daten.
        // GET /WipPa?ausHistory=true - Rückkehr aus der History: die PAs aus dem Zwischenspeicher (siehe LoadAsync)
        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken, bool ausHistory = false)
        {
            var user = HttpContext.Session.GetLoginUser();
            if (user == null)
            {
                return View(new WipPaListViewModel());
            }
            if (!user.SageFaktur)
            {
                // Angemeldet, aber ohne Berechtigung: keine Daten laden, nur der Hinweis
                return View(new WipPaListViewModel { IsLoggedIn = true, PopupMessage = NoPermissionMessage });
            }

            // Jedes Laden der Seite bekommt eine eigene Kennung für die Sperren beim Export (siehe SperrKennung)
            var model = await LoadAsync(user, SperrKennung.Neu(), cancellationToken, ausHistory);
            return View(model);
        }

        // GET /WipPa/Positionen?pa=41001 - Inhalt des Akkordeons einer PA-Zeile, wird beim ersten Aufklappen
        // dieser Zeile nachgeladen. Das Skript merkt sich die Antwort, bis die Seite neu geladen wird.
        [HttpGet]
        public async Task<IActionResult> Positionen(int pa, CancellationToken cancellationToken)
        {
            var denied = DenyUnlessAuthorized(out _);
            if (denied != null)
            {
                return denied;
            }

            try
            {
                // Anzeige im Akkordeon sortiert nach PI und Ulfd (wie die Positionen in SagePos)
                var positionen = (await _repository.GetWipPaketAnaAsync(pa, cancellationToken))
                    .Where(p => p.BelegMatchCode == pa)
                    .OrderBy(p => p.PINummer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Ulfd ?? int.MaxValue)
                    .GroupBy(p => p.BelegMatchCode.Value)
                    .ToList();

                // Rückmeldung von Sage je Position aus SagePos (Zuordnung über PI = zehn Ziffern hinter "PI", und Ulfd) - nur Anzeige
                try
                {
                    var rueckmeldungen = await _repository.GetSagePosRueckmeldungAsync(pa, cancellationToken);
                    foreach (var pos in positionen.SelectMany(g => g))
                    {
                        var pi = pos.PI;
                        if (pi != null && pos.Ulfd.HasValue && rueckmeldungen.TryGetValue((pi, pos.Ulfd.Value), out var rueckmeldung))
                        {
                            pos.SageRueckmeldung = rueckmeldung;
                        }
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger.LogError(ex, "Rückmeldung aus SagePos konnte für PA {PA} nicht gelesen werden", pa);
                }

                // Testbetrieb (Konfig.SageTest): in den Spalten Artikel-Nr. und KST die Werte zeigen, die an Sage gehen
                // (SageExportService.TestArtikelnummer / TestKostenstelle) - fehlt die Konfiguration, wie im echten Betrieb
                try
                {
                    ViewData["SageTest"] = (await _repository.GetSageKonfigAsync(cancellationToken)).Test;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger.LogError(ex, "Konfig.SageTest konnte für die Positionen nicht gelesen werden");
                }

                return PartialView("_Positionen", positionen);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Fehler beim Aufruf von WIPSage_ProcR_getWIP_PaketAnaEx für PA {PA}", pa);
                return StatusCode(500);
            }
        }

        // Höchstzahl der Zeilen auf der History-Seite - bei mehr Treffern weist die Seite darauf hin
        private const int HistoryMaxZeilen = 500;

        // Die Suchfelder der History bleiben in der Sitzung gespeichert, bis "Zurücksetzen" oder Abmelden
        private const string HistoryFilterKey = "WipSage.HistoryFilter";

        // GET /WipPa/History?pa=..&kunde=..&vorgang=..&belegnr=..&belegdatum=2026-11-17 - an Sage übertragene PAs
        // aus dbo.WIPSageUebertragenePAs_V suchen. Ohne Anmeldung oder Berechtigung zurück zur Startseite
        // (Anmelde-Popup bzw. Hinweis). Zahlenfelder: nur Ziffern, höchstens 10; Belegdatum: yyyy-MM-dd (input type=date).
        // Ohne Parameter (Button auf der Hauptseite): die zuletzt eingestellten Suchfelder; ?reset=1 leert sie.
        // Ist kein Suchfeld gefüllt, zeigt die Seite nur die letzten Konfig.SageHistoryDays Tage.
        [HttpGet]
        public async Task<IActionResult> History(string pa, string kunde, string vorgang, string belegnr, string belegdatum, string reset, CancellationToken cancellationToken)
        {
            if (DenyUnlessAuthorized(out _) != null)
            {
                return RedirectToAction(nameof(Index));
            }

            if (!string.IsNullOrEmpty(reset))
            {
                HttpContext.Session.Remove(HistoryFilterKey);
                pa = kunde = vorgang = belegnr = belegdatum = null;
            }
            else if (Request.Query.Count == 0)
            {
                // Erneut geöffnet: die gespeicherten Suchfelder wieder einsetzen ("pa|kunde|vorgang|belegnr|belegdatum")
                var gespeichert = (HttpContext.Session.GetString(HistoryFilterKey) ?? "").Split('|');
                if (gespeichert.Length == 5)
                {
                    pa = gespeichert[0]; kunde = gespeichert[1]; vorgang = gespeichert[2]; belegnr = gespeichert[3]; belegdatum = gespeichert[4];
                }
            }

            var model = new HistoryViewModel
            {
                MaxZeilen = HistoryMaxZeilen,
                Filter = new HistoryFilter
                {
                    PA = Ganzzahl(pa),
                    Kunde = Ganzzahl(kunde),
                    Vorgang = Ganzzahl(vorgang),
                    BelegNummer = Ganzzahl(belegnr),
                    BelegDatum = DateTime.TryParseExact((belegdatum ?? "").Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var datum) ? datum : (DateTime?)null
                }
            };

            // Gültige Werte merken - beim nächsten Öffnen der History stehen sie wieder in den Suchfeldern
            var filter = model.Filter;
            HttpContext.Session.SetString(HistoryFilterKey, string.Join("|",
                filter.PA?.ToString(), filter.Kunde?.ToString(), filter.Vorgang?.ToString(), filter.BelegNummer?.ToString(),
                filter.BelegDatum?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));

            try
            {
                // Ohne Suchfeld nur die letzten Tage (Konfig.SageHistoryDays; leer oder 0 = alle)
                if (filter.IstLeer)
                {
                    var tage = (await _repository.GetSageKonfigAsync(cancellationToken)).HistoryDays;
                    if (tage > 0)
                    {
                        model.LetzteTage = tage;
                        filter.Seit = DateTime.Today.AddDays(-tage.Value);
                    }
                }

                var rows = await _repository.GetUebertragenePAsAsync(filter, HistoryMaxZeilen, cancellationToken);
                model.MehrTreffer = rows.Count > HistoryMaxZeilen;
                model.Rows = rows.Take(HistoryMaxZeilen).ToList();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Fehler beim Lesen von WIPSageUebertragenePAs_V");
                model.ErrorMessage = $"Die übertragenen PAs konnten nicht gelesen werden ({DateTime.Now:dd.MM.yyyy HH:mm:ss}): {ex.Message}";
            }

            // Zurück-Symbol im Header (links neben dem Home-Symbol) führt auf die PA-Liste - mit den zwischengespeicherten PAs
            ViewData["ZurueckUrl"] = Url.Action(nameof(Index), new { ausHistory = true });
            return View(model);
        }

        // Nur Ziffern, höchstens 10 - alles andere gilt als leeres Feld (filtert nicht)
        private static long? Ganzzahl(string text)
        {
            text = (text ?? "").Trim();
            return text.Length > 0 && text.Length <= 10 && text.All(char.IsDigit) ? long.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : (long?)null;
        }

        // POST /WipPa/Login - aus dem Anmelde-Popup
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login([Bind(Prefix = "Login")] LoginViewModel login, CancellationToken cancellationToken)
        {
            var model = new WipPaListViewModel();
            model.Login.UserNr = login.UserNr;

            if (!ModelState.IsValid)
            {
                return View(nameof(Index), model);
            }

            LoginUser user;
            try
            {
                user = await _loginService.TryLoginAsync(login.UserNr, login.Password, cancellationToken);
                if (user != null)
                {
                    // Berechtigung für die Fakturierung mit Sage: ZugriffEx.SageFaktu muss 1 sein
                    user.SageFaktur = await _repository.GetSageFakturAsync(user.UserNr, cancellationToken);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Technischer Fehler (z. B. Datenbank nicht erreichbar) ist keine gescheiterte Anmeldung
                _logger.LogError(ex, "Fehler bei der Anmeldung");
                model.LoginError = $"Die Anmeldung ist zurzeit nicht möglich: {ex.Message}";
                return View(nameof(Index), model);
            }

            if (user == null)
            {
                _logger.LogWarning("Anmeldung gescheitert für Benutzernummer {UserNr}", login.UserNr);

                // Anmeldung gescheitert -> zurück zur aufrufenden Web-Applikation
                if (HasReturnUrl)
                {
                    return Redirect(_loginOptions.ReturnUrl);
                }
                model.LoginError = "Ungültiges Passwort / Benutzer oder ohne Zugriff!";
                return View(nameof(Index), model);
            }

            // Neue Kennung je Anmeldung: Abmelden hebt nur die Sperren dieser Anmeldung auf
            user.Sitzung = SperrKennung.Neu();
            HttpContext.Session.SetLoginUser(user);
            if (!user.SageFaktur)
            {
                _logger.LogWarning("Benutzer {UserNr} hat keine Berechtigung SageFaktur", user.UserNr);
            }

            // Redirect statt direkter Ausgabe: F5 schickt dann nicht erneut das Passwort
            return RedirectToAction(nameof(Index));
        }

        // POST /WipPa/Logout - Power-Symbol in der Kopfleiste
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout(CancellationToken cancellationToken)
        {
            // Abmelden: Sperren aller Seiten dieser Anmeldung freigeben
            await SperrenFreigebenAsync(HttpContext.Session.GetLoginUser(), null, true, cancellationToken);
            HttpContext.Session.Clear();
            if (HasReturnUrl)
            {
                return Redirect(_loginOptions.ReturnUrl);
            }
            return RedirectToAction(nameof(Index));
        }

        // POST /WipPa/ExportStart - Schritt 1 des Exports mit Fortschrittsanzeige:
        // Adresse der Sage-Schnittstelle prüfen, PAs festlegen. Der vorbereitete Export liegt 15 Minuten im Speicher des Servers.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportStart(int[] pas, string seite, CancellationToken cancellationToken)
        {
            var denied = DenyUnlessAuthorized(out var user);
            if (denied != null)
            {
                return denied;
            }
            if (pas == null || pas.Length == 0)
            {
                return Json(new { ok = false, message = "Es wurde kein PA ausgewählt." });
            }

            try
            {
                var job = await _exportService.PrepareAsync(pas, Inhaber(user, seite), cancellationToken);
                var exportId = Guid.NewGuid().ToString("N");
                _cache.Set(ExportCacheKey(user, exportId), job, TimeSpan.FromMinutes(15));
                return Json(new { ok = true, exportId, pas = job.PAs });
            }
            catch (SageExportException ex)
            {
                _logger.LogWarning(ex, "Export an Sage nicht möglich");
                return Json(new { ok = false, message = ex.Message });
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Fehler beim Vorbereiten des Exports an Sage");
                return Json(new { ok = false, message = $"Der Export an Sage ist fehlgeschlagen: {ex.Message}" });
            }
        }

        // POST /WipPa/ExportStep - Schritt 2, je PA einmal: sendet den Auftrag an Sage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportStep(string exportId, int pa, CancellationToken cancellationToken)
        {
            var denied = DenyUnlessAuthorized(out var user);
            if (denied != null)
            {
                return denied;
            }
            if (!_cache.TryGetValue(ExportCacheKey(user, exportId), out SageExportJob job))
            {
                return Json(new { ok = false, message = "Der Export ist abgelaufen. Bitte erneut starten." });
            }

            try
            {
                // ergebnis: Uebergeben, Abgelehnt (meldung von Sage), KeineAntwort oder KeinePositionen (nichts gesendet)
                var schritt = await _exportService.ExportOneAsync(job, pa, cancellationToken);
                return Json(new { ok = true, ergebnis = schritt.Ergebnis.ToString(), officeLineHandle = schritt.OfficeLineHandle, officeLineVorgangsHandle = schritt.OfficeLineVorgangsHandle, meldung = schritt.Meldung });
            }
            catch (SageExportException ex)
            {
                _logger.LogWarning(ex, "Export an Sage nicht möglich (PA {PA})", pa);
                return Json(new { ok = false, message = ex.Message });
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Fehler beim Export an Sage (PA {PA})", pa);
                return Json(new { ok = false, message = $"Der Export von PA {pa} ist fehlgeschlagen: {ex.Message}" });
            }
        }

        // POST /WipPa/NeuErstellen - der Benutzer hat bei einem schon übergebenen PA den Haken gesetzt und die
        // Rückfrage mit OK beantwortet: SageHDR.Status wird "neu" (gesperrt wird erst beim Export).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NeuErstellen(int pa, string seite, CancellationToken cancellationToken)
        {
            var denied = DenyUnlessAuthorized(out var user);
            if (denied != null)
            {
                return denied;
            }

            try
            {
                await _exportService.NeuErstellenAsync(pa, Inhaber(user, seite), cancellationToken);
                _logger.LogInformation("PA {PA} zur erneuten Übergabe freigegeben von {User}", pa, user.UserNr);
                return Json(new { ok = true });
            }
            catch (SageExportException ex)
            {
                _logger.LogWarning(ex, "PA {PA} kann nicht neu erstellt werden", pa);
                return Json(new { ok = false, message = ex.Message });
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Fehler beim Freigeben von PA {PA} zum erneuten Erstellen", pa);
                return Json(new { ok = false, message = $"PA {pa} konnte nicht für das erneute Erstellen freigegeben werden: {ex.Message}" });
            }
        }

        // Der Benutzer gehört zum Schlüssel: ein Export kann nur von dem fortgesetzt werden, der ihn gestartet hat
        private static string ExportCacheKey(LoginUser user, string exportId) => $"SageExport:{user.UserNr}:{exportId}";

        // POST /WipPa/Uebernehmen - Export ohne Fortschrittsanzeige (falls im Browser kein JavaScript läuft)
        // Aus dem Formular kommen nur PA + IsSelected je Zeile; alle anderen Felder
        // werden frisch aus der Datenbank gelesen (sie sind in der View nicht editierbar).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Uebernehmen(WipPaListViewModel posted, CancellationToken cancellationToken)
        {
            if (DenyUnlessAuthorized(out var user) != null)
            {
                // Session abgelaufen oder keine Berechtigung -> zurück zur Startseite (Anmelde-Popup bzw. Hinweis)
                return RedirectToAction(nameof(Index));
            }

            var selectedPAs = posted.Rows
                .Where(r => r.IsSelected && r.PA.HasValue)
                .Select(r => r.PA.Value)
                .ToHashSet();

            // Export: je ausgewähltem PA ein Auftrag an die Sage-Schnittstelle (Konfig.SageTestExportURL bzw. SageReleaseExportURL)
            string popupMessage = null, errorMessage = null;
            if (selectedPAs.Count == 0)
            {
                popupMessage = "Es wurde kein PA ausgewählt.";
            }
            else
            {
                try
                {
                    var exportResult = await _exportService.ExportAsync(selectedPAs, Inhaber(user, posted.Seite), cancellationToken);
                    popupMessage = Zusammenfassung(exportResult.Schritte);
                }
                catch (SageExportException ex)
                {
                    _logger.LogWarning(ex, "Export an Sage nicht möglich");
                    popupMessage = ex.Message;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger.LogError(ex, "Fehler beim Export an Sage");
                    errorMessage = $"Der Export an Sage ist fehlgeschlagen: {ex.Message}";
                }
            }

            var model = await LoadAsync(user, SperrKennung.Neu(), cancellationToken);
            foreach (var row in model.Rows)
            {
                row.IsSelected = row.IstAuswaehlbar && row.PA.HasValue && selectedPAs.Contains(row.PA.Value);
            }
            model.PopupMessage = popupMessage;
            model.ErrorMessage = errorMessage ?? model.ErrorMessage;

            // Sonst würden die geposteten Werte (per Index) die neu geladenen Zeilen überschreiben
            ModelState.Clear();
            return View(nameof(Index), model);
        }

        // Ergebnis des Exports für das Meldungs-Popup (wie wippa.js es nach dem Export zeigt)
        private static string Zusammenfassung(IEnumerable<SageExportSchritt> schritte)
        {
            // Gleiche Meldungen stehen nur einmal, dahinter die PAs - bei Hunderten PAs bleibt die Liste lesbar
            var liste = schritte.ToList();
            var uebergeben = liste.Where(s => s.Ergebnis == SageExportErgebnis.Uebergeben).ToList();
            var zeilen = new List<string> { $"{uebergeben.Count} PA(s) an Sage übergeben." };
            // Je übergebenem PA die OfficeLineHandle des Auftrags und die OfficeLineVorgangsHandle aus der Antwort von Sage
            zeilen.AddRange(uebergeben.Select(s => $"PA {s.PA}: OfficeLineHandle {s.OfficeLineHandle?.ToString() ?? "-"}, Vorgang {s.OfficeLineVorgangsHandle?.ToString() ?? "-"}"
                + (string.IsNullOrWhiteSpace(s.Meldung) ? "" : $" ({s.Meldung})")));
            void Block(string titel, SageExportErgebnis ergebnis)
            {
                var teil = liste.Where(s => s.Ergebnis == ergebnis).ToList();
                if (teil.Count == 0)
                {
                    return;
                }
                zeilen.Add("");
                zeilen.Add($"{titel} ({teil.Count}):");
                // Zeilenumbrüche zusammenziehen, führende Satzzeichen weg (Sage liefert z. B. ", Fehler in ...")
                foreach (var gruppe in teil.GroupBy(s => System.Text.RegularExpressions.Regex.Replace(s.Meldung ?? "", @"\s+", " ").TrimStart(' ', ',', ';', ':', '-').Trim()))
                {
                    // Je Meldung: "• Meldung" und darunter die PAs
                    if (gruppe.Key.Length > 0)
                    {
                        zeilen.Add("• " + gruppe.Key);
                    }
                    zeilen.Add("PA " + string.Join(", ", gruppe.Select(s => s.PA)));
                }
            }
            Block("Von Sage abgelehnt", SageExportErgebnis.Abgelehnt);
            Block("Konnte nicht gesperrt werden", SageExportErgebnis.Gesperrt);
            Block("Nicht übergeben", SageExportErgebnis.NichtUebergeben);
            Block("Keine Antwort von Sage", SageExportErgebnis.KeineAntwort);
            Block("Keine abrechenbaren Positionen, nicht übergeben", SageExportErgebnis.KeinePositionen);
            return string.Join("\n", zeilen);
        }

        private bool HasReturnUrl => !string.IsNullOrWhiteSpace(_loginOptions.ReturnUrl);

        private const string NoPermissionMessage =
            "Sie haben keine Berechtigung für die Fakturierung mit Sage.\nBitte wenden Sie sich an Ihren Administrator.";

        // Für die Aufrufe aus dem Skript: null = angemeldet und berechtigt, sonst 401 (nicht angemeldet) oder 403 (keine Berechtigung)
        private IActionResult DenyUnlessAuthorized(out LoginUser user)
        {
            user = HttpContext.Session.GetLoginUser();
            if (user == null)
            {
                return StatusCode(401);
            }
            return user.SageFaktur ? null : StatusCode(403);
        }

        // Ältere Anmeldungen (ohne oder mit einer Kennung im alten, längeren Format) bekommen jetzt eine neue
        private LoginUser EnsureSitzung(LoginUser user)
        {
            if (!SperrKennung.IstGueltig(user.Sitzung))
            {
                user.Sitzung = SperrKennung.Neu();
                HttpContext.Session.SetLoginUser(user);
            }
            return user;
        }

        // Sperr-Inhaber für die Seite, die den Aufruf schickt. Fehlt deren Kennung oder ist sie ungültig, gilt eine
        // neue - die Sperren laufen dann ab bzw. werden beim Abmelden freigegeben.
        private SperrInhaber Inhaber(LoginUser user, string seite) =>
            EnsureSitzung(user).AlsSperrInhaber(SperrKennung.IstGueltig(seite) ? seite : SperrKennung.Neu());

        private async Task SperrenFreigebenAsync(LoginUser user, string seite, bool ganzeAnmeldung, CancellationToken cancellationToken)
        {
            if (user == null || !SperrKennung.IstGueltig(user.Sitzung))
            {
                return;
            }
            try
            {
                await _repository.SperrenFreigebenAsync(user.AlsSperrInhaber(seite ?? ""), ganzeAnmeldung, cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Abmelden soll trotzdem klappen; die Sperren laufen dann nach SageSperre.Minuten ab
                _logger.LogError(ex, "Sperren in SageHDR konnten nicht aufgehoben werden");
            }
        }

        // Zwischenspeicher für das Ergebnis von WIPSage_ProcR_getWIP_PAs je Anmeldung: Rückkehr aus der History ohne
        // den teuren Prozeduraufruf. Zustand und Rückmeldung aus SageHDR werden trotzdem jedes Mal frisch gelesen.
        private static readonly TimeSpan PaZwischenspeicherDauer = TimeSpan.FromHours(1);

        private sealed class PaZwischenspeicher
        {
            public List<WipPa> Rows;
            public DateTime Stand;
        }

        private static string PaCacheKey(LoginUser user) => "WipPAs:" + user.UserNr + ":" + user.Sitzung;

        // ausZwischenspeicher: Rückkehr aus der History - die PAs aus dem Zwischenspeicher nehmen, falls vorhanden
        private async Task<WipPaListViewModel> LoadAsync(LoginUser user, string seite, CancellationToken cancellationToken, bool ausZwischenspeicher = false)
        {
            var model = new WipPaListViewModel { IsLoggedIn = true, IsAuthorized = true, Seite = seite };
            try
            {
                user = EnsureSitzung(user);
                if (ausZwischenspeicher && _cache.TryGetValue(PaCacheKey(user), out PaZwischenspeicher gespeichert))
                {
                    // Kopien: LoadAsync setzt unten Zustand, Auswahl usw. - der Zwischenspeicher bleibt unverändert
                    model.Rows = gespeichert.Rows.Select(r => r.Kopie()).ToList();
                    model.DatenStand = gespeichert.Stand;
                    model.AusZwischenspeicher = true;
                }
                else
                {
                    model.Rows = (await _repository.GetWipPAsAsync(cancellationToken)).ToList();
                    model.DatenStand = DateTime.Now;
                    _cache.Set(PaCacheKey(user), new PaZwischenspeicher { Rows = model.Rows.Select(r => r.Kopie()).ToList(), Stand = model.DatenStand.Value },
                        PaZwischenspeicherDauer);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Fehler beim Aufruf von WIPSage_ProcR_getWIP_PAs");
                model.ErrorMessage = $"Die PAs konnten nicht geladen werden: {ex.Message}";
                return model;
            }

            // Zustand der PAs aus SageHDR lesen - ohne Sperre (gesperrt wird erst beim Export, je PA).
            // Schlägt das fehl, bleiben alle PAs nur lesbar - sonst könnten schon übergebene PAs erneut an Sage gehen.
            IReadOnlyDictionary<int, SageHdrZustand> zustaende;
            try
            {
                var pas = model.Rows.Where(r => r.PA.HasValue).Select(r => r.PA.Value);
                zustaende = await _repository.GetSageHdrZustaendeAsync(pas, Inhaber(user, seite), cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Zustand der PAs konnte aus SageHDR nicht gelesen werden");
                model.ErrorMessage = $"Der Zustand der PAs konnte nicht gelesen werden, sie sind deshalb nur lesbar: {ex.Message}";
                return model;
            }

            // Status und Meldung von Sage (SageHDR.Status/MessageVonSage) - nur Anzeige; fehlt sie, geht es ohne weiter
            IReadOnlyDictionary<int, SageRueckmeldung> rueckmeldungen = new Dictionary<int, SageRueckmeldung>();
            try
            {
                rueckmeldungen = await _repository.GetSageHdrRueckmeldungAsync(model.Rows.Where(r => r.PA.HasValue).Select(r => r.PA.Value), cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Rückmeldung aus SageHDR konnte nicht gelesen werden");
            }

            foreach (var row in model.Rows)
            {
                if (row.PA.HasValue && zustaende.TryGetValue(row.PA.Value, out var zustand))
                {
                    if (zustand.IstUebergeben)
                    {
                        row.Zustand = PaZustand.Uebergeben;
                        row.UebergebenAm = zustand.AnSageGesendetAm;
                        row.UebergebenVon = zustand.AnSageGesendetVon;

                        // Exportiert ihn gerade ein anderer Benutzer, bleibt er nur lesbar
                        row.GesperrtFuerAndere = zustand.GesperrtFuerAndere;
                        if (zustand.GesperrtFuerAndere)
                        {
                            row.GesperrtVon = zustand.GesperrtVon;
                            row.GesperrtBis = zustand.GesperrtBis;
                        }
                    }
                    else if (zustand.GesperrtFuerAndere)
                    {
                        row.Zustand = PaZustand.GesperrtVonAnderem;
                        row.GesperrtVon = zustand.GesperrtVon;
                        row.GesperrtBis = zustand.GesperrtBis;
                    }
                    else
                    {
                        row.Zustand = PaZustand.Bearbeitbar;
                    }
                }
                else if (row.PA.HasValue)
                {
                    // Noch kein Satz in SageHDR: noch nie übergeben - der Satz entsteht beim ersten Export
                    row.Zustand = PaZustand.Bearbeitbar;
                }

                if (row.PA.HasValue && rueckmeldungen.TryGetValue(row.PA.Value, out var rueckmeldung))
                {
                    row.SageRueckmeldung = rueckmeldung;
                }

                // Vorausgewählt sind alle PAs, die dieser Benutzer übergeben darf - schon übergebene nie,
                // sie werden erst nach der Rückfrage zum Neu-Erstellen angehakt; PAs mit Problem nie
                row.IsSelected = row.IstAuswaehlbar;
            }
            return model;
        }
    }
}
