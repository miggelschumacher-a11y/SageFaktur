using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using WipSageWeb.Models;

namespace WipSageWeb.Data
{
    /// <summary>Fehler, der dem Benutzer als Popup gezeigt wird (z. B. kein gültiger Export-Ordner).</summary>
    public sealed class SageExportException : Exception
    {
        public SageExportException(string message) : base(message) { }
        public SageExportException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class SageExportResult
    {
        public string Folder { get; set; }
        public List<string> Files { get; } = new List<string>();

        /// <summary>Ausgewählte PAs, zu denen die Prozedur keine Positionen geliefert hat.</summary>
        public List<int> PAsWithoutPositions { get; } = new List<int>();
    }

    /// <summary>
    /// Ein vorbereiteter Export: Ordner ist geprüft, die PAs stehen fest.
    /// Danach wird PA für PA gelesen und geschrieben - so kann die Seite den echten Fortschritt anzeigen.
    /// </summary>
    public sealed class SageExportJob
    {
        public string Folder { get; set; }
        public string Timestamp { get; set; }
        public List<int> PAs { get; } = new List<int>();

        /// <summary>Benutzer, der den Export gestartet hat - nur er darf die Dateien seiner PAs schreiben.</summary>
        public SperrInhaber Inhaber { get; set; }
    }

    public interface ISageExportService
    {
        /// <summary>Prüft Konfig.SageExportOrdner und legt die PAs des Exports fest.</summary>
        Task<SageExportJob> PrepareAsync(ICollection<int> selectedPAs, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Liest die Positionen eines PA und schreibt seine XML-Datei; null, wenn der PA keine Positionen hat.</summary>
        Task<string> ExportOneAsync(SageExportJob job, int pa, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Schreibt je PA eine XML-Datei in Konfig.SageExportOrdner.</summary>
        Task<SageExportResult> ExportAsync(ICollection<int> selectedPAs, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Gibt einen schon übergebenen PA zum erneuten Erstellen frei: SageHDR.Datei und DateiErstelltAm werden NULL,
        /// der Satz ist für den Benutzer gesperrt, und die bisherige XML-Datei wird - falls sie noch im Export-Ordner
        /// liegt - gelöscht. Liefert den Namen der gelöschten Datei, null wenn keine mehr da war.
        /// Lässt sich die Datei nicht löschen, bleibt alles wie es war und es wird eine SageExportException geworfen.
        /// </summary>
        Task<string> NeuErstellenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));
    }

    /// <summary>
    /// Stellt die ausgewählten PAs als XML für Sage bereit: je PA eine Datei im Order-Format
    /// (Beispiel "XML-Struktur-#2.xml"), gefüllt aus dbo.WIPSage_ProcR_getWIP_PaketAnaEx.
    /// </summary>
    public sealed class SageExportService : ISageExportService
    {

        private readonly IWipSageRepository _repository;

        public SageExportService(IWipSageRepository repository)
        {
            _repository = repository;
        }

        public async Task<SageExportJob> PrepareAsync(ICollection<int> selectedPAs, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Erst den Ordner prüfen - ohne gültiges Ziel wird nichts gelesen oder geschrieben
            var folder = (await _repository.GetSageExportOrdnerAsync(cancellationToken) ?? "").Trim();
            if (folder.Length == 0)
            {
                throw new SageExportException(
                    "In der Konfiguration ist kein Export-Ordner für Sage hinterlegt (Tabelle Konfig, Feld SageExportOrdner).");
            }
            if (!Directory.Exists(folder))
            {
                throw new SageExportException(
                    $"Der Export-Ordner für Sage ist nicht gültig oder nicht erreichbar:\n{folder}\n(Tabelle Konfig, Feld SageExportOrdner)");
            }

            var job = new SageExportJob
            {
                Folder = folder,
                Timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture),
                Inhaber = inhaber
            };
            job.PAs.AddRange(selectedPAs.Distinct().OrderBy(p => p));
            return job;
        }

        public async Task<string> ExportOneAsync(SageExportJob job, int pa, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!job.PAs.Contains(pa))
            {
                throw new SageExportException($"PA {pa} gehört nicht zu diesem Export.");
            }

