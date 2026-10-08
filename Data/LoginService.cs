using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace WipSageWeb.Data
{
    /// <summary>Abschnitt "Login" in appsettings.json.</summary>
    public class LoginOptions
    {
        /// <summary>Kommagetrennte Benutzernummern der Superuser (entspricht enabledSuperUsers).</summary>
        public string SuperUsers { get; set; }

        /// <summary>Adresse der aufrufenden Web-Applikation; dorthin geht es bei gescheiterter Anmeldung und beim Abmelden.</summary>
        public string ReturnUrl { get; set; }
    }

    public interface ILoginService
    {
        /// <summary>Liefert den Benutzer oder null, wenn die Anmeldung scheitert.</summary>
        Task<LoginUser> TryLoginAsync(string userNrText, string password, CancellationToken cancellationToken = default(CancellationToken));
    }

    /// <summary>Anmeldung gegen die Tabelle Zugriff - gleiche Prüfungen wie das bestehende doLogin().</summary>
    public sealed class LoginService : ILoginService
    {
        private readonly IWipSageRepository _repository;
        private readonly string _superUsersList;

        public LoginService(IWipSageRepository repository, IOptions<LoginOptions> options)
        {
            _repository = repository;
            _superUsersList = "," + (options.Value.SuperUsers ?? "").Replace(" ", "") + ",";
        }

        public async Task<LoginUser> TryLoginAsync(string userNrText, string password, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!long.TryParse((userNrText ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var userNr))
            {
                return null;
            }

            var details = await _repository.GetLoginDetailsAsync(userNr, cancellationToken);
            if (details == null)
            {
                return null; // unbekannt oder ausgetreten
            }

            // Das Passwort wird bei allen Benutzern geprüft, auch bei Superusern (wie in doLogin)
            if (!PwdMatch((password ?? "").Trim(), DecodePassword(details.Kennwort ?? "").Trim()))
            {
                return null;
            }

            // Rolle: Superuser aus der Konfiguration (Login:SuperUsers), sonst der Wert aus Zugriff.Laborleiter
            var isSuperUser = ",995,990,998,".Contains("," + userNr.ToString(CultureInfo.InvariantCulture) + ",");
            int role;
            if (isSuperUser)
            {
                role = 2;
            }
            else if (details.Laborleiter == 0m || details.Laborleiter == 1m)
            {
                role = (int)details.Laborleiter;
            }
            else
            {
                return null; // ohne Zugriff
            }

            return new LoginUser { UserNr = userNr, Kurzname = details.Kurzname, Role = role };
        }

        // TODO: VORLÄUFIG - die Routine pwdMatch() der bestehenden Anwendung liegt noch nicht vor.
        // Hier wird das eingegebene Passwort 1:1 mit Zugriff.Kennwort verglichen. Falls pwdMatch()
        // anders arbeitet (Hash, Verschlüsselung, Groß-/Kleinschreibung), muss diese Methode angepasst werden.
        private static bool PwdMatch(string entered, string stored)
        {
            return entered.Length > 0 && string.Equals(entered, stored, StringComparison.Ordinal);
        }

        private string DecodePassword(string pEncryp)
        {
            string retval = "";
            int ii = 0;
            while (ii < pEncryp.Length)
            {
                char c = Convert.ToChar(pEncryp.Substring(ii, 1));
                uint ui = ((((uint)c << 4) | ((uint)c >> 4)) & 0xff);
                switch (ui)
                {
                    case 2:
                        ui = 0x32;
                        break;
                    case 49:
                        ui = 0x4b;
                        break;
                    case 209:
                        ui = 0x6b;
                        break;
                    case 225:
                        ui = 0x6a;
                        break;
                    default:
                        ui ^= 0x22;
                        break;
                }
                c = Convert.ToChar(ui);
                retval += c.ToString().Trim();
                ii++;
            }
            while (retval.Length < 6)
            {
                retval = string.Format("2{0}", retval);
            }

            return retval;
        }
    }
}

