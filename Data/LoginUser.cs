using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace WipSageWeb.Data
{
    /// <summary>Datensatz aus der Tabelle Zugriff, wie ihn GetLoginDetails liefert.</summary>
    public sealed class LoginDetails
    {
        public string Kurzname { get; set; }
        public string Kennwort { get; set; }
        public decimal Laborleiter { get; set; }
    }

    /// <summary>Der angemeldete LaDIS-Benutzer.</summary>
    public sealed class LoginUser
    {
        public long UserNr { get; set; }
        public string Kurzname { get; set; }

        /// <summary>0 = PB-Wesen, 1 = Laborleiter, 2 = Superuser (wie loginData[3] in doLogin).</summary>
        public int Role { get; set; }

        /// <summary>true, wenn ZugriffEx.SageFaktu = 1 - nur dann darf der Benutzer Daten sehen und exportieren.</summary>
        public bool SageFaktur { get; set; }
    }

    /// <summary>Hält die Anmeldung in der Server-Session; im Browser liegt nur das Session-Cookie.</summary>
    public static class LoginSession
    {
        private const string UserNrKey = "UserNr";
        private const string KurznameKey = "Kurzname";
        private const string RoleKey = "Role";
        private const string SageFakturKey = "SageFaktur";

        public static LoginUser GetLoginUser(this ISession session)
        {
            var userNr = session.GetString(UserNrKey);
            if (string.IsNullOrEmpty(userNr))
            {
                return null;
            }
            return new LoginUser
            {
                UserNr = long.Parse(userNr, CultureInfo.InvariantCulture),
                Kurzname = session.GetString(KurznameKey),
                Role = session.GetInt32(RoleKey) ?? 0,
                SageFaktur = session.GetInt32(SageFakturKey) == 1
            };
        }

        public static void SetLoginUser(this ISession session, LoginUser user)
        {
            session.SetString(UserNrKey, user.UserNr.ToString(CultureInfo.InvariantCulture));
            session.SetString(KurznameKey, user.Kurzname ?? "");
            session.SetInt32(RoleKey, user.Role);
            session.SetInt32(SageFakturKey, user.SageFaktur ? 1 : 0);
        }
    }
}
