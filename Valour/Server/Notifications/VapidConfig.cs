using WebPush;

namespace Valour.Server.Notifications
{
    public class VapidConfig
    {
        public static VapidConfig Current;
        private static VapidDetails _details;

        public VapidConfig()
        {
            Current = this;
        }

        public VapidDetails GetDetails()
        {
            if (_details == null)
            {
                _details = new VapidDetails(Subject, PublicKey, PrivateKey);
            }

            return _details;
        }

        public string Subject { get; set; }

        public string PublicKey { get; set; }

        public string PrivateKey { get; set; }
    }
}
