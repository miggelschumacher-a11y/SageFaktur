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

        public int? Belegjahr { get; set; }
        public DateTime? BelegDatum { get; set; }
        public int? BelegNummer { get; set; }
        public int? Mandant { get; set; }
        public string BelegArt { get; set; }
        public int? OfficeLineHandle { get; set; }
    }

    /// <summary>Suchfilter der History-Seite. Leere Felder filtern nicht.</summary>
    public class HistoryFilter
    {
        public long? PA { get; set; }

        /// <summary>Kundennummer (Spalte Nummer der View).</summary>
        public long? Kunde { get; set; }

        /// <summary>Vorgangsnummer = OfficeLineVorgangsHandle.</summary>
        public long? Vorgang { get; set; }

        public long? BelegNummer { get; set; }

        /// <summary>Belegdatum (nur der Tag zählt).</summary>
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
    }
}
