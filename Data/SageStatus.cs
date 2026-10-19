using System.ComponentModel.DataAnnotations;

namespace WipSageWeb.Data
{
    /// <summary>
    /// Werte der Spalte Status in dbo.SageHDR und dbo.SagePos (tinyint).
    /// Den Text dazu liefert in SageHDR auch die berechnete Spalte StatusText.
    /// </summary>
    public enum SageStatus : byte
    {
        /// <summary>Satz angelegt, noch keine XML-Datei.</summary>
        [Display(Name = "neu")]
        Neu = 0,

        /// <summary>Die App hat die XML-Datei für Sage erzeugt.</summary>
        [Display(Name = "XML erzeugt")]
        XmlErzeugt = 1,

        /// <summary>Sage hat die Datei ohne Fehler verarbeitet (Rückmeldung von Sage).</summary>
        [Display(Name = "Alles ok in Sage")]
        AllesOkInSage = 2,

        /// <summary>Sage hat beim Verarbeiten einen Fehler gemeldet (Text in MessageVonSage).</summary>
        [Display(Name = "Fehler in Sage")]
        FehlerInSage = 3
    }

    public static class SageStatusText
    {
        /// <summary>Anzeigetext wie in SageHDR.StatusText; unbekannte Werte als Zahl.</summary>
        public static string Text(this SageStatus status)
        {
            switch (status)
            {
                case SageStatus.Neu: return "neu";
                case SageStatus.XmlErzeugt: return "XML erzeugt";
                case SageStatus.AllesOkInSage: return "Alles ok in Sage";
                case SageStatus.FehlerInSage: return "Fehler in Sage";
                default: return "Status " + ((byte)status).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
