using System;
using Microsoft.AspNetCore.Http;

namespace WipSageWeb.Data
{
    /// <summary>
    /// Merkt sich die Seite, von der aus WipSageWeb aufgerufen wurde (z. B. das LADIS-Portal). Home-Symbol und
    /// Intertek-Logo im Kopf führen dorthin zurück.
    /// Quelle, in dieser Reihenfolge:
    ///  1. Parameter ?returnUrl=... beim Aufruf (vollständige http/https-Adresse) - die genaueste Angabe
    ///  2. Referer-Header, wenn er von einem anderen Server kommt. Browser schicken bei fremden Seiten meist nur
    ///     den Server-Teil ("http://portal:8080/"), nicht den ganzen Pfad.
    /// Die Adresse liegt in der Session und überlebt das Abmelden. Ohne gemerkte Seite gilt Login:ReturnUrl aus
    /// appsettings.json, sonst die eigene Startseite.
    /// </summary>
    public static class AufruferSeite
    {
        private const string Key = "WipSage.Aufrufer";

        public static void Merken(HttpContext context)
        {
            if (!HttpMethods.IsGet(context.Request.Method))
            {
                return;
            }

            var adresse = Gueltig(context.Request.Query["returnUrl"]);
            if (adresse == null)
            {
                var referer = Gueltig(context.Request.Headers["Referer"]);
                // Eigene Seiten (z. B. die History) zählen nicht als Aufrufer
                if (referer != null && !string.Equals(referer.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
                {
                    adresse = referer;
                }
            }
            if (adresse != null)
            {
                context.Session.SetString(Key, adresse.AbsoluteUri);
            }
        }

        public static string Lesen(ISession session) => session.GetString(Key);

        /// <summary>Nach Session.Clear (Abmelden) wieder eintragen.</summary>
        public static void Setzen(ISession session, string adresse)
        {
            if (!string.IsNullOrEmpty(adresse))
            {
                session.SetString(Key, adresse);
            }
        }

        private static Uri Gueltig(string wert)
        {
            return Uri.TryCreate((wert ?? "").Trim(), UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri
                : null;
        }
    }
}
