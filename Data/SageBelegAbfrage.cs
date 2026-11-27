using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WipSageWeb.Models;

namespace WipSageWeb.Data
{
    /// <summary>Eine Positionszeile aus der JSON-Antwort von Konfig.SageResultUrl.</summary>
    public sealed class SageBelegZeile
    {
        public int? VorID { get; set; }
        public int? BelID { get; set; }
        public string Belegart { get; set; }
        public int? Belegjahr { get; set; }
        public int? Belegnummer { get; set; }
        public DateTime? Belegdatum { get; set; }
        public string LIMSIdentifier_Belegkopf { get; set; }
    }

    public interface ISageBelegAbfrage
    {
        /// <summary>
        /// Fragt die Belege eines PA in Sage ab (HTTP GET an Konfig.SageResultUrl mit dem PA als Parameter) und
        /// liefert die Zeilen des JSON-Arrays. Fehler (nicht erreichbar, HTTP-Fehler, kein JSON-Array, keine Antwort
        /// innerhalb der Wartezeit) kommen als <see cref="SageExportException"/>.
        /// </summary>
        Task<IReadOnlyList<SageBelegZeile>> AbfragenAsync(string url, int pa, CancellationToken cancellationToken = default(CancellationToken));
    }

    public sealed class SageBelegAbfrage : ISageBelegAbfrage
    {
        /// <summary>Wartezeit je PA.</summary>
        public static readonly TimeSpan Wartezeit = TimeSpan.FromSeconds(20);

        private readonly HttpClient _http;

        public SageBelegAbfrage(HttpClient http)
        {
            _http = http;
            _http.Timeout = Timeout.InfiniteTimeSpan;   // Wartezeit regelt AbfragenAsync selbst
        }

        /// <summary>
        /// Adresse für einen PA: steht in der URL der Platzhalter {PA} oder {0}, wird er ersetzt; endet sie auf "=" oder "/",
        /// wird der PA angehängt; sonst kommt "?PA=..." bzw. "&amp;PA=..." dazu.
        /// </summary>
        public static string UrlFuerPa(string url, int pa)
        {
            var wert = pa.ToString(CultureInfo.InvariantCulture);
            url = url.Trim();
            if (Regex.IsMatch(url, @"\{(PA|0)\}", RegexOptions.IgnoreCase))
            {
                return Regex.Replace(url, @"\{(PA|0)\}", wert, RegexOptions.IgnoreCase);
            }
            if (url.EndsWith("=", StringComparison.Ordinal) || url.EndsWith("/", StringComparison.Ordinal))
            {
                return url + wert;
            }
            return url + (url.Contains("?") ? "&" : "?") + "PA=" + wert;
        }

        public async Task<IReadOnlyList<SageBelegZeile>> AbfragenAsync(string url, int pa, CancellationToken cancellationToken = default(CancellationToken))
        {
            var adresse = UrlFuerPa(url, pa);
            string body;
            System.Net.HttpStatusCode status;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(Wartezeit);
                try
                {
                    using (var response = await _http.GetAsync(adresse, timeout.Token))
                    {
                        status = response.StatusCode;
                        body = await response.Content.ReadAsStringAsync();
                    }
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new SageExportException($"Sage hat nicht innerhalb von {Wartezeit.TotalSeconds:0} Sekunden geantwortet.", ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new SageExportException($"Konfig.SageResultUrl ist nicht erreichbar: {ex.GetBaseException().Message}", ex);
                }
            }

            if ((int)status < 200 || (int)status > 299)
            {
                throw new SageExportException($"Konfig.SageResultUrl hat HTTP {(int)status} {status} geliefert.");
            }

            JArray array;
            try
            {
                var token = string.IsNullOrWhiteSpace(body) ? new JArray() : JToken.Parse(body);
                array = token as JArray;
            }
            catch (JsonException ex)
            {
                throw new SageExportException("Die Antwort von Konfig.SageResultUrl ist kein gültiges JSON.", ex);
            }
            if (array == null)
            {
                throw new SageExportException("Die Antwort von Konfig.SageResultUrl ist kein JSON-Array.");
            }

            return array.OfType<JObject>().Select(o => new SageBelegZeile
            {
                VorID = Zahl(o, "VorID"),
                BelID = Zahl(o, "BelID"),
                Belegart = Text(o, "Belegart"),
                Belegjahr = Zahl(o, "Belegjahr"),
                Belegnummer = Zahl(o, "Belegnummer"),
                Belegdatum = Datum(o, "Belegdatum"),
                LIMSIdentifier_Belegkopf = Text(o, "LIMSIdentifier_Belegkopf")
            }).ToList();
        }

