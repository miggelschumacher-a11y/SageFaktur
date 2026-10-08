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
    }
}
