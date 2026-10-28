using System.Collections.Generic;
using System.Linq;

namespace WipSageWeb.Models
{
    public class WipPaListViewModel
    {
        public List<WipPa> Rows { get; set; } = new List<WipPa>();

        /// <summary>
        /// Kennung dieser geöffneten Seite für die Sperren in SageHDR (SperrKennung). Die Seite schickt sie bei jedem
        /// Aufruf mit, damit die Freigabe beim Verlassen nur ihre eigenen Sperren löscht.
        /// </summary>
        public string Seite { get; set; }

        /// <summary>Fehler beim Laden der Daten (z. B. keine DB-Verbindung).</summary>
        public string ErrorMessage { get; set; }

        /// <summary>Meldung, die als Popup über der Seite erscheint (z. B. Ergebnis des Exports, keine gültige Sage-Adresse).</summary>
        public string PopupMessage { get; set; }

        /// <summary>Solange false, zeigt die View das Anmelde-Popup und lädt keine Daten.</summary>
        public bool IsLoggedIn { get; set; }

        public LoginViewModel Login { get; set; } = new LoginViewModel();

        /// <summary>Fehlermeldung im Anmelde-Popup (z. B. Datenbank nicht erreichbar).</summary>
        public string LoginError { get; set; }

        /// <summary>true, wenn der angemeldete Benutzer die Berechtigung SageFaktur hat.</summary>
        public bool IsAuthorized { get; set; }

        /// <summary>Nur angemeldet und berechtigt sind "Neu laden" und "An Sage übergeben" bedienbar.</summary>
        public bool CanWork => IsLoggedIn && IsAuthorized;

        public int SelectedCount => Rows.Count(r => r.IsSelected);

        // Die Prozedur kann denselben PA in mehreren Zeilen liefern (z. B. je PA-Version oder Bemerkung).
        // Im Kopf zählt jeder PA nur einmal - sowohl bei der Anzahl als auch bei der Summe.
        // Zeilen ohne PA-Nummer lassen sich nicht zusammenfassen und zählen einzeln.
        private IEnumerable<WipPa> EinmaligePAs =>
            Rows.Where(r => r.PA.HasValue).GroupBy(r => r.PA.Value).Select(g => g.First())
                .Concat(Rows.Where(r => !r.PA.HasValue));

        /// <summary>Anzahl unterschiedlicher PAs.</summary>
        public int PaCount => EinmaligePAs.Count();

        /// <summary>Summe der Endpreise, jeder PA nur einmal gerechnet.</summary>
        public decimal EndpreisTotal => EinmaligePAs.Sum(r => r.EndpreisSum ?? 0m);
    }
}
