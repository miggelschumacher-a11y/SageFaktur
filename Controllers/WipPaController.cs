using System;
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

            var model = await LoadAsync(cancellationToken);
            return View(model);
        }

        // GET /WipPa/Positionen - Inhalt der Akkordeons, wird beim ersten Aufklappen nachgeladen.
        // Die Prozedur kennt keinen PA-Parameter, deshalb kommen alle PAs in einer Antwort;
        // das Skript verteilt die Blöcke und merkt sie sich, bis die Seite neu geladen wird.
        [HttpGet]
        public async Task<IActionResult> Positionen(CancellationToken cancellationToken)
        {
            var denied = DenyUnlessAuthorized(out _);
            if (denied != null)
            {
                return denied;
            }

            try
            {
                var positionen = (await _repository.GetWipPaketAnaAsync(cancellationToken))
                    .Where(p => p.BelegMatchCode.HasValue)
                    .GroupBy(p => p.BelegMatchCode.Value)
                    .ToList();
                return PartialView("_Positionen", positionen);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Fehler beim Aufruf von WIPSage_ProcR_getWIP_PaketAna");
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
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            if (HasReturnUrl)
            {
                return Redirect(_loginOptions.ReturnUrl);
            }
            return RedirectToAction(nameof(Index));
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
                var job = await _exportService.PrepareAsync(pas, cancellationToken);
                var exportId = Guid.NewGuid().ToString("N");
                _cache.Set(ExportCacheKey(user, exportId), job, TimeSpan.FromMinutes(15));
                return Json(new { ok = true, exportId, folder = job.Folder, pas = job.Positions.Keys.OrderBy(p => p) });
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
        public IActionResult ExportStep(string exportId, int pa)
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
                return Json(new { ok = true, file = _exportService.ExportOne(job, pa) });
            }
            catch (SageExportException ex)
            {
                _logger.LogWarning(ex, "Export an Sage nicht möglich (PA {PA})", pa);
                return Json(new { ok = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Export an Sage (PA {PA})", pa);
                return Json(new { ok = false, message = $"Der Export von PA {pa} ist fehlgeschlagen: {ex.Message}" });
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
            if (DenyUnlessAuthorized(out _) != null)
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
                    exportResult = await _exportService.ExportAsync(selectedPAs, cancellationToken);
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

            var model = await LoadAsync(cancellationToken);
            foreach (var row in model.Rows)
            {
                row.IsSelected = row.PA.HasValue && selectedPAs.Contains(row.PA.Value);
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

        private async Task<WipPaListViewModel> LoadAsync(CancellationToken cancellationToken)
        {
            var model = new WipPaListViewModel { IsLoggedIn = true, IsAuthorized = true };
            try
            {
                model.Rows = (await _repository.GetWipPAsAsync(cancellationToken)).ToList();

                // Alle PAs sind vorausgewählt; der Benutzer nimmt die Haken heraus, die er nicht übergeben will
                foreach (var row in model.Rows)
                {
                    row.IsSelected = true;
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Fehler beim Aufruf von WIPSage_ProcR_getWIP_PAs");
                model.ErrorMessage = $"Die PAs konnten nicht geladen werden: {ex.Message}";
            }
            return model;
        }
    }
}
