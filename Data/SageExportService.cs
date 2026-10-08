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
    /// Ein vorbereiteter Export: Ordner ist geprüft, die Positionen sind geladen.
    /// Danach wird PA für PA geschrieben - so kann die Seite den echten Fortschritt anzeigen.
    /// </summary>
    public sealed class SageExportJob
    {
        public string Folder { get; set; }
        public string Timestamp { get; set; }
        public Dictionary<int, List<WipPaketAna>> Positions { get; } = new Dictionary<int, List<WipPaketAna>>();
    }

    public interface ISageExportService
    {
        /// <summary>Prüft Konfig.SageExportOrdner und lädt die Positionen der ausgewählten PAs.</summary>
        Task<SageExportJob> PrepareAsync(ICollection<int> selectedPAs, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Schreibt die XML-Datei eines PA; null, wenn der PA keine Positionen hat.</summary>
        string ExportOne(SageExportJob job, int pa);

        /// <summary>Schreibt je PA eine XML-Datei in Konfig.SageExportOrdner.</summary>
        Task<SageExportResult> ExportAsync(ICollection<int> selectedPAs, CancellationToken cancellationToken = default(CancellationToken));
    }

    /// <summary>
    /// Stellt die ausgewählten PAs als XML für Sage bereit: je PA eine Datei im Order-Format
    /// (Vorlage "XML-Struktur-#1.xml"), gefüllt aus dbo.WIPSage_ProcR_getWIP_PaketAna.
    /// </summary>
    public sealed class SageExportService : ISageExportService
    {

        private readonly IWipSageRepository _repository;

        public SageExportService(IWipSageRepository repository)
        {
            _repository = repository;
        }

        public async Task<SageExportJob> PrepareAsync(ICollection<int> selectedPAs, CancellationToken cancellationToken = default(CancellationToken))
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

            // Die Prozedur läuft nur einmal je Export; die Positionen werden den ausgewählten PAs zugeordnet
            var positions = await _repository.GetWipPaketAnaAsync(cancellationToken);
            var job = new SageExportJob
            {
                Folder = folder,
                Timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)
            };
            foreach (var pa in selectedPAs.Distinct().OrderBy(p => p))
            {
                job.Positions[pa] = positions.Where(p => p.BelegMatchCode == pa).ToList();
            }
            return job;
        }

        public string ExportOne(SageExportJob job, int pa)
        {
            if (!job.Positions.TryGetValue(pa, out var paPositions) || paPositions.Count == 0)
            {
                return null;
            }

            var fileName = $"WIPSage_PA_{pa.ToString(CultureInfo.InvariantCulture)}_{job.Timestamp}.xml";
            var path = Path.Combine(job.Folder, fileName);
            try
            {
                var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
                using (var writer = XmlWriter.Create(path, settings))
                {
                    BuildDocument(pa, paPositions).Save(writer);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new SageExportException(
                    $"In den Export-Ordner für Sage kann nicht geschrieben werden:\n{job.Folder}\n{ex.Message}", ex);
            }
            return fileName;
        }

        public async Task<SageExportResult> ExportAsync(ICollection<int> selectedPAs, CancellationToken cancellationToken = default(CancellationToken))
        {
            var job = await PrepareAsync(selectedPAs, cancellationToken);
            var result = new SageExportResult { Folder = job.Folder };
            foreach (var pa in job.Positions.Keys.OrderBy(p => p))
            {
                var fileName = ExportOne(job, pa);
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

        // ------------------------------------------------------------------------------------------
        // Aufbau der XML-Datei nach der Vorlage "XML-Struktur-#1.xml" (Order-Format der Sage-Schnittstelle).
        // Jedes Element bekommt den Wert der genannten Spalte aus dbo.WIPSage_ProcR_getWIP_PaketAna.
        // Reihenfolge und Elementnamen entsprechen genau der Vorlage.
        // ------------------------------------------------------------------------------------------
        private static readonly XNamespace Ns = "http://schemas.datacontract.org/2004/07/appulsive.Intertek.LIMSService";

        // Fester Text aus der Vorlage (keine Spalte der Prozedur)
        private const string Kopftext = "We thank you very much for your order and charge the services rendered as follows:";

        // Kopf des Auftrags: XML-Element -> Spalte der Prozedur (Wert aus der ersten Position des PA)
        private static readonly string[][] OrderFieldsBeforeSamples =
        {
            new[] { "Kto", "Kunde_Auftraggeber" },
            new[] { "Matchcode", "BelegMatchCode" }
        };

        // Je Position ein <Package>: XML-Element -> Spalte der Prozedur
        private static readonly string[][] PackageFields =
        {
            new[] { "Bezeichnung", "Lieferdatum" },
            new[] { "Bezeichnung2", "LeistungsZeitraum_von" },
            new[] { "Dimensionstext", "Leistungszeitraum_bis" },
            new[] { "Erloeskonto", "Konto" },
            new[] { "Kostenstelle", "KST" },
            new[] { "OfficeLineHandle", "ArtikelNR" },
            new[] { "Langtext", "ArtikelName" },
            new[] { "Menge", "Menge" },
            new[] { "Price", "Endpreis" },
            new[] { "LIMSIdentifier", "LIMSIdentifier" }
        };

        private static XDocument BuildDocument(int pa, List<WipPaketAna> positions)
        {
            var head = positions[0];

            var order = new XElement(Ns + "Order",
                new XElement(Ns + "Kopftext", Kopftext),
                OrderFieldsBeforeSamples.Select(f => Field(head, f)),
                new XElement(Ns + "Samples",
                    new XElement(Ns + "Sample",
                        new XElement(Ns + "Packages",
                            positions.Select(p => new XElement(Ns + "Package", PackageFields.Select(f => Field(p, f))))))),
                new XElement(Ns + "Wkz", Waehrung(head)));

            return new XDocument(new XDeclaration("1.0", "utf-8", null), order);
        }

        // Währungskürzel für Sage: Spalte USD = 1 -> "USD", jeder andere Wert (auch NULL) -> "EUR"
        private static string Waehrung(WipPaketAna position)
        {
            var usd = GetColumn(position, "USD");
            return usd != null && decimal.TryParse(Convert.ToString(usd, CultureInfo.InvariantCulture),
                       NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value == 1m
                ? "USD"
                : "EUR";
        }

        private static XElement Field(WipPaketAna position, string[] mapping)
        {
            return new XElement(Ns + mapping[0], FormatValue(GetColumn(position, mapping[1])));
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
                $"Die Spalte \"{column}\" fehlt im Ergebnis von WIPSage_ProcR_getWIP_PaketAna.\nOhne sie kann die XML-Datei für Sage nicht erzeugt werden.");
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