            // Die Prozedur liefert die Positionen genau dieses PA
            var paPositions = (await _repository.GetWipPaketAnaAsync(pa, cancellationToken))
                .Where(p => p.BelegMatchCode == pa)
                .ToList();
            if (paPositions.Count == 0)
            {
                return null;
            }

            var fileName = $"WIPSage_PA_{pa.ToString(CultureInfo.InvariantCulture)}_{job.Timestamp}.xml";
            var path = Path.Combine(job.Folder, fileName);

            // Erst in SageHDR beanspruchen, dann schreiben: so entsteht für jeden PA höchstens eine Datei,
            // auch wenn zwei Benutzer gleichzeitig exportieren
            var anspruch = await _repository.ExportBeanspruchenAsync(pa, job.Inhaber, cancellationToken);
            if (!anspruch.Erteilt)
            {
                if (anspruch.DateiErstelltAm.HasValue)
                {
                    throw new SageExportException(
                        $"Für PA {pa} wurde bereits am {anspruch.DateiErstelltAm.Value:dd.MM.yyyy} um {anspruch.DateiErstelltAm.Value:HH:mm} Uhr" + (string.IsNullOrEmpty(anspruch.DateiErstelltVon) ? "" : $" von {anspruch.DateiErstelltVon}") + $" eine XML-Datei erzeugt:\n{anspruch.Datei}");
                }
                if (!anspruch.Vorhanden)
                {
                    throw new SageExportException($"Für PA {pa} gibt es keinen Eintrag in SageHDR. Bitte die Seite neu laden.");
                }
                throw new SageExportException($"PA {pa} ist von {anspruch.GesperrtVon} gesperrt und kann nicht übergeben werden.");
            }

            try
            {
                // Vor dem Erstellen der XML-Datei: SagePos für diesen PA löschen und mit den Positionen neu anlegen
                await _repository.SagePosErsetzenAsync(pa, SagePosZeilen(pa, paPositions), cancellationToken);

                var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
                using (var writer = XmlWriter.Create(path, settings))
                {
                    BuildDocument(pa, paPositions).Save(writer);
                }
            }
            catch (Exception ex)
            {
                // SagePos oder Datei konnte nicht geschrieben werden -> Anspruch zurücknehmen, damit der PA wieder exportierbar ist
                await _repository.ExportZuruecknehmenAsync(pa, job.Inhaber, CancellationToken.None);
                if (ex is SageExportException)
                {
                    throw;
                }
                if (ex is SqlException)
                {
                    throw new SageExportException($"Die Positionen von PA {pa} konnten nicht in SagePos gespeichert werden:\n{ex.Message}", ex);
                }
                if (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new SageExportException(
                        $"In den Export-Ordner für Sage kann nicht geschrieben werden:\n{job.Folder}\n{ex.Message}", ex);
                }
                throw;
            }

            // Datei liegt im Ordner: jetzt den Dateinamen ohne Pfad in SageHDR.Datei eintragen
            await _repository.ExportAbschliessenAsync(pa, job.Inhaber, fileName, CancellationToken.None);
            return fileName;
        }

        public async Task<SageExportResult> ExportAsync(ICollection<int> selectedPAs, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            var job = await PrepareAsync(selectedPAs, inhaber, cancellationToken);
            var result = new SageExportResult { Folder = job.Folder };
            foreach (var pa in job.PAs)
            {
                var fileName = await ExportOneAsync(job, pa, cancellationToken);
                if (fileName == null)
                {
                    result.PAsWithoutPositions.Add(pa);
                }
                else
                {
                    result.Files.Add(fileName);
                }
            }
            return result;
        }

