using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace QuotaArc.Providers;

internal static class LocalhostHttp
{
    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = TrustLoopbackOnly;
        return new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
    }

    private static bool TrustLoopbackOnly(
        HttpRequestMessage message,
        X509Certificate2? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        var host = message.RequestUri?.Host;
        return host is "127.0.0.1" or "localhost" or "::1";
    }
}
