using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
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
        private const string GetWipPaketAnaProc = "dbo.WIPSage_ProcR_getWIP_PaketAnaEx";

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

        public async Task<IReadOnlyList<WipPaketAna>> GetWipPaketAnaAsync(int pa, CancellationToken cancellationToken = default(CancellationToken))
        {
            var rows = new List<WipPaketAna>();

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(GetWipPaketAnaProc, connection) { CommandType = CommandType.StoredProcedure })
            {
                command.Parameters.Add("@PA", SqlDbType.Int).Value = pa;

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
                    int oUlfd = reader.GetOrdinal("Ulfd");
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
                            Ulfd = GetInt(reader, oUlfd),
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

        // ---------------------------------------------------------------------------------------------
        // Sperrvermerk in dbo.SageHDR (siehe SageSperre). Alle Zeitvergleiche mit der Uhr des SQL Servers.
        // ---------------------------------------------------------------------------------------------

        // Abgelaufene Sperren aus SageHDR löschen (Browser geschlossen, ohne dass die Freigabe ankam)
        private const string AbgelaufeneLoeschenSql = @"
                                                        UPDATE dbo.SageHDR
                                                        SET GesperrtVon = NULL, GesperrtUserNr = NULL, GesperrtSitzung = NULL, GesperrtBis = NULL
                                                        WHERE GesperrtBis < GETDATE();";

                                                                // Frei = keine Sperre, abgelaufene Sperre oder Sperre desselben Benutzers
        private const string FreiFuerBenutzer = "(h.GesperrtBis IS NULL OR h.GesperrtBis < GETDATE() OR h.GesperrtUserNr = @UserNr)";

        private const string SperrenSql = @"
                                            SET NOCOUNT ON;
                                            SET XACT_ABORT ON;

                                            DECLARE @p TABLE (PA int PRIMARY KEY);
                                            INSERT INTO @p (PA)
                                            SELECT DISTINCT x.v.value('.', 'int')
                                            FROM @PAs.nodes('/p') AS x(v);

                                            BEGIN TRANSACTION;
                                                " + AbgelaufeneLoeschenSql + @"

                                                -- Kopfsatz für jeden PA anlegen, den es in SageHDR noch nicht gibt.
                                                -- UPDLOCK, HOLDLOCK: zwei gleichzeitige Aufrufe legen denselben PA nicht doppelt an.
                                                INSERT INTO dbo.SageHDR (PA, AngelegtAm, AngelegtVon, BelegTyp, Status)
                                                SELECT p.PA, GETDATE(), @Kurzname, 0, @StatusNeu
                                                FROM @p p
                                                WHERE NOT EXISTS (SELECT 1 FROM dbo.SageHDR h WITH (UPDLOCK, HOLDLOCK) WHERE h.PA = p.PA);

                                                -- Freie und eigene PAs sperren, solange für sie noch keine XML-Datei erzeugt wurde
                                                UPDATE h
                                                SET   h.GesperrtVon     = @Kurzname
                                                    , h.GesperrtUserNr  = @UserNr
                                                    , h.GesperrtSitzung = @Sitzung
                                                    , h.GesperrtBis     = DATEADD(MINUTE, @Minuten, GETDATE())
                                                FROM dbo.SageHDR h
                                                INNER JOIN @p p ON p.PA = h.PA
                                                WHERE h.DateiErstelltAm IS NULL
                                                    AND " + FreiFuerBenutzer + @";

                                                COMMIT TRANSACTION;

                                                SELECT h.PA, h.GesperrtVon, h.GesperrtBis, h.Datei, h.DateiErstelltAm
                                                        , CAST(CASE WHEN h.GesperrtBis >= GETDATE() AND ISNULL(h.GesperrtUserNr, -1) <> @UserNr THEN 1 ELSE 0 END AS bit) AS GesperrtFuerAndere
                                                        , h.DateiErstelltVon
                                                FROM dbo.SageHDR h
                                                INNER JOIN @p p ON p.PA = h.PA;";

        public async Task<IReadOnlyDictionary<int, SageHdrZustand>> SperrenAsync(IEnumerable<int> pas, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new Dictionary<int, SageHdrZustand>();
            var liste = pas.Distinct().ToList();
            if (liste.Count == 0)
            {
                return result;
            }

            // PAs als XML (<p>41001</p><p>41002</p>) - funktioniert mit jeder SQL-Server-Version
            var xml = string.Concat(liste.Select(pa => "<p>" + pa.ToString(CultureInfo.InvariantCulture) + "</p>"));

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(SperrenSql, connection))
            {
                command.Parameters.Add("@PAs", SqlDbType.Xml).Value = xml;
                AddInhaber(command, inhaber);

                await connection.OpenAsync(cancellationToken);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var zustand = new SageHdrZustand
                        {
                            PA = reader.GetInt32(0),
                            GesperrtVon = GetString(reader, 1),
                            // GesperrtBis: Ablaufzeit der Sperre. Bis zu diesem Zeitpunkt gehört der PA dem Benutzer,
                            // der ihn gesperrt hat; danach gilt er wieder als frei.
                            //
                            // Eine Web-App erfährt nicht zuverlässig, wann jemand sie verlässt. Stürzt der Browser ab,
                            // fährt der Rechner herunter oder kommt die Abmeldung beim Schließen nicht an, wird die
                            // Sperre nie aktiv gelöscht. Ohne Ablaufzeit blieben solche PAs für alle anderen dauerhaft
                            // gesperrt, bis jemand die Sperre von Hand aus SageHDR entfernt.
                            //
                            // So arbeitet die App damit:
                            // - Beim Sperren: GesperrtBis wird auf jetzt plus 3 Minuten gesetzt.
                            // - Solange die Seite offen ist: Sie meldet sich jede Minute beim Server und schiebt
                            //   GesperrtBis wieder 3 Minuten nach vorn. Eine aktive Sperre läuft so nie ab.
                            // - Wenn die Seite weg ist: Die Meldungen bleiben aus, und spätestens 3 Minuten später
                            //   ist GesperrtBis überschritten.
                            // - Prüfung, ob ein PA gesperrt ist: Ein PA gilt nur dann als von einem anderen gesperrt,
                            //   wenn GesperrtBis noch in der Zukunft liegt. Abgelaufene Sperren zählen nicht, auch wenn
                            //   die übrigen Felder noch gefüllt sind.
                            // - Aufräumen: Bei jedem Laden der Liste und bei jeder Verlängerung löscht der Server alle
                            //   abgelaufenen Sperren aus SageHDR.
                            GesperrtBis = GetDateTime(reader, 2),
                            Datei = GetString(reader, 3),
                            DateiErstelltAm = GetDateTime(reader, 4),
                            GesperrtFuerAndere = reader.GetBoolean(5),
                            DateiErstelltVon = GetString(reader, 6)
                        };
                        result[zustand.PA] = zustand;
                    }
                }
            }
            return result;
        }

        public async Task<int> SperrenVerlaengernAsync(SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            const string sql = @"
                                SET NOCOUNT ON;
                                " + AbgelaufeneLoeschenSql + @"
                                SET NOCOUNT OFF;
                                -- Alle noch gültigen Sperren des Benutzers verlängern - auch die von PAs,
                                -- deren XML-Datei er schon erzeugt hat (sie bleiben bis zum Verlassen der Seite gesperrt)
                                UPDATE h
                                SET h.GesperrtBis = DATEADD(MINUTE, @Minuten, GETDATE())
                                FROM dbo.SageHDR h
                                WHERE h.GesperrtUserNr = @UserNr
                                  AND h.GesperrtBis >= GETDATE();";
            return await ExecuteAsync(sql, inhaber, null, cancellationToken);
        }

        public async Task<int> SperrenFreigebenAsync(SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            const string sql = @"
                                UPDATE h
                                SET   h.GesperrtVon = NULL
                                    , h.GesperrtUserNr = NULL
                                    , h.GesperrtSitzung = NULL
                                    , h.GesperrtBis = NULL
                                FROM dbo.SageHDR h
                                WHERE h.GesperrtSitzung = @Sitzung;";
            return await ExecuteAsync(sql, inhaber, null, cancellationToken);
        }

        public async Task<ExportAnspruch> ExportBeanspruchenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Ein einziges UPDATE mit Bedingung: von zwei gleichzeitigen Aufrufen kann nur einer gewinnen
            const string sql = @"
                                SET NOCOUNT ON;

                                UPDATE h
                                SET   h.DateiErstelltAm  = GETDATE()
                                    , h.DateiErstelltVon = @Kurzname
                                    , h.GesperrtVon     = @Kurzname
                                    , h.GesperrtUserNr  = @UserNr
                                    , h.GesperrtSitzung = @Sitzung
                                    , h.GesperrtBis     = DATEADD(MINUTE, @Minuten, GETDATE())
                                FROM dbo.SageHDR h
                                WHERE h.PA = @PA
                                  AND h.DateiErstelltAm IS NULL
                                  AND " + FreiFuerBenutzer + @";

                                DECLARE @Erteilt bit = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;

                                SELECT @Erteilt AS Erteilt
                                     , CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.SageHDR WHERE PA = @PA) THEN 1 ELSE 0 END AS bit) AS Vorhanden
                                     , (SELECT GesperrtVon FROM dbo.SageHDR WHERE PA = @PA) AS GesperrtVon
                                     , (SELECT Datei FROM dbo.SageHDR WHERE PA = @PA) AS Datei
                                     , (SELECT DateiErstelltAm FROM dbo.SageHDR WHERE PA = @PA) AS DateiErstelltAm
                                     , (SELECT DateiErstelltVon FROM dbo.SageHDR WHERE PA = @PA) AS DateiErstelltVon;";

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.Add("@PA", SqlDbType.Int).Value = pa;
                AddInhaber(command, inhaber);

                await connection.OpenAsync(cancellationToken);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    await reader.ReadAsync(cancellationToken);
                    return new ExportAnspruch
                    {
                        Erteilt = reader.GetBoolean(0),
                        Vorhanden = reader.GetBoolean(1),
                        GesperrtVon = GetString(reader, 2),
                        Datei = GetString(reader, 3),
                        DateiErstelltAm = GetDateTime(reader, 4),
                        DateiErstelltVon = GetString(reader, 5)
                    };
                }
            }
        }

        public async Task ExportAbschliessenAsync(int pa, SperrInhaber inhaber, string datei, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Die XML-Datei ist geschrieben: Dateiname (ohne Pfad) und Ersteller eintragen, Zeitpunkt aktualisieren.
            // Der Satz bleibt für den Benutzer gesperrt - die Sperre endet erst beim Verlassen der Seite (oder läuft ab).
            const string sql = @"
                                UPDATE h
                                SET   h.Datei            = @Datei
                                    , h.DateiErstelltAm  = GETDATE()
                                    , h.DateiErstelltVon = @Kurzname
                                    , h.Status           = @StatusXmlErzeugt
                                    , h.MessageVonSage   = NULL   -- Meldung zu einer früheren Datei gilt nicht mehr
                                    , h.GesperrtBis      = DATEADD(MINUTE, @Minuten, GETDATE())
                                FROM dbo.SageHDR h
                                WHERE h.PA = @PA
                                  AND h.Datei IS NULL
                                  AND h.DateiErstelltAm IS NOT NULL
                                  AND h.GesperrtUserNr = @UserNr;

                                IF @@ROWCOUNT > 0
                                    UPDATE p
                                    SET p.Status = @StatusXmlErzeugt, p.MessageVonSage = NULL
                                    FROM dbo.SagePos p
                                    INNER JOIN dbo.SageHDR h ON h.id = p.FK_SageHDR
                                    WHERE h.PA = @PA;";
            await ExecuteAsync(sql, inhaber, command =>
            {
                command.Parameters.Add("@PA", SqlDbType.Int).Value = pa;
                command.Parameters.Add("@Datei", SqlDbType.NVarChar, 100).Value = datei;
            }, cancellationToken);
        }

        public async Task ExportZuruecknehmenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Die Datei konnte nicht geschrieben werden: Anspruch zurücknehmen, der PA ist wieder exportierbar
            const string sql = @"
                                UPDATE h
                                SET h.DateiErstelltAm = NULL, h.DateiErstelltVon = NULL, h.Status = @StatusNeu
                                FROM dbo.SageHDR h
                                WHERE h.PA = @PA
                                  AND h.Datei IS NULL
                                  AND h.GesperrtUserNr = @UserNr;";
            await ExecuteAsync(sql, inhaber, command =>
            {
                command.Parameters.Add("@PA", SqlDbType.Int).Value = pa;
            }, cancellationToken);
        }

        public async Task<NeuErstellenAnspruch> ExportZuruecksetzenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Alte Werte lesen und Satz zurücksetzen in einer Transaktion: von zwei gleichzeitigen Aufrufen
            // (oder einem laufenden Export desselben PA) kann nur einer gewinnen
            const string sql = @"
                                SET NOCOUNT ON;
                                SET XACT_ABORT ON;

                                BEGIN TRANSACTION;
                                " + AbgelaufeneLoeschenSql + @"

                                DECLARE @AlteDatei nvarchar(100), @AlterZeitpunkt datetime2, @AlterErsteller nvarchar(30), @AlterStatus tinyint, @AlteMessage nvarchar(200);
                                SELECT @AlteDatei = h.Datei, @AlterZeitpunkt = h.DateiErstelltAm, @AlterErsteller = h.DateiErstelltVon, @AlterStatus = h.Status, @AlteMessage = h.MessageVonSage
                                FROM dbo.SageHDR h WITH (UPDLOCK, HOLDLOCK)
                                WHERE h.PA = @PA;

                                UPDATE h
                                SET   h.Datei            = NULL
                                    , h.DateiErstelltAm  = NULL
                                    , h.DateiErstelltVon = NULL
                                    , h.Status           = @StatusNeu
                                    , h.MessageVonSage   = NULL
                                    , h.GesperrtVon     = @Kurzname
                                    , h.GesperrtUserNr  = @UserNr
                                    , h.GesperrtSitzung = @Sitzung
                                    , h.GesperrtBis     = DATEADD(MINUTE, @Minuten, GETDATE())
                                FROM dbo.SageHDR h
                                WHERE h.PA = @PA
                                    AND h.DateiErstelltAm IS NOT NULL
                                    AND " + FreiFuerBenutzer + @";

                                DECLARE @Erteilt bit = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;
                                COMMIT TRANSACTION;

                                SELECT @Erteilt AS Erteilt
                                        , CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.SageHDR WHERE PA = @PA) THEN 1 ELSE 0 END AS bit) AS Vorhanden
                                        , (SELECT GesperrtVon FROM dbo.SageHDR WHERE PA = @PA) AS GesperrtVon
                                        , @AlteDatei AS AlteDatei
                                        , @AlterZeitpunkt AS AlterZeitpunkt
                                        , @AlterErsteller AS AlterErsteller
                                        , @AlterStatus AS AlterStatus
                                        , @AlteMessage AS AlteMessage;";

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.Add("@PA", SqlDbType.Int).Value = pa;
                AddInhaber(command, inhaber);

                await connection.OpenAsync(cancellationToken);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    await reader.ReadAsync(cancellationToken);
                    return new NeuErstellenAnspruch
                    {
                        Erteilt = reader.GetBoolean(0),
                        Vorhanden = reader.GetBoolean(1),
                        GesperrtVon = GetString(reader, 2),
                        AlteDatei = GetString(reader, 3),
                        AlterZeitpunkt = GetDateTime(reader, 4),
                        AlterErsteller = GetString(reader, 5),
                        AlterStatus = reader.IsDBNull(6) ? (SageStatus?)null : (SageStatus)Convert.ToByte(reader.GetValue(6), CultureInfo.InvariantCulture),
                        AlteMessageVonSage = GetString(reader, 7)
                    };
                }
            }
        }

        public async Task ExportWiederherstellenAsync(int pa, SperrInhaber inhaber, string datei, DateTime? erstelltAm, string erstelltVon, SageStatus? status, string messageVonSage, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Nur der Satz, den dieser Benutzer eben zurückgesetzt hat (noch ohne Datei, noch seine Sperre)
            const string sql = @"
                                UPDATE h
                                SET   h.Datei           = @Datei
                                    , h.DateiErstelltAm = @ErstelltAm
                                    , h.DateiErstelltVon = @ErstelltVon
                                    , h.Status           = @Status
                                    , h.MessageVonSage   = @MessageVonSage
                                    , h.GesperrtVon     = NULL
                                    , h.GesperrtUserNr  = NULL
                                    , h.GesperrtSitzung = NULL
                                    , h.GesperrtBis     = NULL
                                FROM dbo.SageHDR h
                                WHERE h.PA = @PA
                                    AND h.Datei IS NULL
                                    AND h.DateiErstelltAm IS NULL
                                    AND h.GesperrtUserNr = @UserNr;";
            await ExecuteAsync(sql, inhaber, command =>
            {
                command.Parameters.Add("@PA", SqlDbType.Int).Value = pa;
                command.Parameters.Add("@Datei", SqlDbType.NVarChar, 100).Value = (object)datei ?? DBNull.Value;
                command.Parameters.Add("@ErstelltAm", SqlDbType.DateTime2).Value = (object)erstelltAm ?? DBNull.Value;
                command.Parameters.Add("@ErstelltVon", SqlDbType.NVarChar, 30).Value = (object)erstelltVon ?? DBNull.Value;
                command.Parameters.Add("@Status", SqlDbType.TinyInt).Value = status.HasValue ? (object)(byte)status.Value : DBNull.Value;
                command.Parameters.Add("@MessageVonSage", SqlDbType.NVarChar, 200).Value = (object)messageVonSage ?? DBNull.Value;
            }, cancellationToken);
        }

        public async Task<int> SagePosErsetzenAsync(int pa, IReadOnlyList<SagePosZeile> zeilen, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Zeilen als XML übergeben (<r pi=".." u=".." a=".." n=".."/>) - keine Grenze bei der Anzahl der Parameter
            const string sql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @HdrID int = (SELECT id FROM dbo.SageHDR WHERE PA = @PA);
IF @HdrID IS NULL
	THROW 50004, N'SagePos: Für den PA gibt es keinen Eintrag in SageHDR.', 1;

BEGIN TRANSACTION;

-- 1. Bisherige Zeilen dieses PA löschen
DELETE FROM dbo.SagePos
WHERE FK_SageHDR = @HdrID;

-- 2. Zeilen neu anlegen
INSERT INTO dbo.SagePos (FK_SageHDR, PI, Ulfd, ArtikelNr, ArtikelName, Status)
SELECT @HdrID
     , x.r.value('@pi', 'nvarchar(10)')
     , x.r.value('@u',  'tinyint')
     , x.r.value('@a',  'nvarchar(10)')
     , x.r.value('@n',  'nvarchar(40)')
     , @StatusNeu   -- nach dem Schreiben der XML-Datei: @StatusXmlErzeugt
FROM @Zeilen.nodes('/r') AS x(r);

DECLARE @Anzahl int = @@ROWCOUNT;

COMMIT TRANSACTION;

SELECT @Anzahl;";

            var xml = string.Concat(zeilen.Select(z => new System.Xml.Linq.XElement("r",
                new System.Xml.Linq.XAttribute("pi", z.PI),
                new System.Xml.Linq.XAttribute("u", z.Ulfd.ToString(CultureInfo.InvariantCulture)),
                new System.Xml.Linq.XAttribute("a", z.ArtikelNr),
                new System.Xml.Linq.XAttribute("n", z.ArtikelName ?? "")).ToString()));

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.Add("@PA", SqlDbType.Int).Value = pa;
                command.Parameters.Add("@Zeilen", SqlDbType.Xml).Value = xml;
                AddStatus(command);

                await connection.OpenAsync(cancellationToken);
                return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }
        }

        public async Task<IReadOnlyDictionary<int, SageRueckmeldung>> GetSageHdrRueckmeldungAsync(IEnumerable<int> pas, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new Dictionary<int, SageRueckmeldung>();
            var liste = pas.Distinct().ToList();
            if (liste.Count == 0)
            {
                return result;
            }

            const string sql = @"
                                SELECT h.PA, h.Status, h.MessageVonSage
                                FROM dbo.SageHDR h
                                INNER JOIN (SELECT DISTINCT x.v.value('.', 'int') AS PA FROM @PAs.nodes('/p') AS x(v)) p ON p.PA = h.PA
                                WHERE h.Status IS NOT NULL OR h.MessageVonSage IS NOT NULL;";

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.Add("@PAs", SqlDbType.Xml).Value =
                    string.Concat(liste.Select(pa => "<p>" + pa.ToString(CultureInfo.InvariantCulture) + "</p>"));

                await connection.OpenAsync(cancellationToken);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        result[reader.GetInt32(0)] = new SageRueckmeldung
                        {
                            Status = reader.IsDBNull(1) ? (SageStatus?)null : (SageStatus)Convert.ToByte(reader.GetValue(1), CultureInfo.InvariantCulture),
                            MessageVonSage = GetString(reader, 2)
                        };
                    }
                }
            }
            return result;
        }

        public async Task<IReadOnlyDictionary<(string PI, int Ulfd), SageRueckmeldung>> GetSagePosRueckmeldungAsync(int pa, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new Dictionary<(string PI, int Ulfd), SageRueckmeldung>();
            const string sql = @"
                                SELECT p.PI, p.Ulfd, p.Status, p.MessageVonSage
                                FROM dbo.SagePos p
                                INNER JOIN dbo.SageHDR h ON h.id = p.FK_SageHDR
                                WHERE h.PA = @PA
                                  AND (p.Status IS NOT NULL OR p.MessageVonSage IS NOT NULL);";

            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                command.Parameters.Add("@PA", SqlDbType.Int).Value = pa;

                await connection.OpenAsync(cancellationToken);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var key = ((GetString(reader, 0) ?? "").Trim().ToUpperInvariant(), Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture));
                        result[key] = new SageRueckmeldung
                        {
                            Status = reader.IsDBNull(2) ? (SageStatus?)null : (SageStatus)Convert.ToByte(reader.GetValue(2), CultureInfo.InvariantCulture),
                            MessageVonSage = GetString(reader, 3)
                        };
                    }
                }
            }
            return result;
        }

        private async Task<int> ExecuteAsync(string sql, SperrInhaber inhaber, Action<SqlCommand> parameters, CancellationToken cancellationToken)
        {
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                AddInhaber(command, inhaber);
                parameters?.Invoke(command);
                await connection.OpenAsync(cancellationToken);
                return await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private static void AddInhaber(SqlCommand command, SperrInhaber inhaber)
        {
            command.Parameters.Add("@UserNr", SqlDbType.Int).Value = (int)inhaber.UserNr;
            command.Parameters.Add("@Kurzname", SqlDbType.NVarChar, 30).Value = (object)Truncate(inhaber.Kurzname, 30) ?? DBNull.Value;
            command.Parameters.Add("@Sitzung", SqlDbType.NVarChar, 40).Value = inhaber.Sitzung;
            command.Parameters.Add("@Minuten", SqlDbType.Int).Value = SageSperre.Minuten;
            AddStatus(command);
        }

        // Statuswerte für SageHDR.Status und SagePos.Status aus dem Enum SageStatus - keine festen Zahlen im SQL
        private static void AddStatus(SqlCommand command)
        {
            command.Parameters.Add("@StatusNeu", SqlDbType.TinyInt).Value = (byte)SageStatus.Neu;
            command.Parameters.Add("@StatusXmlErzeugt", SqlDbType.TinyInt).Value = (byte)SageStatus.XmlErzeugt;
        }

        private static string Truncate(string value, int length) =>
            value == null || value.Length <= length ? value : value.Substring(0, length);

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
