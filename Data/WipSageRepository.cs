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

        public async Task<SageKonfig> GetSageKonfigAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            using (var connection = new SqlConnection(_connectionString))
            // SageTest entscheidet, welche Adresse gilt: true -> SageTestExportURL, false -> SageReleaseExportURL
            using (var command = new SqlCommand("select top 1 SageTest, SageTestExportURL, SageReleaseExportURL from dbo.Konfig order by ID;", connection))
            {
                await connection.OpenAsync(cancellationToken);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        return new SageKonfig();
                    }
                    var test = IstWahr(reader.IsDBNull(0) ? null : reader.GetValue(0));
                    return new SageKonfig
                    {
                        Test = test,
                        ExportUrl = GetString(reader, test ? 1 : 2),
                        ExportUrlFeld = test ? "SageTestExportURL" : "SageReleaseExportURL"
                    };
                }
            }
        }

        // SageTest als bit, Zahl oder Text: true/1 = Testbetrieb, alles andere (auch NULL) = echter Betrieb
        private static bool IstWahr(object value)
        {
            if (value == null)
            {
                return false;
            }
            if (value is bool flag)
            {
                return flag;
            }
            var text = Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
            return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
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

        // Übergeben = Status "XML erzeugt" (gesendet bzw. wird gerade gesendet) oder "Alles ok in Sage".
        // Noch übergebbar sind "neu" (auch NULL) und "Fehler in Sage".
        private const string NochNichtUebergeben = "ISNULL(h.Status, @StatusNeu) NOT IN (@StatusXmlErzeugt, @StatusAllesOk)";
        private const string Uebergeben = "ISNULL(h.Status, @StatusNeu) IN (@StatusXmlErzeugt, @StatusAllesOk)";

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
                                                INSERT INTO dbo.SageHDR (PA, AngelegtAm, AngelegtVon, Status)
                                                SELECT p.PA, GETDATE(), @Kurzname, @StatusNeu
                                                FROM @p p
                                                WHERE NOT EXISTS (SELECT 1 FROM dbo.SageHDR h WITH (UPDLOCK, HOLDLOCK) WHERE h.PA = p.PA);

                                                -- Freie und eigene PAs sperren, solange sie noch nicht an Sage übergeben sind
                                                UPDATE h
                                                SET   h.GesperrtVon     = @Kurzname
                                                    , h.GesperrtUserNr  = @UserNr
                                                    , h.GesperrtSitzung = @Sitzung
                                                    , h.GesperrtBis     = DATEADD(MINUTE, @Minuten, GETDATE())
                                                FROM dbo.SageHDR h
                                                INNER JOIN @p p ON p.PA = h.PA
                                                WHERE " + NochNichtUebergeben + @"
                                                    AND " + FreiFuerBenutzer + @";

                                                COMMIT TRANSACTION;

                                                SELECT h.PA, h.GesperrtVon, h.GesperrtBis, h.Status
                                                        , CAST(CASE WHEN h.GesperrtBis >= GETDATE() AND ISNULL(h.GesperrtUserNr, -1) <> @UserNr THEN 1 ELSE 0 END AS bit) AS GesperrtFuerAndere
                                                        , h.AnSageGesendetAm, h.AnSageGesendetVon
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
                            Status = GetStatus(reader, 3),
                            GesperrtFuerAndere = reader.GetBoolean(4),
                            AnSageGesendetAm = GetDateTime(reader, 5),
                            AnSageGesendetVon = GetString(reader, 6)
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
                                -- die er schon an Sage übergeben hat (sie bleiben bis zum Verlassen der Seite gesperrt)
                                UPDATE h
                                SET h.GesperrtBis = DATEADD(MINUTE, @Minuten, GETDATE())
                                FROM dbo.SageHDR h
                                WHERE h.GesperrtUserNr = @UserNr
                                  AND h.GesperrtBis >= GETDATE();" ;
            return await ExecuteAsync(sql, inhaber, null, cancellationToken);
        }

        public async Task<int> SperrenFreigebenAsync(SperrInhaber inhaber, bool ganzeAnmeldung, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Seite verlassen: nur die Sperren dieser Seite ("Anmeldung:Seite"). Abmelden: alle Seiten der Anmeldung
            // ("Anmeldung:%") - dazu Sperren im alten Format ohne Seite (nur "Anmeldung").
            // Die Kennungen bestehen nur aus Hex-Zeichen (SperrKennung), LIKE-Platzhalter können nicht vorkommen.
            const string sql = @"
                                UPDATE h
                                SET   h.GesperrtVon = NULL
                                    , h.GesperrtUserNr = NULL
                                    , h.GesperrtSitzung = NULL
                                    , h.GesperrtBis = NULL
                                FROM dbo.SageHDR h
                                WHERE h.GesperrtSitzung = @Sitzung
                                   OR (@GanzeAnmeldung = 1 AND (h.GesperrtSitzung LIKE @Anmeldung + N':%' OR h.GesperrtSitzung = @Anmeldung));";
            return await ExecuteAsync(sql, inhaber, command =>
            {
                command.Parameters.Add("@GanzeAnmeldung", SqlDbType.Bit).Value = ganzeAnmeldung;
                command.Parameters.Add("@Anmeldung", SqlDbType.NVarChar, 40).Value = inhaber.Anmeldung;
            }, cancellationToken);
        }

        public async Task<ExportAnspruch> ExportBeanspruchenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Ein einziges UPDATE mit Bedingung: von zwei gleichzeitigen Aufrufen kann nur einer gewinnen.
            // Status "XML erzeugt" und AnSageGesendetAm/-Von werden direkt vor dem Senden gesetzt; die alten Werte
            // kommen zurück, damit sie wiederhergestellt werden können, falls das Senden scheitert.
            const string sql = @"
                                SET NOCOUNT ON;

                                DECLARE @Alt TABLE (Status tinyint NULL, GesendetAm datetime NULL, GesendetVon nvarchar(30) NULL);

                                UPDATE h
                                SET   h.Status            = @StatusXmlErzeugt
                                    , h.AnSageGesendetAm  = GETDATE()
                                    , h.AnSageGesendetVon = @Kurzname
                                    , h.GesperrtVon       = @Kurzname
                                    , h.GesperrtUserNr    = @UserNr
                                    , h.GesperrtSitzung   = @Sitzung
                                    , h.GesperrtBis       = DATEADD(MINUTE, @Minuten, GETDATE())
                                OUTPUT deleted.Status, deleted.AnSageGesendetAm, deleted.AnSageGesendetVon INTO @Alt
                                FROM dbo.SageHDR h
                                WHERE h.PA = @PA
                                  AND " + NochNichtUebergeben + @"
                                  AND " + FreiFuerBenutzer + @";

                                DECLARE @Erteilt bit = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;

                                SELECT @Erteilt AS Erteilt
                                     , CAST(CASE WHEN h.id IS NULL THEN 0 ELSE 1 END AS bit) AS Vorhanden
                                     , h.GesperrtVon
                                     , h.Status
                                     , h.AnSageGesendetAm
                                     , h.AnSageGesendetVon
                                     , a.Status      AS AlterStatus
                                     , a.GesendetAm  AS AltGesendetAm
                                     , a.GesendetVon AS AltGesendetVon
                                FROM (SELECT 1 AS x) d
                                LEFT JOIN dbo.SageHDR h ON h.PA = @PA
                                LEFT JOIN @Alt a ON 1 = 1;";

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
                        Status = GetStatus(reader, 3),
                        AnSageGesendetAm = GetDateTime(reader, 4),
                        AnSageGesendetVon = GetString(reader, 5),
                        AlterStatus = GetStatus(reader, 6),
                        AltGesendetAm = GetDateTime(reader, 7),
                        AltGesendetVon = GetString(reader, 8)
                    };
                }
            }
        }

        public async Task ExportAbschliessenAsync(int pa, SperrInhaber inhaber, SageStatus status, string messageVonSage,
            int? officeLineHandle, int? officeLineVorgangsHandle, IReadOnlyList<SagePosHandle> posHandles, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Antwort von Sage ist da: Status, Meldung von Sage und die OfficeLineHandle in SageHDR und SagePos eintragen.
            // Nur der Satz, den dieser Benutzer eben beansprucht hat (Status "XML erzeugt", seine Sperre).
            // Der Satz bleibt für den Benutzer gesperrt - die Sperre endet erst beim Verlassen der Seite (oder läuft ab).
            // Handles der Positionen als XML (<r pi=".." u=".." h=".."/>), Zuordnung über PI und Ulfd.
            const string sql = @"
                                SET NOCOUNT ON;
                                SET XACT_ABORT ON;

                                BEGIN TRANSACTION;

                                UPDATE h
                                SET   h.Status           = @Status
                                    , h.MessageVonSage   = @MessageVonSage
                                    , h.OfficeLineHandle = @OfficeLineHandle
                                    , h.OfficeLineVorgangsHandle = @OfficeLineVorgangsHandle
                                    , h.GesperrtBis      = DATEADD(MINUTE, @Minuten, GETDATE())
                                FROM dbo.SageHDR h
                                WHERE h.PA = @PA
                                  AND h.Status = @StatusXmlErzeugt
                                  AND h.GesperrtUserNr = @UserNr;

                                IF @@ROWCOUNT > 0
                                    UPDATE p
                                    SET   p.Status           = @Status
                                        , p.MessageVonSage   = @MessageVonSage
                                        , p.OfficeLineHandle = x.Handle
                                    FROM dbo.SagePos p
                                    INNER JOIN dbo.SageHDR h ON h.id = p.FK_SageHDR
                                    LEFT JOIN (SELECT r.v.value('@pi', 'nvarchar(10)') AS PI
                                                    , r.v.value('@u',  'tinyint')      AS Ulfd
                                                    , r.v.value('@h',  'int')          AS Handle
                                               FROM @PosHandles.nodes('/r') AS r(v)) x ON x.PI = p.PI AND x.Ulfd = p.Ulfd
                                    WHERE h.PA = @PA;

                                COMMIT TRANSACTION;";

            var xml = string.Concat((posHandles ?? new SagePosHandle[0]).Select(z => new System.Xml.Linq.XElement("r",
                new System.Xml.Linq.XAttribute("pi", z.PI),
                new System.Xml.Linq.XAttribute("u", z.Ulfd.ToString(CultureInfo.InvariantCulture)),
                new System.Xml.Linq.XAttribute("h", z.OfficeLineHandle.ToString(CultureInfo.InvariantCulture))).ToString()));

            await ExecuteAsync(sql, inhaber, command =>
            {
                command.Parameters.Add("@PA", SqlDbType.Int).Value = pa;
                command.Parameters.Add("@Status", SqlDbType.TinyInt).Value = (byte)status;
                command.Parameters.Add("@MessageVonSage", SqlDbType.NVarChar, 200).Value = (object)Truncate(messageVonSage, 200) ?? DBNull.Value;
                command.Parameters.Add("@OfficeLineHandle", SqlDbType.Int).Value = (object)officeLineHandle ?? DBNull.Value;
                command.Parameters.Add("@OfficeLineVorgangsHandle", SqlDbType.Int).Value = (object)officeLineVorgangsHandle ?? DBNull.Value;
                command.Parameters.Add("@PosHandles", SqlDbType.Xml).Value = xml;
            }, cancellationToken);
        }

        public async Task ExportZuruecknehmenAsync(int pa, SperrInhaber inhaber, ExportAnspruch anspruch, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Der Auftrag konnte nicht an Sage gesendet werden: Status und AnSageGesendetAm/-Von wie vor dem
            // Beanspruchen, der PA ist wieder übergebbar
            const string sql = @"
                                UPDATE h
                                SET   h.Status            = @AlterStatus
                                    , h.AnSageGesendetAm  = @AltGesendetAm
                                    , h.AnSageGesendetVon = @AltGesendetVon
                                FROM dbo.SageHDR h
                                WHERE h.PA = @PA
                                  AND h.Status = @StatusXmlErzeugt
                                  AND h.GesperrtUserNr = @UserNr;";
            await ExecuteAsync(sql, inhaber, command =>
            {
                command.Parameters.Add("@PA", SqlDbType.Int).Value = pa;
                command.Parameters.Add("@AlterStatus", SqlDbType.TinyInt).Value = (byte)(anspruch.AlterStatus ?? SageStatus.Neu);
                command.Parameters.Add("@AltGesendetAm", SqlDbType.DateTime).Value = (object)anspruch.AltGesendetAm ?? DBNull.Value;
                command.Parameters.Add("@AltGesendetVon", SqlDbType.NVarChar, 30).Value = (object)anspruch.AltGesendetVon ?? DBNull.Value;
            }, cancellationToken);
        }

        public async Task<NeuErstellenAnspruch> ExportZuruecksetzenAsync(int pa, SperrInhaber inhaber, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Übergebenen PA wieder auf "neu" setzen und für den Benutzer sperren - in einem UPDATE mit Bedingung:
            // von zwei gleichzeitigen Aufrufen (oder einem laufenden Export desselben PA) kann nur einer gewinnen.
            // AnSageGesendetAm/-Von bleiben stehen (letzte Übertragung), bis der PA erneut gesendet wird.
            const string sql = @"
                                SET NOCOUNT ON;
                                SET XACT_ABORT ON;

                                BEGIN TRANSACTION;
                                " + AbgelaufeneLoeschenSql + @"

                                UPDATE h
                                SET   h.Status          = @StatusNeu
                                    , h.MessageVonSage  = NULL
                                    , h.GesperrtVon     = @Kurzname
                                    , h.GesperrtUserNr  = @UserNr
                                    , h.GesperrtSitzung = @Sitzung
                                    , h.GesperrtBis     = DATEADD(MINUTE, @Minuten, GETDATE())
                                FROM dbo.SageHDR h
                                WHERE h.PA = @PA
                                    AND " + Uebergeben + @"
                                    AND " + FreiFuerBenutzer + @";

                                DECLARE @Erteilt bit = CASE WHEN @@ROWCOUNT > 0 THEN 1 ELSE 0 END;
                                COMMIT TRANSACTION;

                                SELECT @Erteilt AS Erteilt
                                        , CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.SageHDR WHERE PA = @PA) THEN 1 ELSE 0 END AS bit) AS Vorhanden
                                        , (SELECT GesperrtVon FROM dbo.SageHDR WHERE PA = @PA) AS GesperrtVon;";

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
                        GesperrtVon = GetString(reader, 2)
                    };
                }
            }
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
     , @StatusNeu   -- nach dem Senden an Sage: Status aus der Antwort
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
                                SELECT h.PA, h.Status, h.MessageVonSage, h.OfficeLineHandle, h.OfficeLineVorgangsHandle
                                FROM dbo.SageHDR h
                                INNER JOIN (SELECT DISTINCT x.v.value('.', 'int') AS PA FROM @PAs.nodes('/p') AS x(v)) p ON p.PA = h.PA
                                WHERE h.Status IS NOT NULL OR h.MessageVonSage IS NOT NULL OR h.OfficeLineHandle IS NOT NULL OR h.OfficeLineVorgangsHandle IS NOT NULL;";

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
                            Status = GetStatus(reader, 1),
                            MessageVonSage = GetString(reader, 2),
                            OfficeLineHandle = GetInt(reader, 3),
                            OfficeLineVorgangsHandle = GetInt(reader, 4)
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
                                SELECT p.PI, p.Ulfd, p.Status, p.MessageVonSage, p.OfficeLineHandle
                                FROM dbo.SagePos p
                                INNER JOIN dbo.SageHDR h ON h.id = p.FK_SageHDR
                                WHERE h.PA = @PA
                                  AND (p.Status IS NOT NULL OR p.MessageVonSage IS NOT NULL OR p.OfficeLineHandle IS NOT NULL);";

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
                            Status = GetStatus(reader, 2),
                            MessageVonSage = GetString(reader, 3),
                            OfficeLineHandle = GetInt(reader, 4)
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
            command.Parameters.Add("@StatusAllesOk", SqlDbType.TinyInt).Value = (byte)SageStatus.AllesOkInSage;
        }

        private static string Truncate(string value, int length) =>
            value == null || value.Length <= length ? value : value.Substring(0, length);

        private static SageStatus? GetStatus(SqlDataReader r, int i) =>
            r.IsDBNull(i) ? (SageStatus?)null : (SageStatus)Convert.ToByte(r.GetValue(i), CultureInfo.InvariantCulture);

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
