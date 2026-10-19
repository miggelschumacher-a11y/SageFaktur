using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WipSageWeb.Models;

namespace WipSageWeb.Data
{
    public interface IWipSageRepository
    {
        /// <summary>
        /// Liest Kurzname, Kennwort und Laborleiter aus der Tabelle Zugriff (wie GetLoginDetails).
        /// null, wenn es den Benutzer nicht gibt oder er ausgetreten ist.
        /// </summary>
        Task<LoginDetails> GetLoginDetailsAsync(long userNr, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Ruft dbo.WIPSage_ProcR_getWIP_PAs auf.</summary>
        Task<IReadOnlyList<WipPa>> GetWipPAsAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Ruft dbo.WIPSage_ProcR_getWIP_PaketAnaEx auf (abrechenbare Positionen eines PA).</summary>
        Task<IReadOnlyList<WipPaketAna>> GetWipPaketAnaAsync(int pa, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>true, wenn ZugriffEx.SageFaktu für den Benutzer (MitarbeiterNr) den Wert 1 hat.</summary>
        Task<bool> GetSageFakturAsync(long userNr, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Legt für jeden PA ohne Eintrag einen Satz in dbo.SageHDR an und sperrt alle PAs, die frei sind,
        /// deren Sperre abgelaufen ist oder die schon diesem Benutzer gehören (nur noch nicht übergebene).
        /// Liefert den Zustand jedes PA danach.
        /// </summary>
        Task<IReadOnlyDictionary<int, SageHdrZustand>> SperrenAsync(IEnumerable<int> pas, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Verlängert die noch gültigen Sperren des Benutzers; liefert die Anzahl.</summary>
        Task<int> SperrenVerlaengernAsync(SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Hebt die Sperren dieser Anmeldung auf; liefert die Anzahl.</summary>
        Task<int> SperrenFreigebenAsync(SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Beansprucht einen PA für seine XML-Datei: setzt DateiErstelltAm nur, wenn noch keine Datei erzeugt
        /// wurde und kein anderer Benutzer den PA gesperrt hat. Damit entsteht jede Datei nur einmal.
        /// </summary>
        Task<ExportAnspruch> ExportBeanspruchenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Löscht in dbo.SagePos alle Zeilen des PA (über SageHDR.id) und legt die übergebenen Zeilen neu an.
        /// Beides in einer Transaktion; liefert die Anzahl der angelegten Zeilen.
        /// </summary>
        Task<int> SagePosErsetzenAsync(int pa, IReadOnlyList<SagePosZeile> zeilen, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Trägt nach dem Schreiben der XML-Datei ihren Namen (ohne Pfad) in SageHDR.Datei ein.</summary>
        Task ExportAbschliessenAsync(int pa, SperrInhaber inhaber, string datei, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Nimmt einen Anspruch zurück, wenn die Datei nicht geschrieben werden konnte.</summary>
        Task ExportZuruecknehmenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Gibt einen schon übergebenen PA zum erneuten Erstellen frei: setzt Datei und DateiErstelltAm auf NULL und
        /// sperrt den Satz für den Benutzer - nur, wenn kein anderer Benutzer eine gültige Sperre hält.
        /// Liefert die bisherige Datei, damit sie aus dem Export-Ordner gelöscht werden kann.
        /// </summary>
        Task<NeuErstellenAnspruch> ExportZuruecksetzenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Macht ExportZuruecksetzenAsync rückgängig (alte Datei konnte nicht gelöscht werden) und hebt die Sperre auf.</summary>
        Task ExportWiederherstellenAsync(int pa, SperrInhaber inhaber, string datei, DateTime? erstelltAm, string erstelltVon, SageStatus? status, string messageVonSage, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Liest SageHDR.Status und MessageVonSage für die PAs; nur Sätze mit Inhalt.</summary>
        Task<IReadOnlyDictionary<int, SageRueckmeldung>> GetSageHdrRueckmeldungAsync(IEnumerable<int> pas, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Liest SagePos.Status und MessageVonSage eines PA, Schlüssel: PI (ohne "PI") und Ulfd.</summary>
        Task<IReadOnlyDictionary<(string PI, int Ulfd), SageRueckmeldung>> GetSagePosRueckmeldungAsync(int pa, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Liest Konfig.SageExportOrdner; null, wenn nichts hinterlegt ist.</summary>
        Task<string> GetSageExportOrdnerAsync(CancellationToken cancellationToken = default(CancellationToken));
    }
}