        public async Task<string> NeuErstellenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Erst in SageHDR beanspruchen (Datei/DateiErstelltAm = NULL, Satz gesperrt), dann die Datei löschen:
            // so löscht niemand eine Datei, deren PA ein anderer Benutzer gerade hält
            var anspruch = await _repository.ExportZuruecksetzenAsync(pa, inhaber, cancellationToken);
            if (!anspruch.Erteilt)
            {
                if (!anspruch.Vorhanden)
                {
                    throw new SageExportException($"Für PA {pa} gibt es keinen Eintrag in SageHDR. Bitte die Seite neu laden.");
                }
                if (!string.IsNullOrEmpty(anspruch.GesperrtVon))
                {
                    throw new SageExportException($"PA {pa} ist von {anspruch.GesperrtVon} gesperrt und kann nicht neu erstellt werden.");
                }
                throw new SageExportException($"Für PA {pa} wurde noch keine XML-Datei erzeugt - er kann ganz normal übergeben werden. Bitte die Seite neu laden.");
            }

            // Ohne Dateiname (Export war unterbrochen) gibt es nichts zu löschen
            var datei = Path.GetFileName((anspruch.AlteDatei ?? "").Trim());
            if (datei.Length == 0)
            {
                return null;
            }

            try
            {
                var folder = (await _repository.GetSageExportOrdnerAsync(cancellationToken) ?? "").Trim();
                if (folder.Length == 0 || !Directory.Exists(folder))
                {
                    throw new SageExportException(
                        "Der Export-Ordner für Sage ist nicht gültig oder nicht erreichbar" +
                        (folder.Length == 0 ? "." : $":\n{folder}") +
                        "\nOhne ihn lässt sich die alte XML-Datei nicht löschen (Tabelle Konfig, Feld SageExportOrdner).");
                }

                var path = Path.Combine(folder, datei);
                if (!File.Exists(path))
                {
                    return null;   // schon weg (z. B. von Sage abgeholt) - nichts zu löschen
                }
                File.Delete(path);
                return datei;
            }
            catch (Exception ex)
            {
                // Alte Datei liegt noch da -> Zustand zurückholen, sonst käme sie neben der neuen Datei zu Sage
                await _repository.ExportWiederherstellenAsync(pa, inhaber, anspruch.AlteDatei, anspruch.AlterZeitpunkt, anspruch.AlterErsteller, anspruch.AlterStatus, anspruch.AlteMessageVonSage, CancellationToken.None);
                if (ex is SageExportException)
                {
                    throw;
                }
                if (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new SageExportException($"Die alte XML-Datei {datei} konnte nicht gelöscht werden:\n{ex.Message}", ex);
                }
                throw;
            }
        }

        // Positionen des PA für dbo.SagePos. PI ohne das Präfix "PI" (die Prozedur liefert "PI" + WorkInProgress.PI),
        // Artikelname auf die 40 Zeichen der Spalte gekürzt. Fehlt PI, Ulfd oder Artikelnummer, bricht der Export ab.
        private static List<SagePosZeile> SagePosZeilen(int pa, List<WipPaketAna> positions)
        {
            var zeilen = new List<SagePosZeile>();
            foreach (var p in positions)
            {
                var pi = (p.PINummer ?? "").Trim();
                if (pi.StartsWith("PI", StringComparison.OrdinalIgnoreCase))
                {
                    pi = pi.Substring(2);
                }
                var artikelNr = (p.ArtikelNR ?? "").Trim();
                if (pi.Length == 0 || !p.Ulfd.HasValue || artikelNr.Length == 0)
                {
                    throw new SageExportException(
                        $"PA {pa}: Eine Position hat keine PI-Nummer, keine Ulfd oder keine Artikelnummer ({p.LIMSIdentifier}).\nSie kann nicht in SagePos gespeichert werden.");
                }
                if (pi.Length > 10 || artikelNr.Length > 10 || p.Ulfd.Value < 0 || p.Ulfd.Value > 255)
                {
                    throw new SageExportException(
                        $"PA {pa}: PI-Nummer \"{pi}\", Ulfd {p.Ulfd} oder Artikelnummer \"{artikelNr}\" passt nicht in SagePos.");
                }
                var name = (p.ArtikelName ?? "").Trim();
                zeilen.Add(new SagePosZeile
                {
                    PI = pi,
                    Ulfd = (byte)p.Ulfd.Value,
                    ArtikelNr = artikelNr,
                    ArtikelName = name.Length > 40 ? name.Substring(0, 40) : name
                });
            }
            return zeilen;
        }

