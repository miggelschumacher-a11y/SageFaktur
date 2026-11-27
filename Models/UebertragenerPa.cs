using System;
using System.Collections.Generic;

namespace WipSageWeb.Models
{
    /// <summary>Eine Zeile aus dbo.WIPSageUebertragenePAs_V: ein an Sage übertragener PA mit dem Beleg in Sage.</summary>
    public class UebertragenerPa
    {
        public int PA { get; set; }

        /// <summary>Kundennummer (Spalte Nummer).</summary>
        public long? Nummer { get; set; }

        /// <summary>Kundenkürzel.</summary>
        public string KKuerzel { get; set; }

        public string AnSageGesendetVon { get; set; }
        public DateTime? AnSageGesendetAm { get; set; }
        public string ImportStatus { get; set; }

        /// <summary>Vorgangsnummer in Sage (OfficeLineVorgangsHandle).</summary>
        public int? OfficeLineVorgangsHandle { get; set; }

        /// <summary>Lieferschein in Sage: Datum, Belegjahr, Nummer (Spalten LiefDat, BelegJahrL, LiefNr).</summary>
        public DateTime? LiefDat { get; set; }
        public int? BelegJahrL { get; set; }
        public int? LiefNr { get; set; }

        /// <summary>Rechnung in Sage: Belegart, Datum, Belegjahr, Nummer (Spalten Rechnungsart, RechDat, BelegJahrR, RechNr).</summary>
        public string Rechnungsart { get; set; }
        public DateTime? RechDat { get; set; }
        public int? BelegJahrR { get; set; }
        public int? RechNr { get; set; }

        public int? OfficeLineHandle { get; set; }

        /// <summary>Belegart des Lieferscheins (Spalte "Lieferschein"): aus der Sage-Abfrage, sonst "Lieferschein", wenn die View einen hat.</summary>
        public string Lieferscheinart { get; set; }

        /// <summary>
        /// Die Belege stammen aus der Abfrage an Konfig.SageResultUrl (siehe SageBelegAbfrage.Uebernehmen);
        /// false: Werte aus der View (keine URL hinterlegt oder Abfrage fehlgeschlagen).
        /// </summary>
        public bool SageAbgefragt { get; set; }

        /// <summary>Sage liefert mehr als einen Lieferschein - die Lieferschein-Spalten zeigen "Not unique".</summary>
        public bool LieferscheinNichtEindeutig { get; set; }

        /// <summary>Sage liefert mehr als eine Rechnung - die Rechnungs-Spalten zeigen "Not unique".</summary>
        public bool RechnungNichtEindeutig { get; set; }

        /// <summary>Sage liefert mehr als eine Sammelrechnung - die Rechnungs-Spalten zeigen "Not unique".</summary>
        public bool SammelRechnungNichtEindeutig { get; set; }

        /// <summary>Fehlermeldung der Sage-Abfrage für diesen PA; null, wenn sie geklappt hat oder nicht stattfand.</summary>
        public string SageAbfrageFehler { get; set; }
    }

    /// <summary>Suchfilter der History-Seite. Leere Felder filtern nicht.</summary>
    public class HistoryFilter
    {
        public long? PA { get; set; }

        /// <summary>Kundennummer (Spalte Nummer der View).</summary>
        public long? Kunde { get; set; }

        /// <summary>Vorgangsnummer = OfficeLineVorgangsHandle.</summary>
        public long? Vorgang { get; set; }

        /// <summary>Belegnummer: sucht in Lieferschein-Nr. (LiefNr) und Rechnungs-Nr. (RechNr).</summary>
        public long? BelegNummer { get; set; }

        /// <summary>Belegdatum (nur der Tag zählt): sucht in Lieferdatum (LiefDat) und Rechnungsdatum (RechDat).</summary>
        public DateTime? BelegDatum { get; set; }

        /// <summary>Nur Übertragungen ab diesem Zeitpunkt (aus Konfig.SageHistoryDays, kein Suchfeld).</summary>
        public DateTime? Seit { get; set; }

        /// <summary>Kein Suchfeld gefüllt (<see cref="Seit"/> zählt nicht).</summary>
        public bool IstLeer =>!PA.HasValue && !Kunde.HasValue && !Vorgang.HasValue && !BelegNummer.HasValue && !BelegDatum.HasValue;
    }

    public class HistoryViewModel
    {
        public HistoryFilter Filter { get; set; } = new HistoryFilter();
        public List<UebertragenerPa> Rows { get; set; } = new List<UebertragenerPa>();

        /// <summary>Höchstzahl der angezeigten Zeilen; gibt es mehr Treffer, ist <see cref="MehrTreffer"/> gesetzt.</summary>
        public int MaxZeilen { get; set; }

        /// <summary>Konfig.SageHistoryDays, wenn ohne Suchfeld nur die letzten Tage gezeigt werden; sonst null.</summary>
        public int? LetzteTage { get; set; }
        public bool MehrTreffer { get; set; }

        public string ErrorMessage { get; set; }

        /// <summary>Hinweis, wenn die Sage-Abfrage (Konfig.SageResultUrl) für einzelne PAs fehlgeschlagen ist.</summary>
        public string SageHinweis { get; set; }
    }
}
