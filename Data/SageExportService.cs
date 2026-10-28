using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using WipSageWeb.Models;

namespace WipSageWeb.Data
{
    /// <summary>Fehler, der dem Benutzer als Popup gezeigt wird (z. B. keine gültige Adresse der Sage-Schnittstelle).</summary>
    public sealed class SageExportException : Exception
    {
        public SageExportException(string message) : base(message) { }
        public SageExportException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Wie die Übergabe eines PA an Sage ausgegangen ist.</summary>
    public enum SageExportErgebnis
    {
        /// <summary>Sage hat den Auftrag angelegt.</summary>
        Uebergeben,

        /// <summary>Sage hat den Auftrag abgelehnt (Meldung von Sage); der PA kann erneut übergeben werden.</summary>
        Abgelehnt,

        /// <summary>Gesendet, aber keine Antwort von Sage - in Sage prüfen, ob der Auftrag angelegt wurde.</summary>
        KeineAntwort,

        /// <summary>Die Prozedur hat keine abrechenbaren Positionen geliefert; nichts gesendet.</summary>
        KeinePositionen,

        /// <summary>
        /// Fehler, der nur diesen PA betrifft (z. B. HTTP 413 - Auftrag zu groß, fehlerhafte Positionsdaten, inzwischen
        /// von einem anderen Benutzer gesperrt): der PA wird übersprungen und bleibt übergebbar, der Export läuft weiter.
        /// </summary>
        NichtUebergeben
    }

    /// <summary>Ergebnis der Übergabe eines PA.</summary>
    public sealed class SageExportSchritt
    {
        public int PA { get; set; }
        public SageExportErgebnis Ergebnis { get; set; }

        /// <summary>OfficeLineHandle des in Sage angelegten Auftrags (nur Uebergeben; siehe <see cref="SageAntwort"/>).</summary>
        public int? OfficeLineHandle { get; set; }

        /// <summary>
        /// OfficeLineVorgangsHandle des Vorgangs in Sage, zu dem der Auftrag gehört (nur Uebergeben; siehe <see cref="SageAntwort"/>).
        /// </summary>
        public int? OfficeLineVorgangsHandle { get; set; }

        /// <summary>Meldung von Sage (Abgelehnt, auch Uebergeben falls Sage eine liefert) bzw. Hinweis (KeineAntwort, NichtUebergeben).</summary>
        public string Meldung { get; set; }
    }

    public sealed class SageExportResult
    {
        public string Url { get; set; }
        public List<SageExportSchritt> Schritte { get; } = new List<SageExportSchritt>();
    }

    /// <summary>
    /// Ein vorbereiteter Export: die Adresse der Sage-Schnittstelle ist geprüft, die PAs stehen fest.
    /// Danach wird PA für PA gelesen und gesendet - so kann die Seite den echten Fortschritt anzeigen.
    /// </summary>
    public sealed class SageExportJob
    {
        public string Url { get; set; }
        public List<int> PAs { get; } = new List<int>();

        /// <summary>Konfig.SageTest: Testbetrieb - Artikelnummer in den XML-Daten immer die Test-Artikelnummer.</summary>
        public bool Test { get; set; }

        /// <summary>Benutzer, der den Export gestartet hat - nur er darf seine PAs übergeben.</summary>
        public SperrInhaber Inhaber { get; set; }
    }

    public interface ISageExportService
    {
        /// <summary>
        /// Prüft die Adresse der Sage-Schnittstelle (Konfig.SageTestExportURL bzw. SageReleaseExportURL, je nach
        /// Konfig.SageTest) und legt die PAs des Exports fest.
        /// </summary>
        Task<SageExportJob> PrepareAsync(ICollection<int> selectedPAs, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Liest die Positionen eines PA und sendet sie als Auftrag an Sage.</summary>
        Task<SageExportSchritt> ExportOneAsync(SageExportJob job, int pa, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Sendet je PA einen Auftrag an die Sage-Schnittstelle (Adresse aus Konfig, siehe PrepareAsync).</summary>
        Task<SageExportResult> ExportAsync(ICollection<int> selectedPAs, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Gibt einen schon übergebenen PA zur erneuten Übergabe frei: SageHDR.Status wird "neu",
        /// der Satz ist für den Benutzer gesperrt.
        /// </summary>
        Task NeuErstellenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));
    }

