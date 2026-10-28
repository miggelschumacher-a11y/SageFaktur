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

        /// <summary>
        /// Hebt die Sperren der Seite auf (GesperrtSitzung = "Anmeldung:Seite"), mit <paramref name="ganzeAnmeldung"/>
        /// die aller Seiten dieser Anmeldung (beim Abmelden); liefert die Anzahl.
        /// </summary>
        Task<int> SperrenFreigebenAsync(SperrInhaber inhaber, bool ganzeAnmeldung, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Beansprucht einen PA direkt vor dem Senden an Sage: setzt Status "XML erzeugt" und AnSageGesendetAm/-Von,
        /// nur wenn der PA noch nicht übergeben ist (Status "neu" oder "Fehler in Sage") und kein anderer Benutzer
        /// ihn gesperrt hat. Damit geht jeder PA nur einmal an Sage. Liefert auch die Werte vor dem Beanspruchen.
        /// </summary>
        Task<ExportAnspruch> ExportBeanspruchenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Löscht in dbo.SagePos alle Zeilen des PA (über SageHDR.id) und legt die übergebenen Zeilen neu an.
        /// Beides in einer Transaktion; liefert die Anzahl der angelegten Zeilen.
        /// </summary>
        Task<int> SagePosErsetzenAsync(int pa, IReadOnlyList<SagePosZeile> zeilen, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Trägt nach der Antwort von Sage Status und MessageVonSage in SageHDR und SagePos ein, dazu die
        /// OfficeLineHandle (SageHDR und SagePos) und OfficeLineVorgangsHandle (SageHDR) von Sage (null bzw. leer, wenn Sage keine geliefert hat - dann werden die Spalten NULL).
        /// </summary>
        Task ExportAbschliessenAsync(int pa, SperrInhaber inhaber, SageStatus status, string messageVonSage,
            int? officeLineHandle, int? officeLineVorgangsHandle, IReadOnlyList<SagePosHandle> posHandles, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Der Auftrag konnte nicht an Sage gesendet werden: stellt Status und AnSageGesendetAm/-Von wie vor dem
        /// Beanspruchen wieder her (Werte aus <paramref name="anspruch"/>).
        /// </summary>
        Task ExportZuruecknehmenAsync(int pa, SperrInhaber inhaber, ExportAnspruch anspruch, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Gibt einen schon übergebenen PA zur erneuten Übergabe frei: setzt Status auf "neu" und sperrt den Satz
        /// für den Benutzer - nur, wenn kein anderer Benutzer eine gültige Sperre hält.
        /// </summary>
        Task<NeuErstellenAnspruch> ExportZuruecksetzenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Liest SageHDR.Status und MessageVonSage für die PAs; nur Sätze mit Inhalt.</summary>
        Task<IReadOnlyDictionary<int, SageRueckmeldung>> GetSageHdrRueckmeldungAsync(IEnumerable<int> pas, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Liest SagePos.Status und MessageVonSage eines PA, Schlüssel: PI (ohne "PI") und Ulfd.</summary>
        Task<IReadOnlyDictionary<(string PI, int Ulfd), SageRueckmeldung>> GetSagePosRueckmeldungAsync(int pa, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Liest Konfig.SageTest (Testbetrieb) und die Adresse der Sage-Schnittstelle dazu: bei SageTest = true
        /// Konfig.SageTestExportURL, sonst Konfig.SageReleaseExportURL.
        /// </summary>
        Task<SageKonfig> GetSageKonfigAsync(CancellationToken cancellationToken = default(CancellationToken));
    }
}
