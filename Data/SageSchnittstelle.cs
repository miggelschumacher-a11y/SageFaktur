using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace WipSageWeb.Data
{
    /// <summary>
    /// Antwort der Sage-Schnittstelle auf einen Auftrag (Wrapper AddOrderResult, siehe "sage API Auftragsanlage.docx").
    /// <para>
    /// Bei Erfolg liefert Sage drei Arten von Handles zurück. Ein Handle ist die interne Nummer, unter der Sage 100
    /// (früher "Office Line") einen Datensatz führt; Sage vergibt sie selbst beim Anlegen.
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="OfficeLineHandle"/> (Order/OfficeLineHandle): der Auftrag, den die App gerade
    /// angelegt hat - gespeichert in SageHDR.OfficeLineHandle.</description></item>
    /// <item><description><see cref="OfficeLineVorgangsHandle"/> (Order/OfficeLineVorgangsHandle): der Vorgang, zu dem
    /// dieser Auftrag gehört - gespeichert in SageHDR.OfficeLineVorgangsHandle.</description></item>
    /// <item><description><see cref="SagePackageAntwort.OfficeLineHandle"/> (Package/OfficeLineHandle): je Position
    /// (Package) des Auftrags - gespeichert in SagePos.OfficeLineHandle.</description></item>
    /// </list>
    /// <para>
    /// Vorgang: In Sage 100 hängt jeder Beleg an einem Vorgang. Ein Vorgang fasst alle Belege eines Geschäftsfalls
    /// zusammen, die auseinander entstehen - Auftrag (legt die App an), daraus später in Sage Lieferschein und Rechnung.
    /// Jeder dieser Belege bekommt in Sage eine eigene Nummer, der Vorgang bleibt für die ganze Kette derselbe.
    /// Das OfficeLineHandle zeigt also nur auf den Auftrag; über das OfficeLineVorgangsHandle lässt sich später z. B.
    /// herausfinden, ob aus dem Auftrag schon eine Rechnung geworden ist und mit welcher Belegnummer und welchem Datum.
    /// </para>
    /// <para>
    /// Die Schnittstellenbeschreibung sagt nur, die Handles seien "zur späteren Verwendung" zu speichern. Das Belegkonzept
    /// oben ist das übliche von Sage 100, nicht in der Schnittstelle dokumentiert. Ob es eine Abfrage zum Vorgang gibt
    /// (z. B. Rechnung zum Auftrag), muss der Ersteller der Schnittstelle beantworten.
    /// </para>
    /// </summary>
    public sealed class SageAntwort
    {
        /// <summary>true: Sage hat den Auftrag angelegt (ResultStatus = true).</summary>
        public bool Erfolg { get; set; }

        /// <summary>
        /// Meldung von Sage (ErrorMessage) zur Ausgabe an den Benutzer. Bei Ablehnung immer gefüllt,
        /// bei Erfolg nur, wenn Sage trotzdem eine Meldung mitschickt.
        /// </summary>
        public string Fehlermeldung { get; set; }

        /// <summary>
        /// OfficeLineHandle des angelegten Auftrags (Order/OfficeLineHandle): die interne Nummer des Auftrags in Sage.
        /// Zeigt nur auf den Auftrag, nicht auf den Lieferschein oder die Rechnung, die Sage später daraus erzeugt
        /// (siehe <see cref="OfficeLineVorgangsHandle"/>). Nur bei Erfolg.
        /// </summary>
        public string OfficeLineHandle { get; set; }

        /// <summary>
        /// OfficeLineVorgangsHandle (Order/OfficeLineVorgangsHandle): die interne Nummer des Vorgangs in Sage, zu dem der
        /// Auftrag gehört. Der Vorgang umfasst die ganze Belegkette Auftrag - Lieferschein - Rechnung und bleibt für alle
        /// diese Belege gleich; darüber lässt sich später z. B. die Rechnung zum übergebenen Auftrag finden. Nur bei Erfolg.
        /// </summary>
        public string OfficeLineVorgangsHandle { get; set; }

        /// <summary>Je Package (Position) LIMSIdentifier und OfficeLineHandle, in der Reihenfolge der Antwort; nur bei Erfolg.</summary>
        public List<SagePackageAntwort> Packages { get; } = new List<SagePackageAntwort>();

        /// <summary>Die vollständige Antwort.</summary>
        public XDocument Dokument { get; set; }
    }

    /// <summary>Einstellungen für die Übergabe an Sage aus dbo.Konfig.</summary>
    public sealed class SageKonfig
    {
        /// <summary>
        /// Adresse der Sage-Schnittstelle: bei <see cref="Test"/> = true aus Konfig.SageTestExportURL, sonst aus
        /// Konfig.SageReleaseExportURL; null, wenn im jeweiligen Feld nichts hinterlegt ist.
        /// </summary>
        public string ExportUrl { get; set; }

        /// <summary>Name des Feldes in Konfig, aus dem <see cref="ExportUrl"/> stammt (für Meldungen).</summary>
        public string ExportUrlFeld { get; set; }

        /// <summary>
        /// Konfig.SageTest: Testbetrieb. Dann geht der Auftrag an Konfig.SageTestExportURL (sonst an
        /// Konfig.SageReleaseExportURL), und in den XML-Daten steht als Artikelnummer immer
        /// <see cref="SageExportService.TestArtikelnummer"/>.
        /// </summary>
        public bool Test { get; set; }
    }

    /// <summary>Ein Package aus der Antwort von Sage (Order/Samples/Sample/Packages/Package) - eine Position des Auftrags.</summary>
    public sealed class SagePackageAntwort
    {
        /// <summary>
        /// LIMSIdentifier, den die App für die Position gesendet hat; Sage gibt ihn 1:1 zurück. Darüber ordnet die App
        /// das Handle der Zeile in SagePos zu.
        /// </summary>
        public string LIMSIdentifier { get; set; }

        /// <summary>
        /// OfficeLineHandle der Position: die interne Nummer, unter der Sage diese Position des Auftrags führt
        /// (gespeichert in SagePos.OfficeLineHandle).
        /// </summary>
        public string OfficeLineHandle { get; set; }
    }

    /// <summary>
    /// Die Sage-Schnittstelle ist gar nicht erreichbar (Verbindung abgelehnt, Name unbekannt, Netzwerk weg).
    /// Dann scheitern auch alle weiteren PAs - der Export bricht ab. Andere Fehler betreffen nur den einen PA.
    /// </summary>
    public sealed class SageNichtErreichbarException : Exception
    {
        public SageNichtErreichbarException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Sage hat innerhalb der Wartezeit nicht geantwortet. Der Auftrag kann trotzdem angelegt worden sein -
    /// deshalb darf der PA nicht einfach wieder als "neu" gelten.
    /// </summary>
    public sealed class SageKeineAntwortException : Exception
    {
        public SageKeineAntwortException(string message, Exception inner) : base(message, inner) { }
    }

    public interface ISageSchnittstelle
    {
        /// <summary>
        /// Sendet einen Auftrag (Order-XML) per POST an die URL aus Konfig (SageTestExportURL bzw. SageReleaseExportURL)
        /// und liefert die Antwort.
        /// Eine Ablehnung durch Sage ist kein Ausnahmefall: <see cref="SageAntwort.Erfolg"/> = false mit Fehlermeldung.
        /// Ist die Schnittstelle nicht erreichbar, kommt eine <see cref="SageNichtErreichbarException"/>; ein HTTP-Fehler
        /// ohne lesbare Antwort (z. B. 413, Auftrag zu groß) eine SageExportException, keine Antwort innerhalb der
        /// Wartezeit eine <see cref="SageKeineAntwortException"/>.
        /// </summary>
        Task<SageAntwort> PostXmlTransactionAsync(string url, XDocument requestXml, CancellationToken cancellationToken = default(CancellationToken));
    }

    /// <summary>
    /// Überarbeitete Fassung der vorgefertigten Methode PostXMLTransaction:
    /// asynchron mit HttpClient statt HttpWebRequest, und die Antwort wird so gelesen, wie sie laut
    /// Schnittstellenbeschreibung kommt (Erfolg: Elemente ResultStatus/ErrorMessage, Fehler: Attribute Result/ErrorMessage).
    /// </summary>
    public sealed class SageSchnittstelle : ISageSchnittstelle
    {
        /// <summary>Wartezeit auf die Antwort von Sage.</summary>
        public static readonly TimeSpan Wartezeit = TimeSpan.FromSeconds(100);

        private readonly HttpClient _http;

        public SageSchnittstelle(HttpClient http)
        {
            _http = http;
            _http.Timeout = Timeout.InfiniteTimeSpan;   // Wartezeit regelt PostXmlTransactionAsync selbst
        }

        public async Task<SageAntwort> PostXmlTransactionAsync(string url, XDocument requestXml, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Wie im Original: UTF-8 ohne BOM, Content-Type text/xml; charset=utf-8
            byte[] bytes;
            using (var stream = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                {
                    requestXml.Save(writer);
                }
                bytes = stream.ToArray();
            }

            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };

            string body;
            System.Net.HttpStatusCode status;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(Wartezeit);
                try
                {
                    using (var response = await _http.PostAsync(url, content, timeout.Token))
                    {
                        status = response.StatusCode;
                        body = await response.Content.ReadAsStringAsync();
                    }
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new SageKeineAntwortException(
                        $"Sage hat nicht innerhalb von {Wartezeit.TotalSeconds:0} Sekunden geantwortet.", ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new SageNichtErreichbarException($"Die Sage-Schnittstelle ist nicht erreichbar:\n{url}\n{ex.GetBaseException().Message}", ex);
                }
            }

            // Antwort auch bei HTTP-Fehlerstatus lesen: sie kann ein AddOrderResult mit Fehlermeldung enthalten
            XDocument doc = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(body))
                {
                    doc = XDocument.Parse(body);
                }
            }
            catch (XmlException)
            {
                doc = null;
            }

            if (doc?.Root == null || doc.Root.Name.LocalName != "AddOrderResult")
            {
                // Fehlerseiten kommen oft als HTML: nur der Text, zusammengezogen und gekürzt
                var kurz = Regex.Replace(Regex.Replace(body ?? "", "<[^>]*>", " "), @"\s+", " ").Trim();
                if (kurz.Length > 200)
                {
                    kurz = kurz.Substring(0, 200) + " …";
                }
                throw new SageExportException(
                    $"Die Sage-Schnittstelle hat keine verwertbare Antwort geliefert (HTTP {(int)status} {status}" +
                    $", gesendet: {(bytes.Length + 1023) / 1024} KB)." + (kurz.Length > 0 ? " " + kurz : ""));
            }

            var root = doc.Root;
            var erfolg = IstWahr(Wert(root, "ResultStatus")) || IstWahr(Wert(root, "Result"));
            var antwort = new SageAntwort { Erfolg = erfolg, Dokument = doc };
            if (erfolg)
            {
                var hinweis = Wert(root, "ErrorMessage");
                antwort.Fehlermeldung = string.IsNullOrWhiteSpace(hinweis) ? null : hinweis.Trim();

                var order = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Order");
                if (order != null)
                {
                    antwort.OfficeLineHandle = Wert(order, "OfficeLineHandle");
                    antwort.OfficeLineVorgangsHandle = Wert(order, "OfficeLineVorgangsHandle");
                    antwort.Packages.AddRange(order.Descendants()
                        .Where(e => e.Name.LocalName == "Package")
                        .Select(p => new SagePackageAntwort { LIMSIdentifier = Wert(p, "LIMSIdentifier"), OfficeLineHandle = Wert(p, "OfficeLineHandle") }));
                }
            }
            else
            {
                var meldung = Wert(root, "ErrorMessage");
                antwort.Fehlermeldung = string.IsNullOrWhiteSpace(meldung)
                    ? $"Sage hat den Auftrag abgelehnt, ohne einen Grund zu nennen (HTTP {(int)status})."
                    : meldung.Trim();
            }
            return antwort;
        }

        // Wert als Kind-Element (Erfolgsfall) oder als Attribut (Fehlerfall); i:nil="true" und leer -> null.
        // Vergleich nur über den lokalen Namen - die Antwort steht im Namespace appulsive.Intertek.LIMSService.
        private static string Wert(XElement parent, string name)
        {
            var element = parent.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            if (element != null)
            {
                var nil = element.Attributes().FirstOrDefault(a => a.Name.LocalName == "nil");
                return nil != null && IstWahr(nil.Value) ? null : element.Value;
            }
            return parent.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
        }

        private static bool IstWahr(string value) =>
            string.Equals((value ?? "").Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }
}
