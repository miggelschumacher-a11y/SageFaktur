using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WipSageWeb.Data;
using WipSageWeb.Models;

namespace WipSageWeb.Controllers
{
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
        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
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

            var model = await LoadAsync(user, cancellationToken);
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

                // Rückmeldung von Sage je Position aus SagePos (Zuordnung über PI ohne "PI" und Ulfd) - nur Anzeige
                try
                {
                    var rueckmeldungen = await _repository.GetSagePosRueckmeldungAsync(pa, cancellationToken);
                    foreach (var pos in positionen.SelectMany(g => g))
                    {
                        var pi = (pos.PINummer ?? "").Trim().ToUpperInvariant();
                        if (pi.StartsWith("PI", StringComparison.Ordinal))
                        {
                            pi = pi.Substring(2);
                        }
                        if (pos.Ulfd.HasValue && rueckmeldungen.TryGetValue((pi, pos.Ulfd.Value), out var rueckmeldung))
                        {
                            pos.SageRueckmeldung = rueckmeldung;
                        }
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    _logger.LogError(ex, "Rückmeldung aus SagePos konnte für PA {PA} nicht gelesen werden", pa);
                }

                return PartialView("_Positionen", positionen);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Fehler beim Aufruf von WIPSage_ProcR_getWIP_PaketAnaEx für PA {PA}", pa);
                return StatusCode(500);
            }
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
            user.Sitzung = Guid.NewGuid().ToString("N");
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
            await SperrenFreigebenAsync(HttpContext.Session.GetLoginUser(), cancellationToken);
            HttpContext.Session.Clear();
            if (HasReturnUrl)
            {
                return Redirect(_loginOptions.ReturnUrl);
            }
            return RedirectToAction(nameof(Index));
        }

        // POST /WipPa/SperreVerlaengern - die offene Seite meldet sich alle 5 Minuten, damit ihre Sperren nicht ablaufen
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SperreVerlaengern(CancellationToken cancellationToken)
        {
            var denied = DenyUnlessAuthorized(out var user);
            if (denied != null)
            {
                return denied;
            }
            var anzahl = await _repository.SperrenVerlaengernAsync(EnsureSitzung(user).AlsSperrInhaber(), cancellationToken);
            return Json(new { ok = true, anzahl });
        }

        // POST /WipPa/SperreFreigeben - beim Schließen der Seite (navigator.sendBeacon)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SperreFreigeben(CancellationToken cancellationToken)
        {
            await SperrenFreigebenAsync(HttpContext.Session.GetLoginUser(), cancellationToken);
            return NoContent();
        }

        // POST /WipPa/ExportStart - Schritt 1 des Exports mit Fortschrittsanzeige:
        // Ordner prüfen und Positionen laden. Der vorbereitete Export liegt 15 Minuten im Speicher des Servers.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportStart(int[] pas, CancellationToken cancellationToken)
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
                var job = await _exportService.PrepareAsync(pas, EnsureSitzung(user).AlsSperrInhaber(), cancellationToken);
                var exportId = Guid.NewGuid().ToString("N");
                _cache.Set(ExportCacheKey(user, exportId), job, TimeSpan.FromMinutes(15));
                return Json(new { ok = true, exportId, folder = job.Folder, pas = job.PAs });
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

        // POST /WipPa/ExportStep - Schritt 2, je PA einmal: schreibt eine XML-Datei
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
                // file = null: PA ohne abrechenbare Positionen, es wurde nichts geschrieben
                return Json(new { ok = true, file = await _exportService.ExportOneAsync(job, pa, cancellationToken) });
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
        // Rückfrage mit OK beantwortet: SageHDR.Datei und DateiErstelltAm werden NULL, der Satz wird für ihn
        // gesperrt, und die alte XML-Datei wird - falls sie noch im Export-Ordner liegt - gelöscht.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NeuErstellen(int pa, CancellationToken cancellationToken)
        {
            var denied = DenyUnlessAuthorized(out var user);
            if (denied != null)
            {
                return denied;
            }

            try
            {
                var geloescht = await _exportService.NeuErstellenAsync(pa, EnsureSitzung(user).AlsSperrInhaber(), cancellationToken);
                _logger.LogInformation("PA {PA} zum erneuten Erstellen freigegeben von {User}; gelöschte Datei: {Datei}", pa, user.UserNr, geloescht ?? "(keine)");
                return Json(new { ok = true, geloescht });
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

            // Export: je ausgewähltem PA eine XML-Datei in Konfig.SageExportOrdner
            string popupMessage = null, errorMessage = null;
            SageExportResult exportResult = null;
            if (selectedPAs.Count == 0)
            {
                popupMessage = "Es wurde kein PA ausgewählt.";
            }
            else
            {
                try
                {
                    exportResult = await _exportService.ExportAsync(selectedPAs, EnsureSitzung(user).AlsSperrInhaber(), cancellationToken);
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

            var model = await LoadAsync(user, cancellationToken);
            foreach (var row in model.Rows)
            {
                row.IsSelected = row.Zustand == PaZustand.Bearbeitbar && row.PA.HasValue && selectedPAs.Contains(row.PA.Value);
            }
            model.ExportResult = exportResult;
            model.PopupMessage = popupMessage;
            model.ErrorMessage = errorMessage ?? model.ErrorMessage;

            // Sonst würden die geposteten Werte (per Index) die neu geladenen Zeilen überschreiben
            ModelState.Clear();
            return View(nameof(Index), model);
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

        // Ältere Anmeldungen (vor Einführung der Sperren) haben noch keine Kennung - dann jetzt vergeben
        private LoginUser EnsureSitzung(LoginUser user)
        {
            if (string.IsNullOrEmpty(user.Sitzung))
            {
                user.Sitzung = Guid.NewGuid().ToString("N");
                HttpContext.Session.SetLoginUser(user);
            }
            return user;
        }

        private async Task SperrenFreigebenAsync(LoginUser user, CancellationToken cancellationToken)
        {
            if (user == null || string.IsNullOrEmpty(user.Sitzung))
            {
                return;
            }
            try
            {
                await _repository.SperrenFreigebenAsync(user.AlsSperrInhaber(), cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Abmelden soll trotzdem klappen; die Sperren laufen dann nach SageSperre.Minuten ab
                _logger.LogError(ex, "Sperren in SageHDR konnten nicht aufgehoben werden");
            }
        }

        private async Task<WipPaListViewModel> LoadAsync(LoginUser user, CancellationToken cancellationToken)
        {
            var model = new WipPaListViewModel { IsLoggedIn = true, IsAuthorized = true };
            try
            {
                model.Rows = (await _repository.GetWipPAsAsync(cancellationToken)).ToList();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Fehler beim Aufruf von WIPSage_ProcR_getWIP_PAs");
                model.ErrorMessage = $"Die PAs konnten nicht geladen werden: {ex.Message}";
                return model;
            }

            // Jeder PA bekommt einen Eintrag in SageHDR und wird für diesen Benutzer gesperrt, wenn er frei ist.
            // Schlägt das fehl, bleiben alle PAs nur lesbar - sonst könnten XML-Dateien doppelt entstehen.
            IReadOnlyDictionary<int, SageHdrZustand> zustaende;
            try
            {
                var pas = model.Rows.Where(r => r.PA.HasValue).Select(r => r.PA.Value);
                zustaende = await _repository.SperrenAsync(pas, EnsureSitzung(user).AlsSperrInhaber(), cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "PAs konnten in SageHDR nicht gesperrt werden");
                model.ErrorMessage = $"Die PAs konnten nicht gesperrt werden und sind deshalb nur lesbar: {ex.Message}";
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
                        row.Datei = zustand.Datei;
                        row.UebergebenAm = zustand.DateiErstelltAm;
                        row.UebergebenVon = zustand.DateiErstelltVon;

                        // Hält ein anderer Benutzer den Satz (er erstellt die Datei gerade neu), bleibt er nur lesbar
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

                if (row.PA.HasValue && rueckmeldungen.TryGetValue(row.PA.Value, out var rueckmeldung))
                {
                    row.SageRueckmeldung = rueckmeldung;
                }

                // Vorausgewählt sind alle PAs, die dieser Benutzer übergeben darf - schon übergebene nie,
                // sie werden erst nach der Rückfrage zum Neu-Erstellen angehakt
                row.IsSelected = row.Zustand == PaZustand.Bearbeitbar;
            }
            return model;
        }
    }
}
