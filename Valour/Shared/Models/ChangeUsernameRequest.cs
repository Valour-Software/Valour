namespace Valour.Shared.Models
{
    public class ChangeUsernameRequest
    {
        public string NewUsername { get; set; }
        public string Password { get; set; }

        /// <summary>
        /// A recent proof of identity from api/users/me/reauth or from signing
        /// in again with a linked account. Accepted instead of the password.
        /// </summary>
        public string ReauthProof { get; set; }
    }
}
