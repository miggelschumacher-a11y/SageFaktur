using System;
using System.Collections.Generic;

namespace WipSageWeb.Models
{
    /// <summary>
    /// Eine Zeile aus dbo.WIPSage_ProcR_getWIP_PaketAna: eine abzurechnende Position
    /// (Analyse oder Paket) einer Probe (PI) innerhalb eines PA.
    /// </summary>
    public class WipPaketAna
    {
        /// <summary>PA-Nummer als Zahl - darüber wird den ausgewählten PAs zugeordnet.</summary>
        public int? BelegMatchCode { get; set; }

        /// <summary>Text "Intertek-Auftrags-Nr.…" bzw. "Intertek-Order-No.…".</summary>
        public string PA { get; set; }

        public DateTime? PA_Datum { get; set; }
        public DateTime? Lieferdatum { get; set; }

        /// <summary>Debitorenkonto, z. B. "D29375".</summary>
        public string Kunde_Auftraggeber { get; set; }
        public string KundeName_Auftraggeber { get; set; }

        public string PINummer { get; set; }
        public int? USD { get; set; }
        public string KundenAuftrag { get; set; }
        public string Charge1 { get; set; }
        public string Charge2 { get; set; }

        public string ArtikelNR { get; set; }
        public string ArtikelName { get; set; }
        public string PBuDat { get; set; }
        public string FixParaList { get; set; }

        public int? Menge { get; set; }
        public decimal? Endpreis { get; set; }
        public string Konto { get; set; }
        public string KST { get; set; }
        public string LIMSIdentifier { get; set; }

        /// <summary>
        /// Alle Spalten der Zeile genau so, wie die Prozedur sie liefert (Name und Wert, in der Reihenfolge
        /// der Prozedur). Daraus entsteht das XML für Sage - neue Spalten der Prozedur landen ohne
        /// Programmänderung im Export.
        /// </summary>
        public List<KeyValuePair<string, object>> Spalten { get; } = new List<KeyValuePair<string, object>>();
    }
}
