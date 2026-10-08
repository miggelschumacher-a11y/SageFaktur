using System.ComponentModel.DataAnnotations;

namespace WipSageWeb.Models
{
    public class LoginViewModel
    {
        // string statt Zahl: eine nicht-numerische Eingabe gilt wie im bestehenden doLogin() als gescheiterte Anmeldung
        [Display(Name = "Benutzernummer")]
        [Required(ErrorMessage = "Bitte die Benutzernummer eingeben.")]
        public string UserNr { get; set; }

        [Display(Name = "Passwort")]
        [Required(ErrorMessage = "Bitte das Passwort eingeben.")]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }
}