        // ------------------------------------------------------------------------------------------
        // Aufbau der XML-Datei nach dem Beispiel "XML-Struktur-#2.xml" (Order-Format der Sage-Schnittstelle).
        // Die Werte kommen aus dbo.WIPSage_ProcR_getWIP_PaketAnaEx; Reihenfolge und Elementnamen wie im Beispiel.
        // Felder ohne Wert aus der Prozedur werden wie im Beispiel als i:nil="true" geschrieben.
        //
        //   Order
        //     DiscountPercent      0
        //     Kopftext             fester Text: USD = 0 -> englisch, sonst deutsch
        //     Kto                  Kunde_Auftraggeber
        //     LIMSIdentifier       BelegMatchCode
        //     Matchcode            BelegMatchCode
        //     OfficeLineHandle     nil
        //     Samples
        //       Sample             je Position (Zeile der Prozedur) ein Sample
        //         LIMSIdentifier   LIMSIdentifier
        //         Name             "Your reference:" (USD = 0) bzw. "Ihre Probe:" + Charge1 + Charge2
        //         Packages
        //           Package
        //             Bezeichnung        ArtikelNR
        //             Bezeichnung2       nil
        //             Dimensionstext     nil
        //             DiscountPercent    0
        //             EBITCode           nil
        //             Erloeskonto        Konto, fünfstellig mit führenden Nullen
        //             Kostenstelle       KST
        //             Kostentraeger      nil
        //             LIMSIdentifier     LIMSIdentifier
        //             Langtext           PBuDat und ArtikelName in einer Zeile
        //             Menge              Menge
        //             OfficeLineHandle   nil
        //             OfficeLineItem
        //               Artikelnummer    ArtikelNR
        //             Price              Endpreis
        //     Wkz                  USD = 0 -> "USD", sonst "EUR"
        // ------------------------------------------------------------------------------------------
        private static readonly XNamespace Ns = "http://schemas.datacontract.org/2004/07/appulsive.Intertek.LIMSService";
        private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

        private const string KopftextEnglisch = "We thank you very much for your order and charge the services rendered as follows:";
        private const string KopftextDeutsch = "Wir danken Ihnen für den uns erteilten Auftrag und erlauben uns, folgende Services in Rechnung zu stellen:";
        private const string ProbeEnglisch = "Your reference:";
        private const string ProbeDeutsch = "Ihre Probe:";

        private static XDocument BuildDocument(int pa, List<WipPaketAna> positions)
        {
            var head = positions[0];
            var englisch = IstUsdNull(head);
            var matchcode = FormatValue(GetColumn(head, "BelegMatchCode"));

            var order = new XElement(Ns + "Order",
                new XAttribute(XNamespace.Xmlns + "i", Xsi),
                Element("DiscountPercent", "0"),
                Element("Kopftext", englisch ? KopftextEnglisch : KopftextDeutsch),
                Element("Kto", FormatValue(GetColumn(head, "Kunde_Auftraggeber"))),
                Element("LIMSIdentifier", matchcode),
                Element("Matchcode", matchcode),
                Element("OfficeLineHandle", null),
                new XElement(Ns + "Samples", positions.Select(p => BuildSample(p, englisch, matchcode))),
                Element("Wkz", Waehrung(head)));

            return new XDocument(new XDeclaration("1.0", "utf-8", null), order);
        }

        private static XElement BuildSample(WipPaketAna p, bool englisch, string matchcode)
        {
            return new XElement(Ns + "Sample",
                Element("LIMSIdentifier", FormatValue(GetColumn(p, "LIMSIdentifier"))),
                Element("Name", SampleName(p, englisch)),
                new XElement(Ns + "Packages",
                    new XElement(Ns + "Package",
                        Element("Bezeichnung", FormatValue(GetColumn(p, "ArtikelNR"))),
                        Element("Bezeichnung2", null),
                        Element("Dimensionstext", null),
                        Element("DiscountPercent", "0"),
                        Element("EBITCode", null),
                        Element("Erloeskonto", Erloeskonto(p)),
                        Element("Kostenstelle", FormatValue(GetColumn(p, "KST"))),
                        Element("Kostentraeger", null),
                        Element("LIMSIdentifier", FormatValue(GetColumn(p, "LIMSIdentifier"))),
                        Element("Langtext", Langtext(p)),
                        Element("Menge", FormatValue(GetColumn(p, "Menge"))),
                        Element("OfficeLineHandle", null),
                        new XElement(Ns + "OfficeLineItem",
                            Element("Artikelnummer", FormatValue(GetColumn(p, "ArtikelNR")))),
                        Element("Price", FormatValue(GetColumn(p, "Endpreis"))))));
        }