    /// <summary>
    /// Übergibt die ausgewählten PAs an Sage: je PA ein Auftrag im Order-Format (Beispiel "XML-Struktur-#2.xml"),
    /// gefüllt aus dbo.WIPSage_ProcR_getWIP_PaketAnaEx und per POST an die Sage-Schnittstelle gesendet - im Testbetrieb
    /// (Konfig.SageTest = true) an Konfig.SageTestExportURL, sonst an Konfig.SageReleaseExportURL.
    /// </summary>
    public sealed class SageExportService : ISageExportService
    {
        private const string KeineAntwortHinweis =
            "Keine Antwort von Sage - bitte in Sage prüfen, ob der Auftrag angelegt wurde.";

        /// <summary>Artikelnummer, die bei Konfig.SageTest = true in allen Positionen an Sage geht.</summary>
        public const string TestArtikelnummer = "04005019";

        private readonly IWipSageRepository _repository;
        private readonly ISageSchnittstelle _sage;

        public SageExportService(IWipSageRepository repository, ISageSchnittstelle sage)
        {
            _repository = repository;
            _sage = sage;
        }

        public async Task<SageExportJob> PrepareAsync(ICollection<int> selectedPAs, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Erst die Adresse prüfen - ohne gültiges Ziel wird nichts gelesen oder gesendet
            var konfig = await _repository.GetSageKonfigAsync(cancellationToken);
            var url = (konfig.ExportUrl ?? "").Trim();
            if (url.Length == 0)
            {
                throw new SageExportException(
                    $"In der Konfiguration ist keine Adresse der Sage-Schnittstelle hinterlegt (Tabelle Konfig, Feld {konfig.ExportUrlFeld}" +
                    (konfig.Test ? ", weil SageTest = true)." : ", weil SageTest = false)."));
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new SageExportException(
                    $"Die Adresse der Sage-Schnittstelle ist keine gültige http- oder https-Adresse:\n{url}\n(Tabelle Konfig, Feld {konfig.ExportUrlFeld})");
            }

            var job = new SageExportJob
            {
                Url = url,
                Test = konfig.Test,
                Inhaber = inhaber
            };
            job.PAs.AddRange(selectedPAs.Distinct().OrderBy(p => p));
            return job;
        }

        public async Task<SageExportSchritt> ExportOneAsync(SageExportJob job, int pa, CancellationToken cancellationToken = default(CancellationToken))
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
                return new SageExportSchritt { PA = pa, Ergebnis = SageExportErgebnis.KeinePositionen };
            }

            // Erst in SageHDR beanspruchen (Status "XML erzeugt", AnSageGesendetAm/-Von), dann senden:
            // so geht jeder PA höchstens einmal an Sage, auch wenn zwei Benutzer gleichzeitig exportieren
            // Nicht erteilt betrifft nur diesen PA: überspringen, die übrigen PAs werden weiter übergeben
            var anspruch = await _repository.ExportBeanspruchenAsync(pa, job.Inhaber, cancellationToken);
            if (!anspruch.Erteilt)
            {
                string grund;
                if (!anspruch.Vorhanden)
                {
                    grund = "Kein Eintrag in SageHDR - bitte die Seite neu laden.";
                }
                else if (SageStatusText.IstUebergeben(anspruch.Status))
                {
                    grund = "Wurde bereits" + (anspruch.AnSageGesendetAm.HasValue ? $" am {anspruch.AnSageGesendetAm.Value:dd.MM.yyyy} um {anspruch.AnSageGesendetAm.Value:HH:mm} Uhr" : "") + (string.IsNullOrEmpty(anspruch.AnSageGesendetVon) ? "" : $" von {anspruch.AnSageGesendetVon}") + " an Sage übergeben.";
                }
                else
                {
                    grund = $"Ist von {anspruch.GesperrtVon} gesperrt.";
                }
                return new SageExportSchritt { PA = pa, Ergebnis = SageExportErgebnis.NichtUebergeben, Meldung = grund };
            }

