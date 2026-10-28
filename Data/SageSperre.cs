using System;
using System.Linq;

namespace WipSageWeb.Data
{
    /// <summary>
    /// Sperrvermerk in dbo.SageHDR (Spalten GesperrtVon, GesperrtUserNr, GesperrtSitzung, GesperrtBis):
    /// Ein PA, den ein Benutzer geöffnet hat, ist für andere nur lesbar. Beim Schließen des Browsers
    /// löscht die Seite die Sperren sofort. Klappt das nicht (Absturz, Browser meldet das Schließen nicht),
    /// läuft die Sperre nach <see cref="Minuten"/> ab und wird beim nächsten Laden oder Verlängern gelöscht.
    /// </summary>
    public static class SageSperre
    {
        /// <summary>Gültigkeit einer Sperre; die offene Seite verlängert sie jede Minute (wippa.js).</summary>
        public const int Minuten = 3;
    }

    /// <summary>
    /// Kennungen für SageHDR.GesperrtSitzung: "Anmeldung:Seite", je 16 Hex-Zeichen (33 Zeichen, Spalte nvarchar(40)).
    /// Die Anmeldung wird beim Anmelden vergeben, die Seite bei jedem Laden der Liste. So löscht die Freigabe beim
    /// Verlassen einer Seite (auch bei F5 oder in einem zweiten Tab) nur die Sperren genau dieser Seite - nicht die,
    /// die eine gerade neu geladene Seite schon wieder gesetzt hat. Abmelden gibt alle Seiten der Anmeldung frei.
    /// </summary>
    public static class SperrKennung
    {
        public const int Laenge = 16;

        public static string Neu() => Guid.NewGuid().ToString("N").Substring(0, Laenge);

        /// <summary>16 Hex-Zeichen - nur dann wird eine Kennung aus dem Browser übernommen.</summary>
        public static bool IstGueltig(string kennung) =>
            kennung != null && kennung.Length == Laenge && kennung.All(Uri.IsHexDigit);
    }

    /// <summary>Wer sperrt: der angemeldete Benutzer mit der Kennung seiner Anmeldung und der geöffneten Seite.</summary>
    public sealed class SperrInhaber
    {
        public long UserNr { get; set; }
        public string Kurzname { get; set; }

        /// <summary>Kennung der Anmeldung (wird beim Anmelden neu vergeben).</summary>
        public string Anmeldung { get; set; }

        /// <summary>Kennung der geöffneten Seite (wird bei jedem Laden der Liste neu vergeben).</summary>
        public string Seite { get; set; }

        /// <summary>Wert für SageHDR.GesperrtSitzung: "Anmeldung:Seite".</summary>
        public string Sitzung => Anmeldung + ":" + Seite;
    }

    /// <summary>Zustand eines PA in dbo.SageHDR nach dem Sperren.</summary>
    public sealed class SageHdrZustand
    {
        public int PA { get; set; }
        public string GesperrtVon { get; set; }
        /// <summary>
        /// Ablaufzeit der Sperre. Bis zu diesem Zeitpunkt gehört der PA dem Benutzer, der ihn gesperrt hat;
        /// danach gilt er wieder als frei.
        /// </summary>
        /// <remarks>
        /// Eine Web-App erfährt nicht zuverlässig, wann jemand sie verlässt. Stürzt der Browser ab, fährt der
        /// Rechner herunter oder kommt die Abmeldung beim Schließen nicht an, wird die Sperre nie aktiv gelöscht.
        /// Ohne Ablaufzeit blieben solche PAs für alle anderen dauerhaft gesperrt, bis jemand die Sperre von Hand
        /// aus SageHDR entfernt.
        /// <para>So arbeitet die App damit:</para>
        /// <list type="bullet">
        /// <item><description>Beim Sperren: GesperrtBis wird auf jetzt plus 3 Minuten gesetzt.</description></item>
        /// <item><description>Solange die Seite offen ist: Sie meldet sich jede Minute beim Server und schiebt
        /// GesperrtBis wieder 3 Minuten nach vorn. Eine aktive Sperre läuft so nie ab.</description></item>
        /// <item><description>Wenn die Seite weg ist: Die Meldungen bleiben aus, und spätestens 3 Minuten später
        /// ist GesperrtBis überschritten.</description></item>
        /// <item><description>Prüfung, ob ein PA gesperrt ist: Ein PA gilt nur dann als von einem anderen gesperrt,
        /// wenn GesperrtBis noch in der Zukunft liegt. Abgelaufene Sperren zählen nicht, auch wenn die übrigen
        /// Felder noch gefüllt sind.</description></item>
        /// <item><description>Aufräumen: Bei jedem Laden der Liste und bei jeder Verlängerung löscht der Server
        /// alle abgelaufenen Sperren aus SageHDR.</description></item>
        /// </list>
        /// </remarks>
        public DateTime? GesperrtBis { get; set; }

        /// <summary>SageHDR.Status (Enum SageStatus).</summary>
        public SageStatus? Status { get; set; }

