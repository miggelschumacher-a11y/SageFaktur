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

        /// <summary>Ruft dbo.WIPSage_ProcR_getWIP_PaketAna auf (Positionen aller offenen PAs).</summary>
        Task<IReadOnlyList<WipPaketAna>> GetWipPaketAnaAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>true, wenn ZugriffEx.SageFaktu für den Benutzer (MitarbeiterNr) den Wert 1 hat.</summary>
        Task<bool> GetSageFakturAsync(long userNr, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>Liest Konfig.SageExportOrdner; null, wenn nichts hinterlegt ist.</summary>
        Task<string> GetSageExportOrdnerAsync(CancellationToken cancellationToken = default(CancellationToken));
    }
}