        /// <summary>Die Lieferschein-Spalten sind schon aus der Datenbank (View) gefüllt.</summary>
        public static bool LieferscheinAusDatenbank(UebertragenerPa row) =>
            row.LiefNr.HasValue || row.LiefDat.HasValue || row.BelegJahrL.HasValue;

        /// <summary>Die Rechnungs-Spalten sind schon aus der Datenbank (View) gefüllt.</summary>
        public static bool RechnungAusDatenbank(UebertragenerPa row) =>
            row.RechNr.HasValue || row.RechDat.HasValue || row.BelegJahrR.HasValue || !string.IsNullOrWhiteSpace(row.Rechnungsart);

        /// <summary>
        /// Überträgt die Belege aus der Antwort in die History-Zeile - je Gruppe (Lieferschein / Rechnung) nur, wenn ihre
        /// Spalten noch nicht aus der Datenbank gefüllt sind. Zeilen mit gleichen VorID, BelID, Belegart, Belegjahr,
        /// Belegnummer, Belegdatum und LIMSIdentifier_Belegkopf sind ein Beleg.
        /// Lieferschein: genau einer -> die Spalten werden damit gefüllt; mehrere -> "Not unique"; keiner -> leer.
        /// Rechnung und Sammelrechnung sind getrennte Belegarten, jede wird für sich gezählt: zwei Rechnungen oder zwei
        /// Sammelrechnungen -> "Not unique"; sonst wird die eine gezeigt (gibt es je eine Rechnung und eine Sammelrechnung,
        /// die mit dem späteren Belegdatum).
        /// </summary>
        public static void Uebernehmen(UebertragenerPa row, IEnumerable<SageBelegZeile> zeilen)
        {
            var lieferscheinSetzen = !LieferscheinAusDatenbank(row);
            var rechnungSetzen = !RechnungAusDatenbank(row);
            if (!lieferscheinSetzen && !rechnungSetzen)
            {
                return;
            }

            var belege = zeilen
                .GroupBy(z => new
                {
                    z.VorID,
                    z.BelID,
                    Belegart = (z.Belegart ?? "").Trim().ToUpperInvariant(),
                    z.Belegjahr,
                    z.Belegnummer,
                    z.Belegdatum,
                    Kopf = (z.LIMSIdentifier_Belegkopf ?? "").Trim()
                })
                .Select(g => g.First())
                .ToList();

            row.SageAbgefragt = true;

            if (lieferscheinSetzen)
            {
                var lieferscheine = belege.Where(b => IstArt(b, "Lieferschein")).ToList();
                row.LieferscheinNichtEindeutig = lieferscheine.Count > 1;
                var l = lieferscheine.Count == 1 ? lieferscheine[0] : null;
                row.Lieferscheinart = l?.Belegart?.Trim();
                row.BelegJahrL = l?.Belegjahr;
                row.LiefNr = l?.Belegnummer;
                row.LiefDat = l?.Belegdatum;
            }

            if (rechnungSetzen)
            {
                var rechnungen = belege.Where(b => IstArt(b, "Rechnung")).ToList();
                var sammelrechnungen = belege.Where(b => IstArt(b, "Sammelrechnung")).ToList();
                row.RechnungNichtEindeutig = rechnungen.Count > 1;
                row.SammelRechnungNichtEindeutig = sammelrechnungen.Count > 1;
                var r = row.RechnungNichtEindeutig || row.SammelRechnungNichtEindeutig
                    ? null
                    : rechnungen.Concat(sammelrechnungen)
                                .OrderByDescending(b => b.Belegdatum ?? DateTime.MinValue)
                                .ThenByDescending(b => b.Belegnummer ?? 0)
                                .FirstOrDefault();
                row.Rechnungsart = r?.Belegart?.Trim();
                row.BelegJahrR = r?.Belegjahr;
                row.RechNr = r?.Belegnummer;
                row.RechDat = r?.Belegdatum;
            }
        }

        private static bool IstArt(SageBelegZeile beleg, string art) =>
            string.Equals((beleg.Belegart ?? "").Trim(), art, StringComparison.OrdinalIgnoreCase);

        private static string Text(JObject o, string name)
        {
            var t = o.GetValue(name, StringComparison.OrdinalIgnoreCase);
            return t == null || t.Type == JTokenType.Null ? null : t.ToString();
        }

        private static int? Zahl(JObject o, string name)
        {
            var text = Text(o, name);
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d == Math.Truncate(d)
                   && d >= int.MinValue && d <= int.MaxValue ? (int)d : (int?)null;
        }

        private static DateTime? Datum(JObject o, string name)
        {
            var t = o.GetValue(name, StringComparison.OrdinalIgnoreCase);
            if (t == null || t.Type == JTokenType.Null)
            {
                return null;
            }
            if (t.Type == JTokenType.Date)
            {
                return t.Value<DateTime>();
            }
            return DateTime.TryParse(t.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null;
        }
    }
}