        /// <summary>Wann und von wem der PA zuletzt an Sage gesendet wurde (SageHDR.AnSageGesendetAm/-Von).</summary>
        public DateTime? AnSageGesendetAm { get; set; }
        public string AnSageGesendetVon { get; set; }

        /// <summary>Ein anderer Benutzer hält eine noch gültige Sperre (berechnet mit der Uhr des SQL Servers).</summary>
        public bool GesperrtFuerAndere { get; set; }

        /// <summary>Schon an Sage übergeben: Status "XML erzeugt" (gesendet bzw. wird gerade gesendet) oder "Alles ok in Sage".</summary>
        public bool IstUebergeben => SageStatusText.IstUebergeben(Status);
    }

    /// <summary>Ergebnis, wenn ein PA zum Senden an Sage beansprucht werden soll.</summary>
    public sealed class ExportAnspruch
    {
        /// <summary>true: der PA darf gesendet werden; Status ist "XML erzeugt", AnSageGesendetAm/-Von sind gesetzt.</summary>
        public bool Erteilt { get; set; }

        /// <summary>Gibt es den PA in dbo.SageHDR überhaupt?</summary>
        public bool Vorhanden { get; set; }

        public string GesperrtVon { get; set; }

        /// <summary>Stand in SageHDR nach dem Versuch (bei nicht erteilt: warum).</summary>
        public SageStatus? Status { get; set; }
        public DateTime? AnSageGesendetAm { get; set; }
        public string AnSageGesendetVon { get; set; }

        /// <summary>Werte vor dem Beanspruchen - zum Wiederherstellen, wenn das Senden scheitert.</summary>
        public SageStatus? AlterStatus { get; set; }
        public DateTime? AltGesendetAm { get; set; }
        public string AltGesendetVon { get; set; }
    }

    /// <summary>Ergebnis, wenn ein schon übergebener PA zur erneuten Übergabe an Sage freigegeben werden soll.</summary>
    public sealed class NeuErstellenAnspruch
    {
        /// <summary>true: Status ist wieder "neu", der PA ist für den Benutzer gesperrt.</summary>
        public bool Erteilt { get; set; }

        /// <summary>Gibt es den PA in dbo.SageHDR überhaupt?</summary>
        public bool Vorhanden { get; set; }

        /// <summary>Wer den PA gesperrt hat (wenn nicht erteilt, weil ein anderer Benutzer ihn hält).</summary>
        public string GesperrtVon { get; set; }
    }

    /// <summary>Eine Zeile für dbo.SagePos: eine übergebene Position (Analyse oder Paket) eines PA.</summary>
    public sealed class SagePosZeile
    {
        /// <summary>PI-Nummer ohne das Präfix "PI" (wie WorkInProgress.PI), nvarchar(10).</summary>
        public string PI { get; set; }
        public byte Ulfd { get; set; }

        /// <summary>Artikelnummer wie von der Prozedur geliefert (z. B. 003053 oder P17), nvarchar(10).</summary>
        public string ArtikelNr { get; set; }

        /// <summary>Artikelname, auf 40 Zeichen gekürzt (Länge der Spalte).</summary>
        public string ArtikelName { get; set; }
    }

    /// <summary>OfficeLineHandle einer Position aus der Antwort von Sage, für dbo.SagePos (Zuordnung über PI und Ulfd).</summary>
    public sealed class SagePosHandle
    {
        /// <summary>PI-Nummer ohne das Präfix "PI" (wie SagePos.PI).</summary>
        public string PI { get; set; }
        public byte Ulfd { get; set; }
        public int OfficeLineHandle { get; set; }
    }

    /// <summary>
    /// Status und Meldung von Sage (Spalten Status und MessageVonSage in SageHDR bzw. SagePos).
    /// Die App setzt "neu" und "XML erzeugt"; "Alles ok in Sage", "Fehler in Sage" und MessageVonSage kommen von Sage.
    /// </summary>
    public sealed class SageRueckmeldung
    {
        public SageStatus? Status { get; set; }
        public string MessageVonSage { get; set; }

        /// <summary>
        /// OfficeLineHandle von Sage: interne Nummer des Auftrags (SageHDR) bzw. der Position (SagePos) in Sage
        /// (Erklärung der Handles siehe <see cref="SageAntwort"/>).
        /// </summary>
        public int? OfficeLineHandle { get; set; }

        /// <summary>
        /// OfficeLineVorgangsHandle (nur SageHDR): interne Nummer des Vorgangs in Sage, zu dem der Auftrag gehört -
        /// gleich für die ganze Belegkette Auftrag, Lieferschein, Rechnung (siehe <see cref="SageAntwort"/>).
        /// </summary>
        public int? OfficeLineVorgangsHandle { get; set; }

        /// <summary>Text zum Status wie in SageHDR.StatusText ("neu", "XML erzeugt", ...).</summary>
        public string StatusText => Status?.Text();

        /// <summary>Hat Sage schon etwas zurückgemeldet (Status 2/3 oder eine Meldung)?</summary>
        public bool HatRueckmeldungVonSage =>
            Status == SageStatus.AllesOkInSage || Status == SageStatus.FehlerInSage || !string.IsNullOrWhiteSpace(MessageVonSage);
    }
}