            SageAntwort antwort;
            List<SagePosZeile> zeilen;
            try
            {
                // Vor dem Senden: SagePos für diesen PA löschen und mit den Positionen neu anlegen
                zeilen = SagePosZeilen(pa, paPositions);
                await _repository.SagePosErsetzenAsync(pa, zeilen, cancellationToken);

                antwort = await _sage.PostXmlTransactionAsync(job.Url, BuildDocument(pa, paPositions, job.Test), cancellationToken);
            }
            catch (SageKeineAntwortException)
            {
                // Gesendet, aber keine Antwort: Sage kann den Auftrag angelegt haben. Der PA bleibt deshalb
                // übergeben (Status "XML erzeugt" mit Hinweis) - erneut übergeben erst nach Prüfung in Sage.
                await _repository.ExportAbschliessenAsync(pa, job.Inhaber, SageStatus.XmlErzeugt, KeineAntwortHinweis, null, null, null, CancellationToken.None);
                return new SageExportSchritt { PA = pa, Ergebnis = SageExportErgebnis.KeineAntwort, Meldung = KeineAntwortHinweis };
            }
            catch (Exception ex)
            {
                // SagePos oder Versand fehlgeschlagen -> Status und AnSageGesendetAm/-Von zurück, der PA ist wieder übergebbar
                await _repository.ExportZuruecknehmenAsync(pa, job.Inhaber, anspruch, CancellationToken.None);
                if (ex is SageNichtErreichbarException)
                {
                    // Schnittstelle weg: auch alle weiteren PAs würden scheitern -> Export abbrechen
                    throw new SageExportException($"PA {pa} konnte nicht übergeben werden. {ex.Message}", ex);
                }
                if (ex is SqlException)
                {
                    // Datenbankfehler: Export abbrechen
                    throw new SageExportException($"Die Positionen von PA {pa} konnten nicht in SagePos gespeichert werden:\n{ex.Message}", ex);
                }
                if (ex is SageExportException)
                {
                    // Betrifft nur diesen PA (HTTP-Fehler wie 413, unbrauchbare Antwort, fehlerhafte Positionsdaten):
                    // überspringen, die übrigen PAs werden weiter übergeben
                    var meldung = ex.Message.Replace("\r", "").Replace("\n", " ");
                    var vorsatz = $"PA {pa}: ";
                    if (meldung.StartsWith(vorsatz, StringComparison.Ordinal))
                    {
                        meldung = meldung.Substring(vorsatz.Length);
                    }
                    return new SageExportSchritt { PA = pa, Ergebnis = SageExportErgebnis.NichtUebergeben, Meldung = meldung };
                }
                throw;
            }

            if (!antwort.Erfolg)
            {
                // Sage hat abgelehnt: Status "Fehler in Sage" und die Meldung von Sage in SageHDR und SagePos;
                // der PA kann nach der Korrektur erneut übergeben werden
                await _repository.ExportAbschliessenAsync(pa, job.Inhaber, SageStatus.FehlerInSage, antwort.Fehlermeldung, null, null, null, CancellationToken.None);
                return new SageExportSchritt { PA = pa, Ergebnis = SageExportErgebnis.Abgelehnt, Meldung = antwort.Fehlermeldung };
            }

            // Auftrag in Sage angelegt: Status "Alles ok in Sage", eine etwaige Meldung von Sage und die
            // OfficeLineHandle (SageHDR/SagePos) sowie die OfficeLineVorgangsHandle (SageHDR) eintragen
            var handle = Handle(antwort.OfficeLineHandle);
            await _repository.ExportAbschliessenAsync(pa, job.Inhaber, SageStatus.AllesOkInSage, antwort.Fehlermeldung,
                handle, Handle(antwort.OfficeLineVorgangsHandle), PositionsHandles(paPositions, zeilen, antwort), CancellationToken.None);
            return new SageExportSchritt { PA = pa, Ergebnis = SageExportErgebnis.Uebergeben, OfficeLineHandle = handle, OfficeLineVorgangsHandle = Handle(antwort.OfficeLineVorgangsHandle), Meldung = antwort.Fehlermeldung };
        }

