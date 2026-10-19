using System;

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

    /// <summary>Wer sperrt: der angemeldete Benutzer mit der Kennung seiner Anmeldung.</summary>
    public sealed class SperrInhaber
    {
        public long UserNr { get; set; }
        public string Kurzname { get; set; }

        /// <summary>Kennung der Anmeldung (wird beim Anmelden neu vergeben).</summary>
        public string Sitzung { get; set; }
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
        public string Datei { get; set; }
        public DateTime? DateiErstelltAm { get; set; }

        /// <summary>Kurzname des Benutzers, der die XML-Datei erzeugt hat (SageHDR.DateiErstelltVon).</summary>
        public string DateiErstelltVon { get; set; }

        /// <summary>Ein anderer Benutzer hält eine noch gültige Sperre (berechnet mit der Uhr des SQL Servers).</summary>
        public bool GesperrtFuerAndere { get; set; }

        /// <summary>Für diesen PA wurde schon eine XML-Datei erzeugt.</summary>
        public bool IstUebergeben => DateiErstelltAm.HasValue;
    }

    /// <summary>Ergebnis, wenn ein PA für das Schreiben seiner XML-Datei beansprucht werden soll.</summary>
    public sealed class ExportAnspruch
    {
        /// <summary>true: Datei darf geschrieben werden, Datei und DateiErstelltAm sind eingetragen.</summary>
        public bool Erteilt { get; set; }

        /// <summary>Gibt es den PA in dbo.SageHDR überhaupt?</summary>
        public bool Vorhanden { get; set; }

        public string GesperrtVon { get; set; }
        public string Datei { get; set; }
        public DateTime? DateiErstelltAm { get; set; }

        /// <summary>Kurzname des Benutzers, der die Datei erzeugt hat.</summary>
        public string DateiErstelltVon { get; set; }
    }

    /// <summary>Ergebnis, wenn ein schon übergebener PA zum erneuten Erstellen seiner XML-Datei freigegeben werden soll.</summary>
    public sealed class NeuErstellenAnspruch
    {
        /// <summary>true: Datei und DateiErstelltAm sind jetzt NULL, der PA ist für den Benutzer gesperrt.</summary>
        public bool Erteilt { get; set; }

        /// <summary>Gibt es den PA in dbo.SageHDR überhaupt?</summary>
        public bool Vorhanden { get; set; }

        /// <summary>Wer den PA gesperrt hat (wenn nicht erteilt, weil ein anderer Benutzer ihn hält).</summary>
        public string GesperrtVon { get; set; }

        /// <summary>Die bisherige Datei (Name ohne Pfad) und ihr Zeitpunkt - vor dem Zurücksetzen.</summary>
        public string AlteDatei { get; set; }
        public DateTime? AlterZeitpunkt { get; set; }
        public string AlterErsteller { get; set; }
        public SageStatus? AlterStatus { get; set; }
        public string AlteMessageVonSage { get; set; }
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

    /// <summary>
    /// Status und Meldung von Sage (Spalten Status und MessageVonSage in SageHDR bzw. SagePos).
    /// Die App setzt "neu" und "XML erzeugt"; "Alles ok in Sage", "Fehler in Sage" und MessageVonSage kommen von Sage.
    /// </summary>
    public sealed class SageRueckmeldung
    {
        public SageStatus? Status { get; set; }
        public string MessageVonSage { get; set; }

        /// <summary>Text zum Status wie in SageHDR.StatusText ("neu", "XML erzeugt", ...).</summary>
        public string StatusText => Status?.Text();

        /// <summary>Hat Sage schon etwas zurückgemeldet (Status 2/3 oder eine Meldung)?</summary>
        public bool HatRueckmeldungVonSage =>
            Status == SageStatus.AllesOkInSage || Status == SageStatus.FehlerInSage || !string.IsNullOrWhiteSpace(MessageVonSage);
    }
}