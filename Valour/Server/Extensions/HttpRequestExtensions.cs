namespace Valour.Server.Extensions
{
    public static class HttpRequestExtensions
    {
        public static async Task<string> ReadBodyStringAsync(this HttpRequest request)
        {
            string val;

            using (var reader = new StreamReader(request.Body))
            {
                val = await reader.ReadToEndAsync();
            }

            return val;
        }
    }
}