        // Leere Werte wie im Beispiel als i:nil="true"
        private static XElement Element(string name, string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? new XElement(Ns + name, new XAttribute(Xsi + "nil", "true"))
                : new XElement(Ns + name, value);
        }

        // "Your reference: <Charge1> <Charge2>" bzw. "Ihre Probe: ..."; ohne Charge bleibt das Element leer
        private static string SampleName(WipPaketAna p, bool englisch)
        {
            var chargen = new[] { FormatValue(GetColumn(p, "Charge1")), FormatValue(GetColumn(p, "Charge2")) }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            return chargen.Count == 0 ? null : (englisch ? ProbeEnglisch : ProbeDeutsch) + " " + string.Join(" ", chargen);
        }

        // Erlöskonto fünfstellig mit führenden Nullen (4000 -> 04000); was keine Zahl ist, bleibt unverändert
        private static string Erloeskonto(WipPaketAna p)
        {
            var konto = FormatValue(GetColumn(p, "Konto"));
            if (string.IsNullOrWhiteSpace(konto))
            {
                return null;
            }
            konto = konto.Trim();
            return konto.All(char.IsDigit) && konto.Length < 5 ? konto.PadLeft(5, '0') : konto;
        }

        // PBuDat und ArtikelName in einer Zeile wie im Beispiel; Zeilenumbrüche aus PBuDat werden zu Leerzeichen
        private static string Langtext(WipPaketAna p)
        {
            var teile = new[] { FormatValue(GetColumn(p, "PBuDat")), FormatValue(GetColumn(p, "ArtikelName")) }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s*[\r\n]+\s*", " "));
            var text = string.Join(" ", teile);
            return text.Length == 0 ? null : text;
        }

        // Spalte USD = 0 -> englische Texte. Jeder andere Wert (auch NULL) -> deutsche Texte.
        private static bool IstUsdNull(WipPaketAna position) => UsdWert(position) == 0m;

        // Währungskürzel für Sage: Spalte USD = 0 -> "USD", jeder andere Wert (auch NULL) -> "EUR"
        private static string Waehrung(WipPaketAna position) => UsdWert(position) == 0m ? "USD" : "EUR";

        private static decimal? UsdWert(WipPaketAna position)
        {
            var usd = GetColumn(position, "USD");
            return usd != null && decimal.TryParse(Convert.ToString(usd, CultureInfo.InvariantCulture),
                       NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : (decimal?)null;
        }

        // Sucht die Spalte ohne Rücksicht auf Groß-/Kleinschreibung. Fehlt sie im Ergebnis der Prozedur
        // (umbenannt oder entfernt), bricht der Export mit einer klaren Meldung ab, statt ein Feld leer zu lassen.
        private static object GetColumn(WipPaketAna position, string column)
        {
            foreach (var spalte in position.Spalten)
            {
                if (string.Equals(spalte.Key, column, StringComparison.OrdinalIgnoreCase))
                {
                    return spalte.Value;
                }
            }
            throw new SageExportException(
                $"Die Spalte \"{column}\" fehlt im Ergebnis von WIPSage_ProcR_getWIP_PaketAnaEx.\nOhne sie kann die XML-Datei für Sage nicht erzeugt werden.");
        }

        // Datum wie in der Prozedur selbst (Format 104): 20.09.2026. Zahlen mit Dezimalpunkt. NULL ergibt ein leeres Element.
        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return null;
            }
            if (value is DateTime date)
            {
                return date.TimeOfDay == TimeSpan.Zero
                    ? date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                    : date.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            }
            if (value is bool flag)
            {
                return flag ? "1" : "0";
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        }
    }
}