        // OfficeLineHandle der Positionen: je Zeile der Prozedur ein Sample mit einem Package, in derselben Reihenfolge.
        // Zur Sicherheit muss der LIMSIdentifier des Package zur Zeile passen; sonst wird über einen eindeutigen
        // LIMSIdentifier gesucht. Ohne passendes Package (oder ohne Zahl als Handle) bleibt SagePos.OfficeLineHandle NULL.
        private static List<SagePosHandle> PositionsHandles(List<WipPaketAna> positions, List<SagePosZeile> zeilen, SageAntwort antwort)
        {
            var handles = new List<SagePosHandle>();
            for (int i = 0; i < positions.Count; i++)
            {
                var lims = FormatValue(GetColumn(positions[i], "LIMSIdentifier"));
                var package = i < antwort.Packages.Count && antwort.Packages[i].LIMSIdentifier == lims
                    ? antwort.Packages[i]
                    : null;
                if (package == null && !string.IsNullOrEmpty(lims))
                {
                    var treffer = antwort.Packages.Where(p => p.LIMSIdentifier == lims).ToList();
                    package = treffer.Count == 1 ? treffer[0] : null;
                }
                var handle = Handle(package?.OfficeLineHandle);
                if (handle.HasValue)
                {
                    handles.Add(new SagePosHandle { PI = zeilen[i].PI, Ulfd = zeilen[i].Ulfd, OfficeLineHandle = handle.Value });
                }
            }
            return handles;
        }

        private static int? Handle(string value) =>
            int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var handle) ? handle : (int?)null;

        public async Task<SageExportResult> ExportAsync(ICollection<int> selectedPAs, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            var job = await PrepareAsync(selectedPAs, inhaber, cancellationToken);
            var result = new SageExportResult { Url = job.Url };
            foreach (var pa in job.PAs)
            {
                result.Schritte.Add(await ExportOneAsync(job, pa, cancellationToken));
            }
            return result;
        }

        public async Task NeuErstellenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            // In SageHDR zurücksetzen (Status "neu", Satz gesperrt) - danach ist der PA wieder übergebbar
            var anspruch = await _repository.ExportZuruecksetzenAsync(pa, inhaber, cancellationToken);
            if (!anspruch.Erteilt)
            {
                if (!anspruch.Vorhanden)
                {
                    throw new SageExportException($"Für PA {pa} gibt es keinen Eintrag in SageHDR. Bitte die Seite neu laden.");
                }
                if (!string.IsNullOrEmpty(anspruch.GesperrtVon))
                {
                    throw new SageExportException($"PA {pa} ist von {anspruch.GesperrtVon} gesperrt und kann nicht erneut übergeben werden.");
                }
                throw new SageExportException($"PA {pa} wurde noch nicht an Sage übergeben - er kann ganz normal übergeben werden. Bitte die Seite neu laden.");
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
        //               Artikelnummer    ArtikelNR; bei Konfig.SageTest = true immer TestArtikelnummer (04005019)
        //             Price              Endpreis
        //     Wkz                  USD = 0 -> "USD", sonst "EUR"
        // ------------------------------------------------------------------------------------------
        private static readonly XNamespace Ns = "http://schemas.datacontract.org/2004/07/appulsive.Intertek.LIMSService";
        private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

        private const string KopftextEnglisch = "We thank you very much for your order and charge the services rendered as follows:";
        private const string KopftextDeutsch = "Wir danken Ihnen für den uns erteilten Auftrag und erlauben uns, folgende Services in Rechnung zu stellen:";
        private const string ProbeEnglisch = "Your reference:";
        private const string ProbeDeutsch = "Ihre Probe:";

        private static XDocument BuildDocument(int pa, List<WipPaketAna> positions, bool test)
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
                new XElement(Ns + "Samples", positions.Select(p => BuildSample(p, englisch, matchcode, test))),
                Element("Wkz", Waehrung(head)));

            return new XDocument(new XDeclaration("1.0", "utf-8", null), order);
        }

        private static XElement BuildSample(WipPaketAna p, bool englisch, string matchcode, bool test)
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
                            Element("Artikelnummer", test ? TestArtikelnummer : FormatValue(GetColumn(p, "ArtikelNR")))),
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
