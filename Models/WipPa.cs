using System;
using System.ComponentModel.DataAnnotations;

namespace WipSageWeb.Models
{
    /// <summary>
    /// Eine Zeile aus dbo.WIPSage_ProcR_getWIP_PAs (ein abzurechnender PA).
    /// Alle Felder sind nur lesend; editierbar ist ausschließlich <see cref="IsSelected"/>.
    /// </summary>
    public class WipPa
    {
        [Display(Name = "Auswahl")]
        public bool IsSelected { get; set; }

        [Display(Name = "PA")]
        public int? PA { get; set; }

        [Display(Name = "Kunden-Nr.")]
        public int? KundeNr { get; set; }

        [Display(Name = "Kunde")]
        public string KundeName { get; set; }

        [Display(Name = "PA-Bemerkung")]
        public string PABemerkung { get; set; }

        [Display(Name = "PaPi-Bemerkung")]
        public string PaPiBemerkung { get; set; }

        [Display(Name = "Faktu-Notizen 2")]
        public string FaktuNotizen2 { get; set; }

        [Display(Name = "Sperrflag")]
        public short? Sperrflag { get; set; }

        [Display(Name = "Rechnungszeit")]
        public short? Rechnungszeit { get; set; }

        [Display(Name = "Problem")]
        public string Problem { get; set; }

        // StandortInfo.dbo.SafePA.PAVersion ist int NOT NULL, per LEFT JOIN in der SP aber ggf. NULL
        [Display(Name = "PA-Version")]
        public int? PAVersion { get; set; }

        [Display(Name = "Endpreis Summe")]
        [DisplayFormat(DataFormatString = "{0:N2}")]
        public decimal? EndpreisSum { get; set; }

        [Display(Name = "Rückmeldung Schnittstelle")]
        public string RueckmeldungSchnittstelle { get; set; }

        // ---- Zustand in dbo.SageHDR (Sperrvermerk / bereits übergeben) ----

        [Display(Name = "Status")]
        public PaZustand Zustand { get; set; } = PaZustand.Unbekannt;

        /// <summary>Wer den PA gesperrt hat, wenn <see cref="Zustand"/> = GesperrtVonAnderem.</summary>
        public string GesperrtVon { get; set; }
        public DateTime? GesperrtBis { get; set; }

        /// <summary>Erzeugte XML-Datei, wenn <see cref="Zustand"/> = Uebergeben.</summary>
        public string Datei { get; set; }
        public DateTime? UebergebenAm { get; set; }

        /// <summary>Kurzname des Benutzers, der die XML-Datei erzeugt hat (SageHDR.DateiErstelltVon).</summary>
        public string UebergebenVon { get; set; }

        /// <summary>Status und Meldung von Sage aus SageHDR (Status, MessageVonSage) - nur Anzeige, null wenn leer.</summary>
        public WipSageWeb.Data.SageRueckmeldung SageRueckmeldung { get; set; }

        /// <summary>
        /// Ein anderer Benutzer hält eine gültige Sperre auf dem Satz. Bei einem schon übergebenen PA
        /// (<see cref="Zustand"/> = Uebergeben) heißt das: er wird dort gerade neu erstellt.
        /// </summary>
        public bool GesperrtFuerAndere { get; set; }

        /// <summary>
        /// Nur bearbeitbare PAs dürfen ausgewählt und an Sage übergeben werden. Schon übergebene PAs sind
        /// nur gesperrt, solange ein anderer Benutzer sie hält - sonst darf der Haken gesetzt werden,
        /// um die XML-Datei nach Rückfrage neu zu erstellen (siehe <see cref="KannNeuErstelltWerden"/>).
        /// </summary>
        public bool IsReadOnly => Zustand == PaZustand.Unbekannt
                                  || Zustand == PaZustand.GesperrtVonAnderem
                                  || (Zustand == PaZustand.Uebergeben && GesperrtFuerAndere);

        /// <summary>Schon übergeben und frei: der Haken fragt nach, ob die Datei noch einmal erstellt werden soll.</summary>
        public bool KannNeuErstelltWerden => Zustand == PaZustand.Uebergeben && !GesperrtFuerAndere;
    }

    public enum PaZustand
    {
        /// <summary>Sperre konnte nicht geprüft werden (z. B. Datenbankfehler) - nur Lesen.</summary>
        Unbekannt,

        /// <summary>Von diesem Benutzer gesperrt - darf übergeben werden.</summary>
        Bearbeitbar,

        /// <summary>Von einem anderen Benutzer gesperrt - nur Lesen.</summary>
        GesperrtVonAnderem,

        /// <summary>XML-Datei wurde schon erzeugt - nur Lesen, außer der Benutzer lässt sie neu erstellen.</summary>
        Uebergeben
    }
}
