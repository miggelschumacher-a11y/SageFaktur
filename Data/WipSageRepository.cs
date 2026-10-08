using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WipSageWeb.Models;

namespace WipSageWeb.Data
{
    public sealed class WipSageRepository : IWipSageRepository
    {
        private const string GetWipPAsProc = "dbo.WIPSage_ProcR_getWIP_PAs";
        private const string GetWipPaketAnaProc = "dbo.WIPSage_ProcR_getWIP_PaketAna";

        // Gleiche Abfrage wie im bestehenden GetLoginDetails
        private const string LoginDetailsQuery = @"
            select Kurzname, Kennwort, Laborleiter
            from Zugriff
            where (Austritt is null or Austritt > getdate())
            and nummer = @NUMMUSR
            order by Nummer;";

        private readonly string _connectionString;

        public WipSageRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("LadisInSQL")
                ?? throw new InvalidOperationException("Connection-String 'LadisInSQL' fehlt in appsettings.json.");
        }

        public async Task<LoginDetails> GetLoginDetailsAsync(long userNr, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(LoginDetailsQuery, connection))
            {
                command.Parameters.Add("@NUMMUSR", SqlDbType.BigInt).Value = userNr;

                await connection.OpenAsync(cancellationToken);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        return null;
                    }
                    return new LoginDetails
                    {
                        Kurzname = GetString(reader, 0),
                        Kennwort = GetString(reader, 1),
                        // NULL = keine Rolle hinterlegt -> kein Zugriff
                        Laborleiter = GetDecimal(reader, 2) ?? -1m
                    };
                }
            }
        }

        public async Task<IReadOnlyList<WipPa>> GetWipPAsAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var rows = new List<WipPa>();

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(GetWipPAsProc, connection) { CommandType = CommandType.StoredProcedure })
            {
                await connection.OpenAsync(cancellationToken);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    // Ordinals einmal ermitteln - fällt sofort auf, wenn die SP eine Spalte umbenennt
                    int oPA = reader.GetOrdinal("PA");
                    int oKundeNr = reader.GetOrdinal("KundeNr");
                    int oKundeName = reader.GetOrdinal("KundeName");
                    int oPABemerkung = reader.GetOrdinal("PABemerkung");
                    int oPaPiBemerkung = reader.GetOrdinal("PaPiBemerkung");
                    int oFaktuNotizen2 = reader.GetOrdinal("FaktuNotizen2");
                    int oSperrflag = reader.GetOrdinal("Sperrflag");
                    int oRechnungszeit = reader.GetOrdinal("Rechnungszeit");
                    int oProblem = reader.GetOrdinal("Problem");
                    int oPAVersion = reader.GetOrdinal("PAVersion");
                    int oEndpreisSum = reader.GetOrdinal("EndpreisSum");
                    int oRueckmeldung = reader.GetOrdinal("RueckmeldungSchnittstelle");

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        rows.Add(new WipPa
                        {
                            PA = GetInt(reader, oPA),
                            KundeNr = GetInt(reader, oKundeNr),
                            KundeName = GetString(reader, oKundeName),
                            PABemerkung = GetString(reader, oPABemerkung),
                            PaPiBemerkung = GetString(reader, oPaPiBemerkung),
                            FaktuNotizen2 = GetString(reader, oFaktuNotizen2),
                            Sperrflag = GetShort(reader, oSperrflag),
                            Rechnungszeit = GetShort(reader, oRechnungszeit),
                            Problem = GetString(reader, oProblem),
                            PAVersion = GetInt(reader, oPAVersion),
                            EndpreisSum = GetDecimal(reader, oEndpreisSum),
                            RueckmeldungSchnittstelle = GetString(reader, oRueckmeldung)
                        });
                    }
                }
            }

            return rows;
        }

        public async Task<IReadOnlyList<WipPaketAna>> GetWipPaketAnaAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var rows = new List<WipPaketAna>();

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(GetWipPaketAnaProc, connection) { CommandType = CommandType.StoredProcedure })
            {
                await connection.OpenAsync(cancellationToken);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    int oBelegMatchCode = reader.GetOrdinal("BelegMatchCode");
                    int oPA = reader.GetOrdinal("PA");
                    int oPADatum = reader.GetOrdinal("PA_Datum");
                    int oLieferdatum = reader.GetOrdinal("Lieferdatum");
                    int oKunde = reader.GetOrdinal("Kunde_Auftraggeber");
                    int oKundeName = reader.GetOrdinal("KundeName_Auftraggeber");
                    int oPINummer = reader.GetOrdinal("PINummer");
                    int oUSD = reader.GetOrdinal("USD");
                    int oKundenAuftrag = reader.GetOrdinal("KundenAuftrag");
                    int oCharge1 = reader.GetOrdinal("Charge1");
                    int oCharge2 = reader.GetOrdinal("Charge2");
                    int oArtikelNR = reader.GetOrdinal("ArtikelNR");
                    int oArtikelName = reader.GetOrdinal("ArtikelName");
                    int oPBuDat = reader.GetOrdinal("PBuDat");
                    int oFixParaList = reader.GetOrdinal("FixParaList");
                    int oMenge = reader.GetOrdinal("Menge");
                    int oEndpreis = reader.GetOrdinal("Endpreis");
                    int oKonto = reader.GetOrdinal("Konto");
                    int oKST = reader.GetOrdinal("KST");
                    int oLIMSIdentifier = reader.GetOrdinal("LIMSIdentifier");

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var row = new WipPaketAna
                        {
                            BelegMatchCode = GetInt(reader, oBelegMatchCode),
                            PA = GetString(reader, oPA),
                            PA_Datum = GetDateTime(reader, oPADatum),
                            Lieferdatum = GetDateTime(reader, oLieferdatum),
                            Kunde_Auftraggeber = GetString(reader, oKunde),
                            KundeName_Auftraggeber = GetString(reader, oKundeName),
                            PINummer = GetString(reader, oPINummer),
                            USD = GetInt(reader, oUSD),
                            KundenAuftrag = GetString(reader, oKundenAuftrag),
                            Charge1 = GetString(reader, oCharge1),
                            Charge2 = GetString(reader, oCharge2),
                            ArtikelNR = GetString(reader, oArtikelNR),
                            ArtikelName = GetString(reader, oArtikelName),
                            PBuDat = GetString(reader, oPBuDat),
                            FixParaList = GetString(reader, oFixParaList),
                            Menge = GetInt(reader, oMenge),
                            Endpreis = GetDecimal(reader, oEndpreis),
                            Konto = GetString(reader, oKonto),
                            KST = GetString(reader, oKST),
                            LIMSIdentifier = GetString(reader, oLIMSIdentifier)
                        };

                        // Zusätzlich alle Spalten unverändert mitnehmen - Grundlage für das XML an Sage
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row.Spalten.Add(new KeyValuePair<string, object>(
                                reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i)));
                        }
                        rows.Add(row);
                    }
                }
            }

            return rows;
        }

        public async Task<string> GetSageExportOrdnerAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand("select top 1 SageExportOrdner from dbo.Konfig order by ID;", connection))
            {
                await connection.OpenAsync(cancellationToken);
                var value = await command.ExecuteScalarAsync(cancellationToken);
                return value == null || value == DBNull.Value ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        public async Task<bool> GetSageFakturAsync(long userNr, CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand("select top 1 SageFaktu from dbo.zugriffEx where MitarbeiterNr = @NUMMUSR;", connection))
            {
                command.Parameters.Add("@NUMMUSR", SqlDbType.BigInt).Value = userNr;

                await connection.OpenAsync(cancellationToken);
                var value = await command.ExecuteScalarAsync(cancellationToken);

                // Kein Datensatz oder NULL = keine Berechtigung
                return value != null && value != DBNull.Value && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
            }
        }

        private static DateTime? GetDateTime(SqlDataReader r, int i) =>
            r.IsDBNull(i) ? (DateTime?)null : Convert.ToDateTime(r.GetValue(i), CultureInfo.InvariantCulture);

        // Convert statt GetInt32 & Co.: toleriert int/smallint/numeric, falls sich ein Spaltentyp ändert
        private static int? GetInt(SqlDataReader r, int i) =>
            r.IsDBNull(i) ? (int?)null : Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);

        private static short? GetShort(SqlDataReader r, int i) =>
            r.IsDBNull(i) ? (short?)null : Convert.ToInt16(r.GetValue(i), CultureInfo.InvariantCulture);

        private static decimal? GetDecimal(SqlDataReader r, int i) =>
            r.IsDBNull(i) ? (decimal?)null : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);

        private static string GetString(SqlDataReader r, int i) =>
            r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture);
    }
}
